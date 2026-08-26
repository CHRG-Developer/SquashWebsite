using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;
using SquashClub.Web.Services;

namespace SquashClub.Web.Api;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder routes)
    {
        var member = routes.MapGroup("/api/member").RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>();
        member.MapGet("/dashboard", Dashboard);
        member.MapGet("/availability", Availability);
        member.MapPost("/bookings", Book);
        member.MapPost("/bookings/{id:guid}/split/approve", ApproveSplit);
        member.MapPost("/bookings/{id:guid}/split/decline", DeclineSplit);
        member.MapPost("/bookings/{id:guid}/cancel", Cancel);
        member.MapPost("/bookings/{id:guid}/lights/on", LightsOn);
        member.MapPost("/bookings/{id:guid}/lights/off", LightsOff);
        member.MapPost("/cancellation-alerts", Subscribe);
        member.MapPost("/payments", BeginPayment);
        member.MapGet("/ladders", Ladders);
        member.MapPost("/ladders/{id:guid}/join", JoinLadder);
        member.MapPost("/ladders/{id:guid}/challenge", Challenge);
        member.MapPost("/challenges/{id:guid}/accept", AcceptChallenge);
        member.MapPost("/challenges/{id:guid}/result", SubmitResult);
        member.MapPost("/challenges/{id:guid}/confirm", ConfirmResult);

        var admin = routes.MapGroup("/api/admin").RequireAuthorization("Administrator")
            .AddEndpointFilter<AntiforgeryFilter>();
        admin.MapGet("/members", async (ClubDbContext db, CancellationToken ct) =>
            await db.Users.OrderBy(x => x.LastName).Select(x => new { x.Id, x.FirstName,
                x.LastName, x.Email, x.AccountEnabled, x.CreditBalanceUnits }).ToListAsync(ct));
        admin.MapPost("/members/{id:guid}/credits", AdminCredit);
        admin.MapPost("/courts", CreateCourt);
        admin.MapPut("/courts/{id:guid}", UpdateCourt);
        admin.MapPost("/closures", CreateClosure);
        admin.MapPost("/opening-hours", UpsertOpeningHours);
        admin.MapPost("/peak-periods", CreatePeakPeriod);
        admin.MapPost("/membership-products", CreateMembershipProduct);
        admin.MapPost("/credit-packages", CreateCreditPackage);
        admin.MapGet("/bookings", async (ClubDbContext db, CancellationToken ct) =>
            await db.Bookings.Include(x => x.Court).OrderByDescending(x => x.StartsAtUtc)
                .Take(500).ToListAsync(ct));
        admin.MapGet("/lighting", async (ClubDbContext db, CancellationToken ct) =>
            await db.Courts.Select(x => new { x.Id, x.Name, x.LightingEnabled,
                Session = db.LightingSessions.Where(s => s.CourtId == x.Id &&
                    (s.Status == LightingSessionStatus.On || s.Status == LightingSessionStatus.Pending))
                    .Select(s => new { s.BookingId, s.Status, s.ScheduledOffAtUtc }).FirstOrDefault() })
                .ToListAsync(ct));
        admin.MapPut("/settings/{key}", SetSetting);
        admin.MapGet("/audit", async (ClubDbContext db, CancellationToken ct) =>
            await db.AuditLogs.OrderByDescending(x => x.TimestampUtc).Take(500).ToListAsync(ct));
        return routes;
    }

    static Guid MemberId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException());

    static async Task<IResult> Dashboard(ClaimsPrincipal user, ClubDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var id = MemberId(user); var now = clock.GetUtcNow().UtcDateTime;
        var account = await db.Users.Where(x => x.Id == id).Select(x => new { x.FirstName,
            x.LastName, x.CreditBalanceUnits }).SingleAsync(ct);
        var membership = await db.Memberships.Where(x => x.MemberId == id && !x.Cancelled)
            .OrderByDescending(x => x.EndsAtUtc).Select(x => new { x.StartsAtUtc, x.EndsAtUtc,
                Product = x.Product.Name }).FirstOrDefaultAsync(ct);
        var bookings = await db.Bookings.Where(x => x.Status == BookingStatus.Confirmed &&
            x.EndsAtUtc > now && (x.PrimaryMemberId == id || x.OpponentMemberId == id))
            .OrderBy(x => x.StartsAtUtc).Select(x => new { x.Id, x.CourtId, Court = x.Court.Name,
                x.StartsAtUtc, x.EndsAtUtc, x.PaymentMode, x.SplitStatus }).ToListAsync(ct);
        var alerts = await db.CancellationSubscriptions.Where(x => x.MemberId == id && x.Active &&
            x.ExpiresAtUtc > now).ToListAsync(ct);
        var notifications = await db.Notifications.Where(x => x.MemberId == id)
            .OrderByDescending(x => x.CreatedAtUtc).Take(20).ToListAsync(ct);
        return Results.Ok(new { account, membership, bookings, alerts, notifications });
    }

    static async Task<IResult> Availability(DateOnly date, ICourtAvailabilityService availability,
        CancellationToken ct) => Results.Ok(await availability.GetAsync(date, ct));

    static async Task<IResult> Book(ClaimsPrincipal user, BookDto dto, IBookingService bookings,
        INotificationService notifications, CancellationToken ct)
    {
        try
        {
            var id = MemberId(user);
            var booking = await bookings.BookAsync(new(id, dto.CourtId, dto.StartsAtUtc,
                dto.OpponentMemberId, dto.PaymentMode), ct);
            await notifications.QueueAsync(id, NotificationType.BookingConfirmation,
                "Court booked", $"Your booking starts at {booking.StartsAtUtc:u}.", ct);
            if (dto.PaymentMode == PaymentMode.Split && dto.OpponentMemberId is not null)
                await notifications.QueueAsync(dto.OpponentMemberId.Value,
                    NotificationType.SplitApprovalRequest, "Split payment approval",
                    "Approve your court-credit share from your dashboard.", ct);
            return Results.Created($"/api/member/bookings/{booking.Id}", booking);
        }
        catch (ClubRuleException ex) { return RuleError(ex); }
    }

    static async Task<IResult> ApproveSplit(Guid id, ClaimsPrincipal user, IBookingService service,
        CancellationToken ct) { try { return Results.Ok(await service.ApproveSplitAsync(id, MemberId(user), ct)); }
        catch (ClubRuleException ex) { return RuleError(ex); } }

    static async Task<IResult> DeclineSplit(Guid id, ClaimsPrincipal user, ClubDbContext db,
        IBookingService service, CancellationToken ct)
    {
        try
        {
            var memberId = MemberId(user); var booking = await db.Bookings.SingleAsync(x => x.Id == id, ct);
            if (booking.OpponentMemberId != memberId) throw new ClubRuleException("FORBIDDEN", "Only the invitee can decline.");
            if (booking.SplitStatus != SplitStatus.AwaitingAcceptance) return Results.Ok();
            booking.SplitStatus = SplitStatus.Declined; await db.SaveChangesAsync(ct);
            await service.CancelAsync(id, booking.PrimaryMemberId, ct); return Results.Ok();
        }
        catch (ClubRuleException ex) { return RuleError(ex); }
    }

    static async Task<IResult> Cancel(Guid id, ClaimsPrincipal user, IBookingService service,
        ClubDbContext db, ICancellationNotificationService alerts, INotificationService notifications,
        CancellationToken ct)
    {
        try
        {
            var memberId = MemberId(user); await service.CancelAsync(id, memberId, ct);
            var booking = await db.Bookings.SingleAsync(x => x.Id == id, ct);
            await alerts.NotifyReleasedSlotAsync(booking, ct);
            await notifications.QueueAsync(memberId, NotificationType.CancellationConfirmation,
                "Booking cancelled", $"Refunded {booking.CreditsRefundedUnits / 100m:0.##} credits.", ct);
            return Results.Ok(new { booking.CreditsRefundedUnits });
        }
        catch (ClubRuleException ex) { return RuleError(ex); }
    }

    static async Task<IResult> LightsOn(Guid id, ClaimsPrincipal user, ICourtLightingService lights,
        CancellationToken ct) { try { return Results.Ok(await lights.TurnOnAsync(id, MemberId(user), false, ct)); }
        catch (ClubRuleException ex) { return RuleError(ex); } }
    static async Task<IResult> LightsOff(Guid id, ClaimsPrincipal user, ICourtLightingService lights,
        CancellationToken ct) { try { await lights.TurnOffAsync(id, MemberId(user), false, ct); return Results.Ok(); }
        catch (ClubRuleException ex) { return RuleError(ex); } }

    static async Task<IResult> Subscribe(ClaimsPrincipal user, AlertDto dto,
        ICancellationNotificationService alerts, CancellationToken ct)
    { try { return Results.Ok(new { id = await alerts.SubscribeAsync(MemberId(user), dto.Date,
        dto.Earliest, dto.Latest, dto.CourtId, ct) }); } catch (ClubRuleException ex) { return RuleError(ex); } }

    static async Task<IResult> BeginPayment(ClaimsPrincipal user, PaymentDto dto,
        PaymentService payments, CancellationToken ct)
    { var result = await payments.BeginAsync(MemberId(user), dto.Purpose, dto.ProductId, ct);
      return Results.Ok(new { result.Payment.Id, result.CheckoutUrl }); }

    static async Task<IResult> Ladders(ClubDbContext db, CancellationToken ct) => Results.Ok(
        await db.Ladders.Where(x => x.Active).Select(x => new { x.Id, x.Name,
            Standings = x.Participants.OrderBy(p => p.Rank).Select(p => new { p.MemberId, p.Rank }) })
            .ToListAsync(ct));
    static async Task<IResult> JoinLadder(Guid id, ClaimsPrincipal user, CompetitionService service,
        CancellationToken ct) { try { return Results.Ok(new { id = await service.JoinAsync(id, MemberId(user), ct) }); }
        catch (ClubRuleException ex) { return RuleError(ex); } }
    static async Task<IResult> Challenge(Guid id, ClaimsPrincipal user, ChallengeDto dto,
        CompetitionService service, CancellationToken ct) { try { return Results.Ok(await service.ChallengeAsync(id,
        MemberId(user), dto.DefenderMemberId, ct)); } catch (ClubRuleException ex) { return RuleError(ex); } }
    static async Task<IResult> AcceptChallenge(Guid id, ClaimsPrincipal user, CompetitionService service,
        CancellationToken ct) { try { await service.AcceptAsync(id, MemberId(user), ct); return Results.Ok(); }
        catch (ClubRuleException ex) { return RuleError(ex); } }
    static async Task<IResult> SubmitResult(Guid id, ClaimsPrincipal user, ResultDto dto,
        CompetitionService service, CancellationToken ct) { try { await service.SubmitResultAsync(id,
        MemberId(user), dto.WinnerMemberId, dto.Score, ct); return Results.Ok(); }
        catch (ClubRuleException ex) { return RuleError(ex); } }
    static async Task<IResult> ConfirmResult(Guid id, ClaimsPrincipal user, CompetitionService service,
        CancellationToken ct) { try { await service.ConfirmResultAsync(id, MemberId(user), ct); return Results.Ok(); }
        catch (ClubRuleException ex) { return RuleError(ex); } }

    static async Task<IResult> AdminCredit(Guid id, AdminCreditDto dto, ClaimsPrincipal user,
        ICreditService credits, ClubDbContext db, TimeProvider clock, CancellationToken ct)
    {
        try { var balance = await credits.AdjustAsync(id, dto.Units, CreditTransactionType.AdminAdjustment,
            dto.Reason, null, null, ct); db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = MemberId(user),
            Action = "CreditAdjusted", EntityType = "Member", EntityId = id.ToString(),
            TimestampUtc = clock.GetUtcNow().UtcDateTime, Detail = dto.Reason }); await db.SaveChangesAsync(ct);
            return Results.Ok(new { balance }); } catch (ClubRuleException ex) { return RuleError(ex); }
    }
    static async Task<IResult> CreateCourt(CourtDto dto, ClubDbContext db, CancellationToken ct)
    { var court = new Court { Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
        DisplayOrder = dto.DisplayOrder, Active = dto.Active, LightingEnabled = dto.LightingEnabled,
        LightingDeviceId = dto.LightingDeviceId }; db.Courts.Add(court); await db.SaveChangesAsync(ct);
      return Results.Created($"/api/admin/courts/{court.Id}", court); }
    static async Task<IResult> UpdateCourt(Guid id, CourtDto dto, ClubDbContext db, CancellationToken ct)
    { var court = await db.Courts.FindAsync([id], ct); if (court is null) return Results.NotFound();
      court.Name = dto.Name; court.Description = dto.Description; court.DisplayOrder = dto.DisplayOrder;
      court.Active = dto.Active; court.LightingEnabled = dto.LightingEnabled;
      court.LightingDeviceId = dto.LightingDeviceId; await db.SaveChangesAsync(ct); return Results.Ok(court); }
    static async Task<IResult> CreateClosure(ClosureDto dto, ClubDbContext db, CancellationToken ct)
    { if (dto.EndsAtUtc <= dto.StartsAtUtc) return Results.ValidationProblem(new Dictionary<string,string[]>{{"endsAtUtc",["End must follow start."]}});
      var closure = new CourtClosure { Id = Guid.NewGuid(), CourtId = dto.CourtId,
        StartsAtUtc = dto.StartsAtUtc, EndsAtUtc = dto.EndsAtUtc, Reason = dto.Reason };
      db.CourtClosures.Add(closure); await db.SaveChangesAsync(ct); return Results.Ok(closure); }
    static async Task<IResult> SetSetting(string key, SettingDto dto, ClubDbContext db, ClubOptions options,
        CancellationToken ct) { var value = await db.SystemSettings.FindAsync([key], ct);
      if (value is null) db.SystemSettings.Add(new() { Key = key, Value = dto.Value }); else value.Value = dto.Value;
      if (!ApplySetting(options, key, dto.Value)) return Results.BadRequest(new { error = "INVALID_SETTING" });
      await db.SaveChangesAsync(ct); return Results.Ok(); }
    static bool ApplySetting(ClubOptions options, string key, string value)
    {
      if (key == "ClubTimezone") { try { TimeZoneInfo.FindSystemTimeZoneById(value); options.TimeZone = value; return true; } catch { return false; } }
      if (!int.TryParse(value, out var number) || number <= 0) return false;
      switch (key) { case "SlotDurationMinutes": options.SlotMinutes = number; break;
        case "OffPeakCostUnits": options.OffPeakCostUnits = number; break;
        case "MaximumPeakBookingsPerMemberPerDay": options.MaximumPeakBookingsPerDay = number; break;
        case "CancellationRefundCutoffMinutes": options.CancellationRefundCutoffMinutes = number; break;
        case "SplitApprovalTimeoutMinutes": options.SplitTimeoutMinutes = number; break;
        case "LightActivationEarlyMinutes": options.LightEarlyMinutes = number; break;
        case "LightingDurationMinutes": options.LightDurationMinutes = number; break;
        default: return false; }
      return true;
    }
    static async Task<IResult> UpsertOpeningHours(OpeningHoursDto dto, ClubDbContext db,
        CancellationToken ct) { if (dto.Opens >= dto.Closes) return Results.BadRequest();
      var value = await db.OpeningHours.SingleOrDefaultAsync(x => x.Day == dto.Day, ct);
      if (value is null) db.OpeningHours.Add(new() { Id = Guid.NewGuid(), Day = dto.Day,
          Opens = dto.Opens, Closes = dto.Closes }); else { value.Opens = dto.Opens; value.Closes = dto.Closes; }
      await db.SaveChangesAsync(ct); return Results.Ok(); }
    static async Task<IResult> CreatePeakPeriod(PeakPeriodDto dto, ClubDbContext db,
        CancellationToken ct) { if (dto.Starts >= dto.Ends || dto.CostUnits <= 0) return Results.BadRequest();
      var value = new PeakPeriod { Id = Guid.NewGuid(), Day = dto.Day, Starts = dto.Starts,
          Ends = dto.Ends, CostUnits = dto.CostUnits }; db.PeakPeriods.Add(value);
      await db.SaveChangesAsync(ct); return Results.Ok(value); }
    static async Task<IResult> CreateMembershipProduct(MembershipProductDto dto, ClubDbContext db,
        CancellationToken ct) { if (dto.PriceCents < 0 || dto.DurationDays <= 0) return Results.BadRequest();
      var value = new MembershipProduct { Id = Guid.NewGuid(), Name = dto.Name,
          Description = dto.Description, PriceCents = dto.PriceCents, DurationDays = dto.DurationDays,
          BookingEntitlement = dto.BookingEntitlement, LadderEntitlement = dto.LadderEntitlement };
      db.MembershipProducts.Add(value); await db.SaveChangesAsync(ct); return Results.Ok(value); }
    static async Task<IResult> CreateCreditPackage(CreditPackageDto dto, ClubDbContext db,
        CancellationToken ct) { if (dto.Units <= 0 || dto.PriceCents < 0) return Results.BadRequest();
      var value = new CreditPackage { Id = Guid.NewGuid(), Name = dto.Name, Units = dto.Units,
          PriceCents = dto.PriceCents }; db.CreditPackages.Add(value); await db.SaveChangesAsync(ct);
      return Results.Ok(value); }
    static IResult RuleError(ClubRuleException ex) => Results.Conflict(new { error = ex.Code, message = ex.Message });
}

public sealed record BookDto(Guid CourtId, DateTime StartsAtUtc, Guid? OpponentMemberId, PaymentMode PaymentMode);
public sealed record AlertDto(DateOnly Date, TimeOnly Earliest, TimeOnly Latest, Guid? CourtId);
public sealed record PaymentDto(PaymentPurpose Purpose, Guid ProductId);
public sealed record ChallengeDto(Guid DefenderMemberId);
public sealed record ResultDto(Guid WinnerMemberId, string Score);
public sealed record AdminCreditDto(int Units, string Reason);
public sealed record CourtDto(string Name, string Description, int DisplayOrder, bool Active,
    bool LightingEnabled, Guid? LightingDeviceId);
public sealed record ClosureDto(Guid? CourtId, DateTime StartsAtUtc, DateTime EndsAtUtc, string Reason);
public sealed record SettingDto(string Value);
public sealed record OpeningHoursDto(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);
public sealed record PeakPeriodDto(DayOfWeek Day, TimeOnly Starts, TimeOnly Ends, int CostUnits);
public sealed record MembershipProductDto(string Name, string Description, int PriceCents,
    int DurationDays, bool BookingEntitlement, bool LadderEntitlement);
public sealed record CreditPackageDto(string Name, int Units, int PriceCents);
