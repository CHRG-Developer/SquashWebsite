using System.Data;
using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;

namespace SquashClub.Web.Services;

public sealed record AvailabilitySlot(Guid CourtId, string CourtName, DateTime StartsAtUtc,
    DateTime EndsAtUtc, bool IsPeak, int CostUnits, bool Available, string? Reason,
    string? BookedByMemberName = null);

public interface ICourtAvailabilityService
{
    Task<IReadOnlyList<AvailabilitySlot>> GetAsync(DateOnly localDate, CancellationToken ct = default);
}

public sealed class CourtAvailabilityService(ClubDbContext db, ClubOptions options)
    : ICourtAvailabilityService
{
    public async Task<IReadOnlyList<AvailabilitySlot>> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var opening = await db.OpeningHours.SingleOrDefaultAsync(x => x.Day == date.DayOfWeek, ct);
        if (opening is null) return [];

        var courts = await db.Courts.Where(x => x.Active).OrderBy(x => x.DisplayOrder).ToListAsync(ct);
        var localStart = date.ToDateTime(opening.Opens, DateTimeKind.Unspecified);
        var localClose = date.ToDateTime(opening.Closes, DateTimeKind.Unspecified);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, timezone);
        var utcClose = TimeZoneInfo.ConvertTimeToUtc(localClose, timezone);
        var bookings = await db.Bookings.Where(x => x.Status == BookingStatus.Confirmed &&
            x.StartsAtUtc < utcClose && x.EndsAtUtc > utcStart)
            .Join(db.Users, booking => booking.PrimaryMemberId, member => member.Id,
                (booking, member) => new { Booking = booking,
                    MemberName = member.FirstName + " " + member.LastName })
            .ToListAsync(ct);
        var closures = await db.CourtClosures.Where(x => x.StartsAtUtc < utcClose &&
            x.EndsAtUtc > utcStart).ToListAsync(ct);
        var peaks = await db.PeakPeriods.Where(x => x.Day == date.DayOfWeek).ToListAsync(ct);
        var result = new List<AvailabilitySlot>();

        for (var local = localStart; local.AddMinutes(options.SlotMinutes) <= localClose;
             local = local.AddMinutes(options.SlotMinutes))
        {
            var start = TimeZoneInfo.ConvertTimeToUtc(local, timezone);
            var end = TimeZoneInfo.ConvertTimeToUtc(local.AddMinutes(options.SlotMinutes), timezone);
            var time = TimeOnly.FromDateTime(local);
            var peak = peaks.FirstOrDefault(x => x.Starts <= time && x.Ends > time);
            foreach (var court in courts)
            {
                var closure = closures.Any(x => (x.CourtId is null || x.CourtId == court.Id) &&
                    x.StartsAtUtc < end && x.EndsAtUtc > start);
                var booking = bookings.FirstOrDefault(x => x.Booking.CourtId == court.Id &&
                    x.Booking.StartsAtUtc < end && x.Booking.EndsAtUtc > start);
                var booked = booking is not null;
                result.Add(new(court.Id, court.Name, start, end, peak is not null,
                    peak?.CostUnits ?? options.OffPeakCostUnits, !closure && !booked,
                    closure ? "Closed" : booked ? "Booked" : null,
                    booked ? booking!.MemberName : null));
            }
        }
        return result;
    }
}

public interface INotificationService
{
    Task QueueAsync(Guid memberId, NotificationType type, string subject, string body,
        CancellationToken ct = default);
}

public sealed class DatabaseNotificationService(ClubDbContext db, TimeProvider clock)
    : INotificationService
{
    public async Task QueueAsync(Guid memberId, NotificationType type, string subject,
        string body, CancellationToken ct = default)
    {
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), MemberId = memberId,
            Type = type, Subject = subject, Body = body, Status = NotificationStatus.Pending,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
    }
}

public interface ICancellationNotificationService
{
    Task<Guid> SubscribeAsync(Guid memberId, DateOnly date, TimeOnly earliest, TimeOnly latest,
        Guid? courtId, CancellationToken ct = default);
    Task NotifyReleasedSlotAsync(Booking booking, CancellationToken ct = default);
}

public sealed class CancellationNotificationService(ClubDbContext db,
    INotificationService notifications, ClubOptions options, TimeProvider clock)
    : ICancellationNotificationService
{
    public async Task<Guid> SubscribeAsync(Guid memberId, DateOnly date, TimeOnly earliest,
        TimeOnly latest, Guid? courtId, CancellationToken ct = default)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            clock.GetUtcNow().UtcDateTime, timezone));
        if (date != today || earliest >= latest)
            throw new ClubRuleException("INVALID_ALERT", "Alerts are only available for later today.");
        var expiry = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone);
        var subscription = new CancellationSubscription { Id = Guid.NewGuid(), MemberId = memberId,
            Date = date, Earliest = earliest, Latest = latest, CourtId = courtId,
            ExpiresAtUtc = expiry };
        db.CancellationSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        return subscription.Id;
    }

    public async Task NotifyReleasedSlotAsync(Booking booking, CancellationToken ct = default)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(booking.StartsAtUtc, timezone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            clock.GetUtcNow().UtcDateTime, timezone));
        if (DateOnly.FromDateTime(local) != today) return;
        var time = TimeOnly.FromDateTime(local);
        var matches = await db.CancellationSubscriptions.Where(x => x.Active &&
            x.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime && x.Date == today &&
            x.Earliest <= time && x.Latest >= time && (x.CourtId == null || x.CourtId == booking.CourtId))
            .ToListAsync(ct);
        foreach (var match in matches)
            await notifications.QueueAsync(match.MemberId, NotificationType.SameDayCancellationAlert,
                "A squash court is available", $"A court at {local:HH:mm} was released. It remains first-come-first-served.", ct);
    }
}

public interface IPaymentGateway
{
    Task<string> CreateCheckoutAsync(Guid paymentId, int amountCents, string currency,
        CancellationToken ct = default);
}
public sealed class DevelopmentPaymentGateway : IPaymentGateway
{
    public Task<string> CreateCheckoutAsync(Guid id, int amount, string currency,
        CancellationToken ct = default) => Task.FromResult($"development://checkout/{id}");
}

public sealed class PaymentService(ClubDbContext db, ICreditService credits,
    IPaymentGateway gateway, INotificationService notifications, TimeProvider clock)
{
    public async Task<(Payment Payment, string CheckoutUrl)> BeginAsync(Guid memberId,
        PaymentPurpose purpose, Guid productId, CancellationToken ct = default)
    {
        var amount = purpose == PaymentPurpose.Membership
            ? await db.MembershipProducts.Where(x => x.Id == productId && x.Active).Select(x => x.PriceCents).SingleAsync(ct)
            : await db.CreditPackages.Where(x => x.Id == productId && x.Active).Select(x => x.PriceCents).SingleAsync(ct);
        var payment = new Payment { Id = Guid.NewGuid(), MemberId = memberId, Purpose = purpose,
            ProductId = productId, AmountCents = amount, Status = PaymentStatus.Pending,
            ProviderPaymentId = $"pending-{Guid.NewGuid():N}", CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.Payments.Add(payment); await db.SaveChangesAsync(ct);
        return (payment, await gateway.CreateCheckoutAsync(payment.Id, amount, payment.Currency, ct));
    }

    public async Task ConfirmAsync(Guid paymentId, string providerEventId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.PaymentWebhookEvents.AnyAsync(x => x.Provider == "Stripe" &&
            x.ProviderEventId == providerEventId, ct)) return;
        var payment = await db.Payments.SingleAsync(x => x.Id == paymentId, ct);
        if (payment.Status == PaymentStatus.Succeeded) return;
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent { Id = Guid.NewGuid(), Provider = "Stripe",
            ProviderEventId = providerEventId, ReceivedAtUtc = clock.GetUtcNow().UtcDateTime, Processed = true });
        payment.Status = PaymentStatus.Succeeded; payment.ConfirmedAtUtc = clock.GetUtcNow().UtcDateTime;
        if (payment.Purpose == PaymentPurpose.CreditPackage)
        {
            var package = await db.CreditPackages.FindAsync([payment.ProductId], ct) ?? throw new InvalidOperationException();
            await credits.AdjustAsync(payment.MemberId, package.Units, CreditTransactionType.Purchase,
                $"Purchased {package.Name}", null, null, ct);
            await notifications.QueueAsync(payment.MemberId, NotificationType.CreditPurchaseConfirmation,
                "Credit purchase confirmed", $"{package.Units / 100m:0.##} credits were added.", ct);
        }
        else
        {
            var product = await db.MembershipProducts.FindAsync([payment.ProductId], ct) ?? throw new InvalidOperationException();
            var start = clock.GetUtcNow().UtcDateTime;
            var currentEnd = await db.Memberships.Where(x => x.MemberId == payment.MemberId && !x.Cancelled && x.EndsAtUtc > start)
                .Select(x => (DateTime?)x.EndsAtUtc).MaxAsync(ct);
            if (currentEnd is not null) start = currentEnd.Value;
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), MemberId = payment.MemberId,
                ProductId = product.Id, StartsAtUtc = start, EndsAtUtc = start.AddDays(product.DurationDays) });
            await notifications.QueueAsync(payment.MemberId, NotificationType.MembershipConfirmation,
                "Membership confirmed", $"Your {product.Name} membership is active.", ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
