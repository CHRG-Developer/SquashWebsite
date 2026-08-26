using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;

namespace SquashClub.Web.Services;

public interface IEmailNotificationSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct);
}
public sealed class DevelopmentEmailNotificationSender(ILogger<DevelopmentEmailNotificationSender> log)
    : IEmailNotificationSender
{
    public Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        log.LogInformation("Development email to {Recipient}: {Subject}", recipient, subject);
        return Task.CompletedTask;
    }
}

public sealed class NotificationDeliveryWorker(IServiceScopeFactory scopes, TimeProvider clock)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailNotificationSender>();
            var pending = await db.Notifications.Where(x => x.Status == NotificationStatus.Pending)
                .OrderBy(x => x.CreatedAtUtc).Take(50).ToListAsync(stoppingToken);
            foreach (var notification in pending)
            {
                var email = await db.Users.Where(x => x.Id == notification.MemberId)
                    .Select(x => x.Email).SingleAsync(stoppingToken);
                try
                {
                    await sender.SendAsync(email!, notification.Subject, notification.Body, stoppingToken);
                    notification.Status = NotificationStatus.Sent;
                    notification.SentAtUtc = clock.GetUtcNow().UtcDateTime;
                }
                catch { notification.Status = NotificationStatus.Failed; }
            }
            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}

public sealed class SplitExpirationWorker(IServiceScopeFactory scopes, TimeProvider clock)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var expired = await db.Bookings.Where(x => x.Status == BookingStatus.Confirmed &&
                x.SplitStatus == SplitStatus.AwaitingAcceptance &&
                x.SplitExpiresAtUtc <= clock.GetUtcNow().UtcDateTime).Select(x => x.Id)
                .ToListAsync(stoppingToken);
            foreach (var id in expired)
            {
                var booking = await db.Bookings.FindAsync([id], stoppingToken);
                if (booking is null) continue;
                booking.SplitStatus = SplitStatus.Expired; await db.SaveChangesAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<IBookingService>()
                    .CancelAsync(id, booking.PrimaryMemberId, stoppingToken);
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
