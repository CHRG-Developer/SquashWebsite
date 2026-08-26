using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Domain;
using SquashClub.Web.Services;
using Xunit;

namespace SquashClub.Tests;

public class FeatureServiceTests
{
    [Fact]
    public async Task Availability_honours_closures_and_server_costs()
    {
        await using var fixture = new Fixture(); await fixture.Init();
        var opening = await fixture.Db.OpeningHours.SingleAsync(); opening.Opens = new TimeOnly(12, 0); opening.Closes = new TimeOnly(14, 0);
        fixture.Db.CourtClosures.Add(new CourtClosure { Id = Guid.NewGuid(), CourtId = fixture.Court.Id,
            StartsAtUtc = fixture.Clock.Now, EndsAtUtc = fixture.Clock.Now.AddMinutes(45), Reason = "Maintenance" });
        await fixture.Db.SaveChangesAsync();
        var slots = await new CourtAvailabilityService(fixture.Db, fixture.Options)
            .GetAsync(DateOnly.FromDateTime(fixture.Clock.Now));
        Assert.False(slots.First().Available); Assert.Equal("Closed", slots.First().Reason);
        Assert.Equal(100, slots.Last().CostUnits);
    }

    [Fact]
    public async Task Same_day_released_slot_queues_alert_without_reserving_it()
    {
        await using var fixture = new Fixture(); await fixture.Init();
        var notifications = new DatabaseNotificationService(fixture.Db, fixture.Clock);
        var service = new CancellationNotificationService(fixture.Db, notifications,
            fixture.Options, fixture.Clock);
        await service.SubscribeAsync(fixture.Opponent.Id, DateOnly.FromDateTime(fixture.Clock.Now),
            new TimeOnly(12, 0), new TimeOnly(15, 0), null);
        var booking = await fixture.Booking().BookAsync(new(fixture.Member.Id, fixture.Court.Id,
            fixture.Clock.Now.AddHours(1)));
        await service.NotifyReleasedSlotAsync(booking);
        Assert.Single(await fixture.Db.Notifications.Where(x =>
            x.Type == NotificationType.SameDayCancellationAlert).ToListAsync());
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task Payment_confirmation_is_idempotent_and_activates_membership()
    {
        await using var fixture = new Fixture(); await fixture.Init(false);
        var notifications = new DatabaseNotificationService(fixture.Db, fixture.Clock);
        var service = new PaymentService(fixture.Db, new CreditService(fixture.Db, fixture.Clock),
            new DevelopmentPaymentGateway(), notifications, fixture.Clock);
        var product = await fixture.Db.MembershipProducts.SingleAsync();
        var started = await service.BeginAsync(fixture.Member.Id, PaymentPurpose.Membership, product.Id);
        await service.ConfirmAsync(started.Payment.Id, "evt-1");
        await service.ConfirmAsync(started.Payment.Id, "evt-1");
        Assert.Single(await fixture.Db.Memberships.ToListAsync());
        Assert.Single(await fixture.Db.PaymentWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task Credit_package_confirmation_uses_integer_ledger_units()
    {
        await using var fixture = new Fixture(); await fixture.Init();
        var package = new CreditPackage { Id = Guid.NewGuid(), Name = "Half credit",
            Units = 50, PriceCents = 250 };
        fixture.Db.CreditPackages.Add(package); await fixture.Db.SaveChangesAsync();
        var service = new PaymentService(fixture.Db, new CreditService(fixture.Db, fixture.Clock),
            new DevelopmentPaymentGateway(), new DatabaseNotificationService(fixture.Db, fixture.Clock),
            fixture.Clock);
        var started = await service.BeginAsync(fixture.Member.Id, PaymentPurpose.CreditPackage, package.Id);
        await service.ConfirmAsync(started.Payment.Id, "evt-credit");
        Assert.Equal(1050, fixture.Member.CreditBalanceUnits);
        Assert.Contains(await fixture.Db.CreditTransactions.ToListAsync(), x => x.Units == 50);
    }

    [Fact]
    public async Task Challenger_win_updates_ranking_and_audits_history()
    {
        await using var fixture = new Fixture(); await fixture.Init();
        var ladder = new Ladder { Id = Guid.NewGuid(), Name = "Test ladder" };
        fixture.Db.Ladders.Add(ladder);
        fixture.Db.LadderParticipants.AddRange(
            new LadderParticipant { Id = Guid.NewGuid(), LadderId = ladder.Id,
                MemberId = fixture.Opponent.Id, Rank = 1 },
            new LadderParticipant { Id = Guid.NewGuid(), LadderId = ladder.Id,
                MemberId = fixture.Member.Id, Rank = 2 });
        await fixture.Db.SaveChangesAsync();
        var service = new CompetitionService(fixture.Db,
            new DatabaseNotificationService(fixture.Db, fixture.Clock), fixture.Clock);
        var challenge = await service.ChallengeAsync(ladder.Id, fixture.Member.Id, fixture.Opponent.Id);
        await service.AcceptAsync(challenge.Id, fixture.Opponent.Id);
        await service.SubmitResultAsync(challenge.Id, fixture.Member.Id, fixture.Member.Id, "3-1");
        await service.ConfirmResultAsync(challenge.Id, fixture.Opponent.Id);
        Assert.Equal(1, await fixture.Db.LadderParticipants.Where(x =>
            x.MemberId == fixture.Member.Id).Select(x => x.Rank).SingleAsync());
        Assert.NotEmpty(await fixture.Db.LadderRankingHistory.ToListAsync());
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x =>
            x.Action == "LadderResultConfirmed");
    }
}
