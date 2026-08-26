using Microsoft.AspNetCore.Identity;

namespace SquashClub.Web.Domain;

public sealed class Member : IdentityUser<Guid>
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool AccountEnabled { get; set; } = true;
    public int CreditBalanceUnits { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public uint Version { get; set; }
    public ICollection<Membership> Memberships { get; set; } = [];
}
public sealed class MembershipProduct { public Guid Id { get; set; } public string Name { get; set; }=""; public string Description { get; set; }=""; public int PriceCents { get; set; } public int DurationDays { get; set; } public bool Active { get; set; }=true; public bool BookingEntitlement { get; set; }=true; public bool LadderEntitlement { get; set; }=true; }
public sealed class Membership { public Guid Id { get; set; } public Guid MemberId { get; set; } public Member Member { get; set; }=null!; public Guid ProductId { get; set; } public MembershipProduct Product { get; set; }=null!; public DateTime StartsAtUtc { get; set; } public DateTime EndsAtUtc { get; set; } public bool Cancelled { get; set; } }
public sealed class Court { public Guid Id { get; set; } public string Name { get; set; }=""; public string Description { get; set; }=""; public int DisplayOrder { get; set; } public bool Active { get; set; }=true; public bool LightingEnabled { get; set; } public Guid? LightingDeviceId { get; set; } }
public sealed class OpeningHour { public Guid Id { get; set; } public DayOfWeek Day { get; set; } public TimeOnly Opens { get; set; } public TimeOnly Closes { get; set; } }
public sealed class PeakPeriod { public Guid Id { get; set; } public DayOfWeek Day { get; set; } public TimeOnly Starts { get; set; } public TimeOnly Ends { get; set; } public int CostUnits { get; set; } }
public sealed class CourtClosure { public Guid Id { get; set; } public Guid? CourtId { get; set; } public DateTime StartsAtUtc { get; set; } public DateTime EndsAtUtc { get; set; } public string Reason { get; set; }=""; }
public enum BookingStatus { Confirmed, Cancelled, Completed, NoShow }
public enum PaymentMode { SingleMember, Split }
public enum SplitStatus { NotApplicable, AwaitingAcceptance, Accepted, Declined, Expired }
public sealed class Booking { public Guid Id { get; set; } public Guid PrimaryMemberId { get; set; } public Guid? OpponentMemberId { get; set; } public Guid CourtId { get; set; } public Court Court { get; set; }=null!; public DateTime StartsAtUtc { get; set; } public DateTime EndsAtUtc { get; set; } public BookingStatus Status { get; set; } public int CreditCostUnits { get; set; } public PaymentMode PaymentMode { get; set; } public SplitStatus SplitStatus { get; set; } public DateTime SplitExpiresAtUtc { get; set; } public DateTime CreatedAtUtc { get; set; } public DateTime? CancelledAtUtc { get; set; } public int CreditsRefundedUnits { get; set; } public ICollection<BookingPaymentShare> Shares { get; set; }=[]; }
public sealed class BookingPaymentShare { public Guid Id { get; set; } public Guid BookingId { get; set; } public Booking Booking { get; set; }=null!; public Guid MemberId { get; set; } public int Units { get; set; } public bool Paid { get; set; } public bool Refunded { get; set; } }
public enum CreditTransactionType { Purchase, Booking, CancellationRefund, ExternalDebit, AdminAdjustment }
public sealed class CreditTransaction { public Guid Id { get; set; } public Guid MemberId { get; set; } public int Units { get; set; } public CreditTransactionType Type { get; set; } public string Description { get; set; }=""; public Guid? BookingId { get; set; } public string? ExternalReference { get; set; } public DateTime CreatedAtUtc { get; set; } public int ResultingBalanceUnits { get; set; } public string Source { get; set; }=""; }
public sealed class CreditPackage { public Guid Id { get; set; } public string Name { get; set; }=""; public int Units { get; set; } public int PriceCents { get; set; } public bool Active { get; set; }=true; }
public sealed class CancellationSubscription { public Guid Id { get; set; } public Guid MemberId { get; set; } public DateOnly Date { get; set; } public TimeOnly Earliest { get; set; } public TimeOnly Latest { get; set; } public Guid? CourtId { get; set; } public bool Active { get; set; }=true; public DateTime ExpiresAtUtc { get; set; } }
public sealed class LightingDevice { public Guid Id { get; set; } public string Name { get; set; }=""; public string ProviderType { get; set; }="Mock"; public string NonSecretConfiguration { get; set; }="{}"; public bool Enabled { get; set; }=true; }
public enum LightingSessionStatus { Pending, On, Off, Failed }
public sealed class CourtLightingSession { public Guid Id { get; set; } public Guid BookingId { get; set; } public Guid CourtId { get; set; } public Guid? LightingDeviceId { get; set; } public Guid ActivatedByMemberId { get; set; } public DateTime ActivatedAtUtc { get; set; } public DateTime ScheduledOffAtUtc { get; set; } public DateTime? TurnedOffAtUtc { get; set; } public LightingSessionStatus Status { get; set; } public string? LastError { get; set; } }
public sealed class Ladder { public Guid Id { get; set; } public string Name { get; set; }=""; public bool Active { get; set; }=true; public int MaximumChallengeDistance { get; set; }=3; public ICollection<LadderParticipant> Participants { get; set; }=[]; }
public sealed class LadderParticipant { public Guid Id { get; set; } public Guid LadderId { get; set; } public Guid MemberId { get; set; } public int Rank { get; set; } }
public sealed class AuditLog { public Guid Id { get; set; } public Guid? ActorId { get; set; } public string Action { get; set; }=""; public string EntityType { get; set; }=""; public string EntityId { get; set; }=""; public DateTime TimestampUtc { get; set; } public string? Detail { get; set; } public string Source { get; set; }="Web"; }
public sealed class SystemSetting { public string Key { get; set; }=""; public string Value { get; set; }=""; }
