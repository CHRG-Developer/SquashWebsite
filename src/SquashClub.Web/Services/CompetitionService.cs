using System.Data;
using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;

namespace SquashClub.Web.Services;

public sealed class CompetitionService(ClubDbContext db, INotificationService notifications,
    TimeProvider clock)
{
    public async Task<Guid> JoinAsync(Guid ladderId, Guid memberId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var entitled = await db.Memberships.AnyAsync(x => x.MemberId == memberId && !x.Cancelled &&
            x.StartsAtUtc <= now && x.EndsAtUtc > now && x.Product.LadderEntitlement, ct);
        if (!entitled) throw new ClubRuleException("MEMBERSHIP_INACTIVE", "Active ladder membership required.");
        var existing = await db.LadderParticipants.SingleOrDefaultAsync(x =>
            x.LadderId == ladderId && x.MemberId == memberId, ct);
        if (existing is not null) return existing.Id;
        var rank = (await db.LadderParticipants.Where(x => x.LadderId == ladderId)
            .MaxAsync(x => (int?)x.Rank, ct) ?? 0) + 1;
        var participant = new LadderParticipant { Id = Guid.NewGuid(), LadderId = ladderId,
            MemberId = memberId, Rank = rank };
        db.LadderParticipants.Add(participant); await db.SaveChangesAsync(ct); return participant.Id;
    }

    public async Task<LadderChallenge> ChallengeAsync(Guid ladderId, Guid challengerId,
        Guid defenderId, CancellationToken ct = default)
    {
        var ladder = await db.Ladders.SingleAsync(x => x.Id == ladderId && x.Active, ct);
        var players = await db.LadderParticipants.Where(x => x.LadderId == ladderId &&
            (x.MemberId == challengerId || x.MemberId == defenderId)).ToListAsync(ct);
        if (players.Count != 2) throw new ClubRuleException("NOT_PARTICIPANT", "Both players must participate.");
        var challenger = players.Single(x => x.MemberId == challengerId);
        var defender = players.Single(x => x.MemberId == defenderId);
        if (challenger.Rank <= defender.Rank || challenger.Rank - defender.Rank > ladder.MaximumChallengeDistance)
            throw new ClubRuleException("INVALID_CHALLENGE", "Player is outside the challenge range.");
        var challenge = new LadderChallenge { Id = Guid.NewGuid(), LadderId = ladderId,
            ChallengerMemberId = challengerId, DefenderMemberId = defenderId,
            Status = LadderChallengeStatus.Pending, CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddDays(14) };
        db.LadderChallenges.Add(challenge); await db.SaveChangesAsync(ct);
        await notifications.QueueAsync(defenderId, NotificationType.LadderChallenge,
            "New ladder challenge", "A ladder player has challenged you.", ct);
        return challenge;
    }

    public async Task AcceptAsync(Guid challengeId, Guid defenderId, CancellationToken ct = default)
    {
        var challenge = await db.LadderChallenges.SingleAsync(x => x.Id == challengeId, ct);
        if (challenge.DefenderMemberId != defenderId) throw new ClubRuleException("FORBIDDEN", "Only the defender can accept.");
        if (challenge.Status != LadderChallengeStatus.Pending || challenge.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
            throw new ClubRuleException("CHALLENGE_EXPIRED", "Challenge is no longer active.");
        challenge.Status = LadderChallengeStatus.Accepted; await db.SaveChangesAsync(ct);
    }

    public async Task SubmitResultAsync(Guid challengeId, Guid submitterId, Guid winnerId,
        string score, CancellationToken ct = default)
    {
        var challenge = await db.LadderChallenges.SingleAsync(x => x.Id == challengeId, ct);
        if (submitterId != challenge.ChallengerMemberId && submitterId != challenge.DefenderMemberId)
            throw new ClubRuleException("FORBIDDEN", "Only a challenge player can submit a result.");
        if (challenge.Status != LadderChallengeStatus.Accepted ||
            (winnerId != challenge.ChallengerMemberId && winnerId != challenge.DefenderMemberId))
            throw new ClubRuleException("INVALID_RESULT", "Result is invalid.");
        db.LadderMatches.Add(new LadderMatch { Id = Guid.NewGuid(), ChallengeId = challengeId,
            WinnerMemberId = winnerId, Score = score, PlayedAtUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmResultAsync(Guid challengeId, Guid confirmerId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var challenge = await db.LadderChallenges.SingleAsync(x => x.Id == challengeId, ct);
        var match = await db.LadderMatches.SingleAsync(x => x.ChallengeId == challengeId, ct);
        if (confirmerId != challenge.ChallengerMemberId && confirmerId != challenge.DefenderMemberId)
            throw new ClubRuleException("FORBIDDEN", "Only a challenge player can confirm.");
        if (match.Confirmed) return;
        var players = await db.LadderParticipants.Where(x => x.LadderId == challenge.LadderId).ToListAsync(ct);
        var challenger = players.Single(x => x.MemberId == challenge.ChallengerMemberId);
        var defender = players.Single(x => x.MemberId == challenge.DefenderMemberId);
        if (match.WinnerMemberId == challenge.ChallengerMemberId)
        {
            var oldChallengerRank = challenger.Rank;
            var oldDefenderRank = defender.Rank;
            var shifted = players.Where(x => x.Rank >= oldDefenderRank && x.Rank < oldChallengerRank)
                .Select(x => (Player: x, OldRank: x.Rank)).ToList();
            foreach (var item in shifted) item.Player.Rank = -item.OldRank;
            challenger.Rank = -oldChallengerRank;
            await db.SaveChangesAsync(ct);
            foreach (var item in shifted)
            {
                var player = item.Player; player.Rank = item.OldRank + 1;
                db.LadderRankingHistory.Add(new() { Id = Guid.NewGuid(), LadderId = challenge.LadderId,
                    MemberId = player.MemberId, OldRank = item.OldRank, NewRank = player.Rank,
                    MatchId = match.Id, CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
            }
            challenger.Rank = oldDefenderRank;
            db.LadderRankingHistory.Add(new() { Id = Guid.NewGuid(), LadderId = challenge.LadderId,
                MemberId = challenger.MemberId, OldRank = oldChallengerRank, NewRank = challenger.Rank,
                MatchId = match.Id, CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
        }
        match.Confirmed = true; challenge.Status = LadderChallengeStatus.Completed;
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = confirmerId,
            Action = "LadderResultConfirmed", EntityType = "LadderChallenge",
            EntityId = challengeId.ToString(), TimestampUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
