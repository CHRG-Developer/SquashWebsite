using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;

namespace SquashClub.Web.Services;

public interface ILightingProvider
{
    Task TurnOnAsync(Guid courtId, CancellationToken ct);
    Task TurnOffAsync(Guid courtId, CancellationToken ct);
    Task<bool> IsOnAsync(Guid courtId, CancellationToken ct);
}

public sealed class MockLightingProvider : ILightingProvider
{
    readonly HashSet<Guid> enabled = [];
    public Task TurnOnAsync(Guid id, CancellationToken ct) { lock (enabled) enabled.Add(id); return Task.CompletedTask; }
    public Task TurnOffAsync(Guid id, CancellationToken ct) { lock (enabled) enabled.Remove(id); return Task.CompletedTask; }
    public Task<bool> IsOnAsync(Guid id, CancellationToken ct) { lock (enabled) return Task.FromResult(enabled.Contains(id)); }
}

public interface ICourtLightingService
{
    Task<CourtLightingSession> TurnOnAsync(Guid bookingId, Guid actorId, bool isAdmin = false,
        CancellationToken ct = default);
    Task TurnOffAsync(Guid bookingId, Guid actorId, bool isAdmin = false, CancellationToken ct = default);
    Task ReconcileAsync(CancellationToken ct = default);
}

public sealed class CourtLightingService(ClubDbContext db, ILightingProvider provider,
    ClubOptions options, TimeProvider clock) : ICourtLightingService
{
    public async Task<CourtLightingSession> TurnOnAsync(Guid bookingId, Guid actorId,
        bool isAdmin = false, CancellationToken ct = default)
    {
        var booking = await db.Bookings.Include(x => x.Court).SingleAsync(x => x.Id == bookingId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        Authorize(booking, actorId, isAdmin);
        var existing = await ActiveSession(bookingId, ct);
        if (existing is not null) return existing;
        if (booking.Status != BookingStatus.Confirmed || !booking.Court.LightingEnabled ||
            now < booking.StartsAtUtc.AddMinutes(-options.LightEarlyMinutes) || now >= booking.EndsAtUtc)
            throw new ClubRuleException("LIGHTS_NOT_AVAILABLE", "Lights are not available for this booking.");

        var session = new CourtLightingSession { Id = Guid.NewGuid(), BookingId = booking.Id,
            CourtId = booking.CourtId, LightingDeviceId = booking.Court.LightingDeviceId,
            ActivatedByMemberId = actorId, ActivatedAtUtc = now,
            ScheduledOffAtUtc = new[] { now.AddMinutes(options.LightDurationMinutes), booking.EndsAtUtc }.Min(),
            Status = LightingSessionStatus.Pending };
        db.LightingSessions.Add(session);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(session).State = EntityState.Detached;
            var concurrentSession = await ActiveSession(bookingId, ct);
            if (concurrentSession is not null) return concurrentSession;
            throw;
        }

        try
        {
            await provider.TurnOnAsync(booking.CourtId, ct);
            session.Status = LightingSessionStatus.On;
            Audit("LightsOn", session, actorId, null);
        }
        catch (Exception ex)
        {
            session.Status = LightingSessionStatus.Failed;
            session.LastError = ex.Message;
            Audit("LightsOnFailed", session, actorId, "Device command failed");
        }
        await db.SaveChangesAsync(ct);
        if (session.Status == LightingSessionStatus.Failed)
            throw new ClubRuleException("LIGHTING_FAILURE", "Unable to turn on the court lights.");
        return session;
    }

    public async Task TurnOffAsync(Guid bookingId, Guid actorId, bool isAdmin = false,
        CancellationToken ct = default)
    {
        var booking = await db.Bookings.SingleAsync(x => x.Id == bookingId, ct);
        Authorize(booking, actorId, isAdmin);
        var session = await ActiveSession(bookingId, ct);
        if (session is null) return;
        await provider.TurnOffAsync(session.CourtId, ct);
        session.Status = LightingSessionStatus.Off;
        session.TurnedOffAtUtc = clock.GetUtcNow().UtcDateTime;
        Audit("LightsOff", session, actorId, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var overdue = await db.LightingSessions.Where(x =>
            (x.Status == LightingSessionStatus.On || x.Status == LightingSessionStatus.Pending) &&
            x.ScheduledOffAtUtc <= now).ToListAsync(ct);
        foreach (var session in overdue)
        {
            try
            {
                await provider.TurnOffAsync(session.CourtId, ct);
                session.Status = LightingSessionStatus.Off; session.TurnedOffAtUtc = now;
                Audit("LightsAutoOff", session, null, null);
            }
            catch (Exception ex)
            {
                session.LastError = ex.Message;
                Audit("LightsAutoOffFailed", session, null, "Device command failed");
            }
        }
        await db.SaveChangesAsync(ct);
    }

    Task<CourtLightingSession?> ActiveSession(Guid bookingId, CancellationToken ct) =>
        db.LightingSessions.SingleOrDefaultAsync(x => x.BookingId == bookingId &&
            (x.Status == LightingSessionStatus.On || x.Status == LightingSessionStatus.Pending), ct);

    static void Authorize(Booking booking, Guid actorId, bool admin)
    {
        if (!admin && actorId != booking.PrimaryMemberId && actorId != booking.OpponentMemberId)
            throw new ClubRuleException("FORBIDDEN", "Not a booking participant.");
        if (!admin && actorId == booking.OpponentMemberId && booking.PaymentMode == PaymentMode.Split &&
            booking.SplitStatus != SplitStatus.Accepted)
            throw new ClubRuleException("FORBIDDEN", "Split payment must be accepted first.");
    }

    void Audit(string action, CourtLightingSession session, Guid? actor, string? detail) =>
        db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = actor, Action = action,
            EntityType = "CourtLightingSession", EntityId = session.Id.ToString(),
            TimestampUtc = clock.GetUtcNow().UtcDateTime, Detail = detail, Source = "Lighting" });
}

public sealed class LightingReconciliationWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICourtLightingService>()
                .ReconcileAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
