namespace SquashClub.Web.Domain;

public enum PaymentPurpose { Membership, CreditPackage }
public enum PaymentStatus { Pending, Succeeded, Failed, Refunded }
public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public PaymentPurpose Purpose { get; set; }
    public Guid ProductId { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Provider { get; set; } = "Stripe";
    public string ProviderPaymentId { get; set; } = "";
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
}

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderEventId { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
    public bool Processed { get; set; }
}

public enum CancellationResult { Refund, NoRefund }
public sealed class BookingCancellation
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid CancelledByMemberId { get; set; }
    public string Reason { get; set; } = "";
    public CancellationResult Result { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum NotificationType
{
    MembershipConfirmation, CreditPurchaseConfirmation, BookingConfirmation,
    SplitApprovalRequest, CancellationConfirmation, SameDayCancellationAlert,
    LadderChallenge, LadderResultConfirmation, MembershipExpiryReminder
}
public enum NotificationStatus { Pending, Sent, Failed }
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public NotificationType Type { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public NotificationStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}

public enum LadderChallengeStatus { Pending, Accepted, Completed, Cancelled, Expired }
public sealed class LadderChallenge
{
    public Guid Id { get; set; }
    public Guid LadderId { get; set; }
    public Guid ChallengerMemberId { get; set; }
    public Guid DefenderMemberId { get; set; }
    public LadderChallengeStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
public sealed class LadderMatch
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid WinnerMemberId { get; set; }
    public string Score { get; set; } = "";
    public bool Confirmed { get; set; }
    public DateTime PlayedAtUtc { get; set; }
}
public sealed class LadderRankingHistory
{
    public Guid Id { get; set; }
    public Guid LadderId { get; set; }
    public Guid MemberId { get; set; }
    public int OldRank { get; set; }
    public int NewRank { get; set; }
    public Guid MatchId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ExternalApiClient
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
