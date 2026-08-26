# Codex Implementation and Test Requirements

## 1. Technical Direction

For a new project, use:

```text
Backend: ASP.NET Core
Language: C#
Database: PostgreSQL or SQL Server
ORM: Entity Framework Core
Authentication: ASP.NET Core Identity
Payments: Stripe
Frontend: Razor Pages or MVC
CSS: Bootstrap or similar lightweight responsive framework
```

Prefer a conventional server-rendered application over unnecessary SPA complexity for the first version.

If this specification is added to an existing repository, inspect the repository first and reuse established patterns where sensible.

---

## 2. Suggested Service Boundaries

Business rules should live in domain/application services, not controllers/pages.

Suggested services:

```text
BookingService
CourtAvailabilityService
MembershipService
PaymentService
CreditService
CancellationNotificationService
LadderService
NotificationService
CourtLightingService
ExternalApiService
AuditService
```

For lighting, use an interface such as:

```text
ICourtLightingService
```

with a provider-specific implementation behind it.

---

## 3. Implementation Priorities

Implement in this order where practical:

1. Authentication and roles.
2. Member/membership model.
3. Courts, opening hours, slots and peak periods.
4. Credit ledger and credit products.
5. Core court booking transaction.
6. Double-booking protection.
7. Peak booking limits.
8. Booking cancellation/refunds.
9. Membership and credit payments.
10. Split-credit bookings.
11. Same-day cancellation alerts.
12. External credit webhook/API.
13. Ladder competitions.
14. Court lighting abstraction and device configuration.
15. Server-side lighting sessions and automatic shutoff.
16. Admin dashboards.
17. Audit/reconciliation jobs.
18. Full automated tests.

---

## 4. Development Seed Data

Seed development data for:

- 3 courts
- Opening hours
- Peak periods
- Membership products
- Credit packages
- Test members
- One sample ladder
- Optional development/mock lighting device

A mock lighting provider should be available for local development/tests so no physical relay is required.

---

## 5. README Requirements

Project README should document:

- Required SDK/runtime.
- Database setup.
- Migrations.
- Environment variables.
- Stripe setup.
- Email setup.
- External credit API authentication.
- Lighting provider configuration.
- How to use mock lighting locally.
- How to run application.
- How to run tests.

---

# Automated Tests

## 6. Booking Tests

Test:

- Active member can book available court.
- Inactive member cannot book.
- Member without sufficient credits cannot book.
- Booking deducts correct credits.
- Client cannot alter server-calculated booking cost.
- Same slot cannot be double booked.
- Peak limit is enforced.
- Off-peak booking does not count against peak limit.
- Admin override works.
- Concurrent booking results in exactly one success.
- Failed debit rolls back booking.
- Failed booking rolls back debit.

---

## 7. Cancellation Tests

Test:

- Eligible cancellation refunds credits.
- Late cancellation does not refund.
- Cancelled slot becomes available.
- Qualifying same-day cancellation creates notification event.
- Split booking refunds each payer correctly.
- Cancellation does not refund more than originally paid.

---

## 8. Credit Tests

Test:

- Credit purchase increases balance.
- Booking decreases balance.
- Fractional displayed credits map exactly to integer credit units.
- Balance cannot become negative.
- Admin adjustment creates ledger entry.
- Concurrent debit requests cannot overdraw.
- Duplicate external transaction IDs do not double-debit.

---

## 9. Split Credit Tests

Test:

- Split option only appears when second member is selected.
- Pay-all option still works.
- Split amounts are calculated correctly.
- 0.5 credit is represented exactly.
- Member cannot charge another member without approval.
- Second member with insufficient credits cannot accept.
- Expired split invitation cannot be accepted.
- Declined invitation follows configured behaviour.
- Concurrent acceptance cannot double-deduct.
- Each payer receives independent ledger entries.
- Cancellation refunds each payer correctly.

---

## 10. External API Tests

Test:

- Valid authenticated debit succeeds.
- Invalid authentication fails.
- Invalid amount fails.
- Unknown member fails.
- Insufficient credits fails.
- Duplicate transaction ID returns idempotent result.
- Concurrent duplicate requests deduct only once.
- Credit lookup requires authentication.

---

## 11. Ladder Tests

Test:

- Eligible challenge succeeds.
- Invalid challenge distance fails.
- Challenger win updates rankings correctly.
- Challenger loss leaves ranking unchanged.
- Expired challenge cannot be completed.
- Ranking changes are audited.

---

## 12. Lighting Tests

Test:

- User cannot activate lights without qualifying booking.
- User cannot activate another court's lights.
- Primary booking member can activate lights.
- Accepted second player can activate lights.
- Admin can override.
- Button/action is rejected outside configured activation window.
- Successful ON creates one lighting session.
- Repeated ON does not create another session.
- Repeated ON does not extend session.
- Shutoff is capped at booking end.
- Manual OFF works.
- Manual OFF is idempotent.
- Automatic OFF occurs server-side.
- Browser closure has no effect on automatic OFF.
- Application restart recovery finds overdue sessions.
- Periodic reconciliation turns off overdue lights.
- Network failure does not alter booking or credits.
- Failed light actions are audited.
- Credentials/internal relay details are never returned to member client.

---

# Acceptance Scenarios

## 13. Peak Booking Limit

Given:

- Monday peak period: 17:00-21:00
- Maximum peak bookings/day: 1

Member books:

- Monday 18:00 Court 1

Then attempts:

- Monday 19:30 Court 3

Expected:

- Rejected because peak daily limit is reached.

Then attempts:

- Monday 14:00 Court 2

Expected:

- Allowed if otherwise available.

---

## 14. Concurrent Booking

Member A and Member B simultaneously request:

- Court 2
- Wednesday
- 19:15

Expected:

- Exactly one booking exists.
- Exactly one payer/debit set is committed.
- Losing request gets a clear availability error.

---

## 15. Cancellation Alert

All courts are booked from 18:00-20:00.

Member subscribes for:

- Today
- 18:00-20:00
- Any court

Court 2 at 19:15 is cancelled.

Expected:

- Member receives notification.
- Notification links to booking page.
- Slot is not reserved.
- First successful booking gets it.

---

## 16. External Credit Debit

Member has:

- 5 credits

External system calls:

```text
POST /api/webhooks/credits/debit
credits = 1
externalTransactionId = ABC123
```

Expected:

- Balance becomes 4.

Same external transaction is submitted again.

Expected:

- Balance remains 4.
- No second debit occurs.

---

## 17. Split Credits

Court cost:

- 2 credits

Member A books with Member B and selects split.

Expected before acceptance:

- Member A responsible for 1 credit.
- Member B receives request for 1 credit.

Member B accepts.

Expected:

- Member A has 1 credit deducted.
- Member B has 1 credit deducted.
- Booking split-payment state becomes accepted.
- Two ledger transactions are linked to same booking.

---

## 18. Match Lighting

Booking:

- Court 2
- 19:15-20:00
- Member A vs Member B

Configured light activation lead time:

- 5 minutes

At 19:10:

- "Turn on Lights" becomes eligible.

At 19:13 Member B selects it.

Expected:

- Court 2 relay receives ON.
- One lighting session is created.
- Scheduled-off time is 20:00.
- Both authorised members see lights as ON.

At 20:00:

- Court 2 relay receives OFF.
- Session is marked off/completed.
- No browser needs to remain open.

---

## 19. Codex Completion Criteria

Before treating implementation as complete:

1. Run database migrations successfully.
2. Run complete automated test suite.
3. Verify all tests pass.
4. Verify critical acceptance scenarios manually or through integration tests.
5. Verify member/mobile booking flow.
6. Verify duplicate booking protection under concurrent requests.
7. Verify webhook idempotency.
8. Verify split-credit approval and refund flows.
9. Verify lighting with mock provider.
10. Verify server-side automatic light shutoff and restart recovery.
11. Verify secrets are not committed to source control.
12. Update README for any new setup steps.

Do not add unrelated club-management functionality during the initial implementation.
