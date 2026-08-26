# Functional Specification

## 1. Purpose

Build a responsive web application for a squash club supporting:

- User registration and authentication.
- Membership sign-up and online payment.
- Court configuration and booking.
- Peak-time booking restrictions.
- Same-day cancellation alerts.
- Court-credit purchases and balances.
- Split-credit court bookings.
- External credit deductions through a secure webhook/API.
- Squash ladder competitions.
- Court lighting control for active bookings.
- Club administration.

The site must work well on desktop and mobile browsers.

---

## 2. Roles

### 2.1 Visitor

A visitor can:

- View public club information.
- View membership products and prices.
- Register.
- Purchase membership.
- Sign in.

Member identities and booking details must never be exposed publicly.

### 2.2 Member

An authenticated member can:

- View membership status.
- Purchase or renew membership.
- View credit balance.
- Buy credits.
- View court availability.
- Book and cancel courts.
- Select an opponent/second player for a booking.
- Pay all court credits or split credits with the second player.
- Subscribe to qualifying same-day court cancellation alerts.
- Join and participate in ladders.
- View ladder standings and match history.
- View transaction and booking history.
- Turn on/off court lights when authorised for a current booking.
- Update basic account details.

Only members with an active membership may normally book courts or participate in ladders.

### 2.3 Administrator

Administrators can:

- Manage members and membership status.
- Configure membership products and prices.
- Create, edit and disable courts.
- Configure opening hours.
- Configure slot duration.
- Configure peak/off-peak periods.
- Configure maximum peak bookings per day.
- Block courts for maintenance/events.
- Configure court credit costs.
- Configure credit packages.
- Adjust member credits.
- Create/cancel bookings on behalf of members.
- Override booking restrictions where authorised.
- Configure cancellation/refund rules.
- Configure cancellation alert hours.
- Create and manage ladders.
- Adjust ladder positions.
- View payment and credit history.
- Manage court lighting devices and current light status.
- View audit logs.

---

## 3. Authentication and Member Accounts

Provide:

- Email/password registration.
- Email verification.
- Login/logout.
- Forgotten-password/reset-password flow.
- Role-based permissions.
- Unique email addresses.

Member fields should include at minimum:

- Member ID
- First name
- Last name
- Email
- Phone number
- Account status
- Membership status
- Membership start date
- Membership expiry date
- Credit balance or derived credit balance
- Created date
- Last login date

Do not store payment card details.

---

## 4. Memberships

### 4.1 Membership Products

Administrators can create membership products such as:

- Adult Annual
- Student Annual
- Junior
- Family
- Trial / Temporary

Each product should contain:

- Name
- Description
- Price
- Duration
- Active flag
- Booking entitlement
- Ladder entitlement

Membership products must not be hard-coded.

### 4.2 Membership Purchase

Flow:

1. User registers or signs in.
2. User selects membership.
3. User proceeds to payment.
4. Payment provider processes payment.
5. Payment is confirmed server-side.
6. Membership becomes active.
7. Start/end dates are recorded.
8. Payment transaction is recorded.
9. Confirmation is displayed and emailed.

Default provider for greenfield implementation: Stripe.

Payment webhooks must be:

- Signature verified.
- Idempotent.
- Logged.
- Safe against duplicate events.

Browser redirect alone must never activate membership.

---

## 5. Courts and Availability

### 5.1 Courts

Administrators can create any number of courts.

Each court should contain:

- Court ID
- Name/number
- Description
- Display order
- Active flag
- Optional lighting configuration

Courts must not be hard-coded.

### 5.2 Opening Hours

Configure opening hours independently for each day of the week.

Example:

| Day | Open | Close |
|---|---:|---:|
| Monday | 07:00 | 23:00 |
| Tuesday | 07:00 | 23:00 |
| Sunday | 09:00 | 21:00 |

### 5.3 Slot Duration

Slot duration must be configurable.

Default:

- 45 minutes

Do not hard-code 45 minutes throughout the application.

### 5.4 Peak Periods

Administrators can configure one or more peak periods per day.

Example:

- Monday-Friday: 17:00-21:00
- Saturday: 10:00-14:00

Peak periods must be stored as data/configuration.

### 5.5 Court Closures

Administrators can block:

- A single slot.
- A time range.
- One court.
- All courts.

Use cases include maintenance, coaching, events and tournaments.

Blocked slots cannot be booked.

---

## 6. Court Booking UI

The primary booking screen should show a date and a court/time grid.

Example:

| Time | Court 1 | Court 2 | Court 3 |
|---|---|---|---|
| 17:00 | Available | Booked | Available |
| 17:45 | Available | Available | Booked |
| 18:30 | Booked | Available | Available |

Members can:

- Select date.
- View availability.
- Select an available slot.
- Optionally select another club member as opponent/second player.
- Choose payment mode:
  - Pay all credits.
  - Split credits.
- Confirm booking.

The UI must be comfortable to use on mobile.

---

## 7. Peak-Time Booking Restriction

Default rule:

> A member may hold a maximum of one peak-time court slot per calendar day.

Example:

If Monday peak time is 17:00-21:00 and a member has booked 18:30, another peak slot that Monday must be rejected.

An off-peak booking that day remains allowed unless another configured rule prevents it.

This rule must be enforced server-side.

Configuration:

- `MaximumPeakBookingsPerMemberPerDay`
- Default: `1`

Admin-created bookings may permit an explicit override.

---

## 8. Booking Concurrency

Double booking must be prevented.

If two members attempt to book the same court/time simultaneously:

- Exactly one booking succeeds.
- Exactly one credit transaction set is committed.
- The other member receives a clear "slot is no longer available" message.

Use database-level protection/transactions, not only application checks.

---

## 9. Booking Cancellation

Cancellation policy must be configurable.

Example:

- Full refund until 2 hours before start.
- No refund within 2 hours.

When eligible:

- Cancel booking.
- Refund the correct credits to the correct payer(s).
- Create refund ledger entries.

When not eligible:

- Cancel booking.
- Do not refund credits.
- Record cancellation as non-refundable.

A cancelled slot becomes available again and can trigger same-day cancellation alerts.

---

## 10. Same-Day Cancellation Alerts

Members can subscribe to notifications for released courts later that same day.

Subscription contains:

- Member
- Date
- Earliest acceptable time
- Latest acceptable time
- Optional court restriction
- Status
- Created time
- Expiry time

All same-day watches expire at end of day.

Administrators can configure when cancellation-alert subscriptions are available.

When a matching court is cancelled:

1. Slot becomes available.
2. Matching active subscriptions are found.
3. Matching members are notified.

Initial channel:

- Email

Architecture should allow SMS or push notification later.

Important:

> A cancellation notification does not reserve the court.

Released slots remain first-come-first-served.

---

## 11. Ladder Competitions

Administrators can create ladders with:

- Ladder ID
- Name
- Description
- Start date
- End date
- Status
- Challenge rules
- Participants
- Rankings
- Match history

Members can:

- View active ladders.
- Join/request to join according to configuration.
- Challenge eligible players.
- Record match results.
- Confirm opponent-submitted results.
- View rankings and history.

### 11.1 Default Challenge Rule

A player may challenge another player up to a configurable number of places above them.

Default:

- Maximum challenge distance: 3 positions

### 11.2 Challenge Statuses

- Pending
- Accepted
- Completed
- Cancelled
- Expired

### 11.3 Default Ranking Movement

If a lower-ranked challenger wins:

- Challenger takes the defeated player's position.
- Defeated player and intermediate players shift down as required.

If the higher-ranked player wins:

- Rankings remain unchanged.

Ranking logic must live in a dedicated service, not UI code.

Every ranking change must be auditable.

---

## 12. Member Dashboard

Display:

### Membership

- Status
- Expiry date
- Renewal action

### Credits

- Current balance
- Buy credits action

### Upcoming Bookings

- Court/date/time
- Opponent
- Payment mode
- Cancellation action
- Lighting controls when eligible

### Cancellation Alerts

- Active same-day alerts

### Ladders

- Current ranking
- Outstanding challenges
- Recent results

---

## 13. Admin Dashboard

Provide administration areas for:

### Members

- Search
- Membership status
- Credit balance
- Booking history
- Payment history
- Credit ledger
- Manual credit adjustment

### Courts

- Courts
- Opening hours
- Slot length
- Peak periods
- Closures
- Lighting-device assignment

### Bookings

- Today's bookings
- Future bookings
- Search/history
- Create booking
- Cancel booking
- Override restrictions

### Payments

- Membership payments
- Credit purchases
- Failed payments
- Refund/reference details

### Ladders

- Create/edit ladders
- Participants
- Rankings
- Challenges
- Match results

### Lighting

- Current light state per court
- Booking associated with active lights
- Scheduled automatic-off time
- Manual admin ON/OFF
- Device connectivity/health where available

---

## 14. Time Handling

Store timestamps internally in UTC.

Club rules operate in the configured club timezone.

Default:

- `Europe/Dublin`

The application must correctly handle Irish daylight-saving time.

Do not assume Irish local time is always equal to UTC.
