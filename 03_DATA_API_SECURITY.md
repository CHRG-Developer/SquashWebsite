# Data Model, APIs and Security

## 1. Suggested Domain Entities

Implement entities broadly equivalent to:

```text
User
Member
Role

MembershipProduct
Membership
Payment

Court
CourtOpeningHours
CourtPeakPeriod
CourtClosure

Booking
BookingParticipant
BookingPaymentShare
BookingCancellation

CreditProduct
CreditTransaction

CancellationSubscription
Notification

Ladder
LadderParticipant
LadderChallenge
LadderMatch
LadderRankingHistory

LightingDevice
CourtLightingSession

ExternalApiClient
WebhookTransaction

AuditLog
SystemSetting
```

Use proper foreign keys and database constraints.

---

## 2. Suggested Booking Fields

```text
Id
PrimaryMemberId
OpponentMemberId
CourtId
StartDateTimeUtc
EndDateTimeUtc
Status
CreditCostUnits
PaymentMode
SplitPaymentStatus
CreatedAtUtc
CancelledAtUtc
CancelledBy
CancellationReason
CreditsRefundedUnits
```

Booking statuses:

- Confirmed
- Cancelled
- Completed
- NoShow

Payment modes:

- SingleMember
- Split

Split-payment statuses:

- NotApplicable
- AwaitingAcceptance
- Accepted
- Declined
- Expired

A separate `BookingPaymentShare` entity is preferred where it makes credit ownership/refunds clearer.

---

## 3. Lighting Device Fields

Suggested:

```text
Id
Name
ProviderType
Host
Port
NonSecretConfiguration
Enabled
LastSeenAtUtc
```

Secrets must be stored in application/environment secret storage, not plaintext configuration fields.

Court fields may include:

```text
LightingEnabled
LightingDeviceId
```

---

## 4. Court Lighting Session Fields

```text
Id
BookingId
CourtId
LightingDeviceId
ActivatedByMemberId
ActivatedAtUtc
ScheduledOffAtUtc
TurnedOffAtUtc
Status
LastError
```

Create a uniqueness rule that prevents multiple concurrently active light sessions for the same booking.

---

## 5. Credit Debit Webhook/API

Provide a secure inbound endpoint such as:

```http
POST /api/webhooks/credits/debit
```

Example request:

```json
{
  "memberId": "12345",
  "credits": 1,
  "externalTransactionId": "court-access-938472",
  "description": "Court usage",
  "timestamp": "2026-08-26T18:30:00Z"
}
```

Requirements:

- Authenticate caller.
- Validate payload.
- Reject zero/negative debit values.
- Confirm member exists.
- Check sufficient balance.
- Deduct credits atomically.
- Record ledger entry.
- Return resulting balance.
- Be idempotent.

The same `externalTransactionId` must never deduct twice.

Example success:

```json
{
  "success": true,
  "transactionId": "cr_837462",
  "creditsDeducted": 1,
  "remainingCredits": 7
}
```

Example insufficient credits:

```json
{
  "success": false,
  "error": "INSUFFICIENT_CREDITS",
  "remainingCredits": 0
}
```

Example duplicate:

```json
{
  "success": true,
  "duplicate": true,
  "transactionId": "cr_837462",
  "remainingCredits": 7
}
```

Use either:

- HMAC signing, or
- API key plus HMAC

Do not store secrets in source code.

---

## 6. Credit Lookup API

Provide an authorised read endpoint such as:

```http
GET /api/members/{memberId}/credits
```

Example response:

```json
{
  "memberId": "12345",
  "credits": 7,
  "membershipActive": true
}
```

Use the same external API authentication model.

---

## 7. Payment Webhooks

Payment webhooks must:

- Verify provider signature.
- Be idempotent.
- Persist provider event/reference IDs.
- Be safe against duplicate delivery.
- Never trust browser redirect alone.

---

## 8. Booking Database Constraints

At minimum, prevent duplicate active bookings for:

- Same Court
- Same Start Time
- Same Slot/overlapping period as appropriate to slot model

If the application uses fixed generated slots, a unique constraint on court + slot start is appropriate.

If arbitrary time ranges are supported, enforce non-overlap transactionally and with the strongest available database constraint.

---

## 9. Credit Concurrency

Credit operations must not allow overdraw under concurrent requests.

Use:

- Database transactions.
- Appropriate row locking / concurrency strategy.
- Idempotency keys for external operations.

Do not perform:

1. Read balance.
2. Return to application.
3. Later update without concurrency protection.

---

## 10. Audit Log

Record important actions including:

- Manual membership activation.
- Credit adjustment.
- Booking creation/cancellation.
- Peak-rule override.
- Ladder-position change.
- Payment-state change.
- Light ON/OFF.
- Failed light command.
- External credit debit.

Suggested fields:

```text
Actor
Action
EntityType
EntityId
TimestampUtc
OldValue
NewValue
Reason
Source
```

---

## 11. Lighting Audit

For every lighting operation record:

- Booking ID
- Court ID
- Action
- Actor/member/admin
- Requested time
- Device result
- Scheduled-off time
- Error information where applicable

Never log credentials or secrets.

---

## 12. Security Requirements

Implement:

- HTTPS-only production configuration.
- ASP.NET Core secure password hashing.
- CSRF protection.
- Server-side role/ownership authorization.
- Input validation.
- Rate limiting on login and external APIs.
- Payment webhook signature verification.
- Credit webhook authentication.
- Idempotency.
- Secure secret management.
- Parameterised SQL / ORM.
- Protection against mass assignment.

A member must never be able to manipulate a request to:

- Book for another member without authorised workflow.
- Change their credit balance.
- Change booking cost.
- Change membership status.
- Change ladder ranking directly.
- Activate another court's lights.
- Change lighting-device identifiers.
- Extend a light session beyond permitted limits.

---

## 13. Notification Abstraction

Create a notification abstraction, for example:

```csharp
public interface INotificationService
{
    Task SendAsync(...);
}
```

Initial implementation:

- Email

Notification types:

- Membership purchase confirmation
- Credit purchase confirmation
- Court booking confirmation
- Split-payment approval request
- Court cancellation confirmation
- Same-day cancellation alert
- Ladder challenge
- Ladder result confirmation
- Membership expiry reminder

---

## 14. Configuration

The following must be configurable rather than hard-coded:

- Club timezone
- Slot duration
- Opening hours
- Peak periods
- Maximum peak bookings/member/day
- Court credit costs
- Credit package definitions
- Cancellation refund cutoff
- Cancellation alert hours
- Split-payment approval timeout
- Split-payment expiry behaviour
- Light activation early-start allowance
- Lighting duration
- Lighting grace period
- Ladder challenge distance
- Membership products
