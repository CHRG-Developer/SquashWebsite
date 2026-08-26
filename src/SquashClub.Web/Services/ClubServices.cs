using System.Data;
using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;

namespace SquashClub.Web.Services;
public sealed class ClubOptions
{
 public ClubOptions(string TimeZone="Europe/Dublin", int SlotMinutes=45, int OffPeakCostUnits=100, int MaximumPeakBookingsPerDay=1, int CancellationRefundCutoffMinutes=120, int SplitTimeoutMinutes=30, int LightEarlyMinutes=5, int LightDurationMinutes=45) { this.TimeZone=TimeZone; this.SlotMinutes=SlotMinutes; this.OffPeakCostUnits=OffPeakCostUnits; this.MaximumPeakBookingsPerDay=MaximumPeakBookingsPerDay; this.CancellationRefundCutoffMinutes=CancellationRefundCutoffMinutes; this.SplitTimeoutMinutes=SplitTimeoutMinutes; this.LightEarlyMinutes=LightEarlyMinutes; this.LightDurationMinutes=LightDurationMinutes; }
 public string TimeZone { get; set; } public int SlotMinutes { get; set; } public int OffPeakCostUnits { get; set; } public int MaximumPeakBookingsPerDay { get; set; } public int CancellationRefundCutoffMinutes { get; set; } public int SplitTimeoutMinutes { get; set; } public int LightEarlyMinutes { get; set; } public int LightDurationMinutes { get; set; }
}
public sealed record BookingRequest(Guid MemberId, Guid CourtId, DateTime StartsAtUtc, Guid? OpponentId=null, PaymentMode PaymentMode=PaymentMode.SingleMember, bool AdminOverride=false);
public sealed class ClubRuleException(string code, string message) : Exception(message) { public string Code { get; }=code; }

public interface ICreditService { Task<int> AdjustAsync(Guid memberId,int units,CreditTransactionType type,string description,Guid? bookingId=null,string? externalReference=null,CancellationToken ct=default); Task<CreditTransaction> ExternalDebitAsync(Guid memberId,int units,string externalId,string description,CancellationToken ct=default); }
public sealed class CreditService(ClubDbContext db, TimeProvider clock) : ICreditService
{
 public async Task<int> AdjustAsync(Guid memberId,int units,CreditTransactionType type,string description,Guid? bookingId=null,string? externalReference=null,CancellationToken ct=default) {
  if(units==0) throw new ClubRuleException("INVALID_AMOUNT","Credit movement cannot be zero.");
  var member=await db.Users.SingleOrDefaultAsync(x=>x.Id==memberId,ct)??throw new ClubRuleException("MEMBER_NOT_FOUND","Member does not exist.");
  if(member.CreditBalanceUnits+units<0) throw new ClubRuleException("INSUFFICIENT_CREDITS","Insufficient credits.");
  member.CreditBalanceUnits+=units;
  db.CreditTransactions.Add(new(){Id=Guid.NewGuid(),MemberId=memberId,Units=units,Type=type,Description=description,BookingId=bookingId,ExternalReference=externalReference,CreatedAtUtc=clock.GetUtcNow().UtcDateTime,ResultingBalanceUnits=member.CreditBalanceUnits,Source="Application"});
  await db.SaveChangesAsync(ct); return member.CreditBalanceUnits;
 }
 public async Task<CreditTransaction> ExternalDebitAsync(Guid memberId,int units,string externalId,string description,CancellationToken ct=default) {
  if(units<=0||string.IsNullOrWhiteSpace(externalId)) throw new ClubRuleException("INVALID_AMOUNT","A positive amount and transaction id are required.");
  var existing=await db.CreditTransactions.SingleOrDefaultAsync(x=>x.ExternalReference==externalId,ct); if(existing is not null)return existing;
  await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
  existing=await db.CreditTransactions.SingleOrDefaultAsync(x=>x.ExternalReference==externalId,ct); if(existing is not null){await tx.CommitAsync(ct);return existing;}
  await AdjustAsync(memberId,-units,CreditTransactionType.ExternalDebit,description,null,externalId,ct);
  await tx.CommitAsync(ct); return await db.CreditTransactions.SingleAsync(x=>x.ExternalReference==externalId,ct);
 }
}
public interface IBookingService { Task<Booking> BookAsync(BookingRequest request,CancellationToken ct=default); Task<Booking> ApproveSplitAsync(Guid bookingId,Guid memberId,CancellationToken ct=default); Task CancelAsync(Guid bookingId,Guid actorId,CancellationToken ct=default); }
public sealed class BookingService(ClubDbContext db, ICreditService credits, ClubOptions options, TimeProvider clock) : IBookingService
{
 public async Task<Booking> BookAsync(BookingRequest r,CancellationToken ct=default) {
  var now=clock.GetUtcNow().UtcDateTime; var end=r.StartsAtUtc.AddMinutes(options.SlotMinutes);
  await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
  if(!await IsActive(r.MemberId,now,ct))throw new ClubRuleException("MEMBERSHIP_INACTIVE","An active membership is required.");
  if(r.StartsAtUtc<=now)throw new ClubRuleException("INVALID_SLOT","Bookings must be in the future.");
  var local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.StartsAtUtc,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));
  var opening=await db.OpeningHours.SingleOrDefaultAsync(x=>x.Day==local.DayOfWeek,ct);var localTime=TimeOnly.FromDateTime(local);
  if(opening is null||localTime<opening.Opens||localTime.AddMinutes(options.SlotMinutes)>opening.Closes)throw new ClubRuleException("INVALID_SLOT","Slot is outside configured opening hours.");
  if(!await db.Courts.AnyAsync(x=>x.Id==r.CourtId&&x.Active,ct))throw new ClubRuleException("COURT_UNAVAILABLE","Court is unavailable.");
  if(await db.CourtClosures.AnyAsync(x=>(x.CourtId==null||x.CourtId==r.CourtId)&&x.StartsAtUtc<end&&x.EndsAtUtc>r.StartsAtUtc,ct))throw new ClubRuleException("COURT_CLOSED","Court is closed.");
  if(await db.Bookings.AnyAsync(x=>x.CourtId==r.CourtId&&x.Status==BookingStatus.Confirmed&&x.StartsAtUtc<end&&x.EndsAtUtc>r.StartsAtUtc,ct))throw new ClubRuleException("SLOT_UNAVAILABLE","Slot is no longer available.");
  var peak=await IsPeak(r.StartsAtUtc,ct); if(peak&&!r.AdminOverride) { var (from,to)=LocalDayBounds(r.StartsAtUtc); var starts=await db.Bookings.Where(x=>x.PrimaryMemberId==r.MemberId&&x.Status==BookingStatus.Confirmed&&x.StartsAtUtc>=from&&x.StartsAtUtc<to).Select(x=>x.StartsAtUtc).ToListAsync(ct); var count=0; foreach(var start in starts) if(await IsPeak(start,ct)) count++; if(count>=options.MaximumPeakBookingsPerDay)throw new ClubRuleException("PEAK_LIMIT","Daily peak booking limit reached."); }
  var cost=peak ? await PeakCost(r.StartsAtUtc,ct) : options.OffPeakCostUnits;
  if(r.PaymentMode==PaymentMode.Split&&r.OpponentId is null)throw new ClubRuleException("OPPONENT_REQUIRED","Split payment requires a second player.");
  var booking=new Booking{Id=Guid.NewGuid(),PrimaryMemberId=r.MemberId,OpponentMemberId=r.OpponentId,CourtId=r.CourtId,StartsAtUtc=r.StartsAtUtc,EndsAtUtc=end,Status=BookingStatus.Confirmed,CreditCostUnits=cost,PaymentMode=r.PaymentMode,SplitStatus=r.PaymentMode==PaymentMode.Split?SplitStatus.AwaitingAcceptance:SplitStatus.NotApplicable,SplitExpiresAtUtc=now.AddMinutes(options.SplitTimeoutMinutes),CreatedAtUtc=now}; db.Bookings.Add(booking);
  var first=r.PaymentMode==PaymentMode.Split?(cost+1)/2:cost; booking.Shares.Add(new(){Id=Guid.NewGuid(),MemberId=r.MemberId,Units=first,Paid=true});
  if(r.PaymentMode==PaymentMode.Split)booking.Shares.Add(new(){Id=Guid.NewGuid(),MemberId=r.OpponentId!.Value,Units=cost-first,Paid=false});
  await credits.AdjustAsync(r.MemberId,-first,CreditTransactionType.Booking,"Court booking",booking.Id,null,ct);
  db.AuditLogs.Add(new(){Id=Guid.NewGuid(),ActorId=r.MemberId,Action="BookingCreated",EntityType="Booking",EntityId=booking.Id.ToString(),TimestampUtc=now});
  try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); } catch(DbUpdateException){throw new ClubRuleException("SLOT_UNAVAILABLE","Slot is no longer available.");} return booking;
 }
 public async Task<Booking> ApproveSplitAsync(Guid id,Guid memberId,CancellationToken ct=default) { await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct); var b=await db.Bookings.Include(x=>x.Shares).SingleAsync(x=>x.Id==id,ct); if(b.OpponentMemberId!=memberId)throw new ClubRuleException("FORBIDDEN","Only the invited member may approve."); if(b.SplitStatus==SplitStatus.Accepted)return b; if(b.SplitStatus!=SplitStatus.AwaitingAcceptance||b.SplitExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)throw new ClubRuleException("INVITATION_EXPIRED","Invitation is not active."); if(!await IsActive(memberId,clock.GetUtcNow().UtcDateTime,ct))throw new ClubRuleException("MEMBERSHIP_INACTIVE","An active membership is required."); var share=b.Shares.Single(x=>x.MemberId==memberId); await credits.AdjustAsync(memberId,-share.Units,CreditTransactionType.Booking,"Split court booking",b.Id,null,ct); share.Paid=true;b.SplitStatus=SplitStatus.Accepted;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return b; }
 public async Task CancelAsync(Guid id,Guid actorId,CancellationToken ct=default) { await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);var b=await db.Bookings.Include(x=>x.Shares).SingleAsync(x=>x.Id==id,ct);if(actorId!=b.PrimaryMemberId&&actorId!=b.OpponentMemberId)throw new ClubRuleException("FORBIDDEN","Not a participant.");if(b.Status==BookingStatus.Cancelled)return;b.Status=BookingStatus.Cancelled;b.CancelledAtUtc=clock.GetUtcNow().UtcDateTime;if(b.StartsAtUtc-b.CancelledAtUtc.Value>=TimeSpan.FromMinutes(options.CancellationRefundCutoffMinutes)){foreach(var s in b.Shares.Where(x=>x.Paid&&!x.Refunded)){await credits.AdjustAsync(s.MemberId,s.Units,CreditTransactionType.CancellationRefund,"Booking cancellation refund",b.Id,null,ct);s.Refunded=true;b.CreditsRefundedUnits+=s.Units;}}db.BookingCancellations.Add(new(){Id=Guid.NewGuid(),BookingId=id,CancelledByMemberId=actorId,Result=b.CreditsRefundedUnits>0?CancellationResult.Refund:CancellationResult.NoRefund,CreatedAtUtc=clock.GetUtcNow().UtcDateTime});db.AuditLogs.Add(new(){Id=Guid.NewGuid(),ActorId=actorId,Action="BookingCancelled",EntityType="Booking",EntityId=id.ToString(),TimestampUtc=clock.GetUtcNow().UtcDateTime});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct); }
 async Task<bool> IsActive(Guid id,DateTime at,CancellationToken ct)=>await db.Users.AnyAsync(x=>x.Id==id&&x.AccountEnabled,ct)&&await db.Memberships.AnyAsync(x=>x.MemberId==id&&!x.Cancelled&&x.StartsAtUtc<=at&&x.EndsAtUtc>at&&x.Product.BookingEntitlement,ct);
 async Task<bool> IsPeak(DateTime utc,CancellationToken ct){var local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));return await db.PeakPeriods.AnyAsync(x=>x.Day==local.DayOfWeek&&x.Starts<=TimeOnly.FromDateTime(local)&&x.Ends>TimeOnly.FromDateTime(local),ct);}
 async Task<int> PeakCost(DateTime utc,CancellationToken ct){var local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));return (await db.PeakPeriods.Where(x=>x.Day==local.DayOfWeek&&x.Starts<=TimeOnly.FromDateTime(local)&&x.Ends>TimeOnly.FromDateTime(local)).Select(x=>x.CostUnits).FirstAsync(ct));}
 (DateTime,DateTime) LocalDayBounds(DateTime utc){var tz=TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);var local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),tz).Date;return(TimeZoneInfo.ConvertTimeToUtc(local,tz),TimeZoneInfo.ConvertTimeToUtc(local.AddDays(1),tz));}
}
