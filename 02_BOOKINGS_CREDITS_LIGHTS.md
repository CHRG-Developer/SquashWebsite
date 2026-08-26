# Booking, Credit and Court Lighting Rules

## 1. Credit Model

Court bookings use credits.

Credits must support fractional amounts without floating-point arithmetic.

Recommended representation:

- 1 credit = 100 credit units
- 0.5 credit = 50 credit units
- 2 credits = 200 credit units

Store credit units as integers.

Display them to members as decimal credits.

---

## 2. Credit Packages

Administrators can create credit products.

Example:

| Product | Credits | Price |
|---|---:|---:|
| 5 Court Credits | 5 | €20 |
| 10 Court Credits | 10 | €35 |
| 20 Court Credits | 20 | €60 |

Purchase flow:

1. Member selects package.
2. Member pays online.
3. Payment is confirmed server-side.
4. Credits are added.
5. Ledger transaction is created.
6. Updated balance is displayed.

---

## 3. Credit Ledger

Every credit movement must generate a ledger entry.

Examples:

- `+10` Credit Purchase
- `-1` Court Booking
- `+1` Booking Cancellation Refund
- `-1` External Webhook Usage
- `+5` Administrator Adjustment

Each entry should contain:

- Transaction ID
- Member ID
- Credit units
- Transaction type
- Description
- Related booking ID if applicable
- Related payment ID if applicable
- External reference if applicable
- Timestamp
- Resulting balance
- Created by / source

A member must not spend credits resulting in a negative balance.

---

## 4. Court Credit Cost

Court credit cost must be configurable.

Examples:

- Off-peak: 1 credit
- Peak: 2 credits

Do not assume all slots cost exactly one credit.

Credit cost is determined server-side.

The client must not be trusted to submit the charge amount.

---

## 5. Single-Payer Booking

When one member pays the full amount:

1. Validate membership.
2. Validate slot availability.
3. Validate booking restrictions.
4. Determine server-side court cost.
5. Validate sufficient balance.
6. Create booking.
7. Deduct credits.
8. Add ledger entry.

Booking creation and debit must occur in the same database transaction.

---

## 6. Opponent / Second Player

A booking may optionally identify another member as the opponent/second player.

The second player should see the match in their upcoming bookings.

The second member must not automatically become financially liable simply because they were selected.

---

## 7. Split Credits

When a second player is selected, offer:

- Pay all credits
- Split credits

Example:

```text
Court Cost: 2 credits

Payment:
(*) Pay all – 2 credits
( ) Split – 1 credit each
```

For a 1-credit booking:

- Player A: 0.5
- Player B: 0.5

If a cost cannot be divided exactly into the supported smallest credit unit, define a deterministic split rule in configuration/business logic. Do not use floating-point rounding.

---

## 8. Split Credit Approval

A member cannot directly charge another member.

For split payment:

1. Member A creates the booking and names Member B.
2. Member A's share may be reserved/debited according to implementation.
3. Member B receives an approval request.
4. Member B accepts or declines.
5. On acceptance, validate:
   - Active membership.
   - Sufficient balance.
   - Booking is still valid.
   - Invitation is not expired.
6. Deduct Member B's share atomically.
7. Mark split payment accepted.

Suggested split invitation timeout:

- 30 minutes

Make the timeout configurable.

If declined/expired, configurable behaviour should allow either:

- Original booker to pay the outstanding share, or
- Booking to be cancelled.

All debits/refunds must remain individually traceable in the ledger.

---

## 9. Cancellation Refunds for Split Bookings

If cancellation qualifies for a refund:

- Refund each member only the amount they actually paid.
- Create separate ledger entries.
- Never refund the entire cost to only the original booker.

If cancellation does not qualify:

- No payer receives a refund.

---

## 10. Same-Day Cancellation Subscription

A member may subscribe to an available-court alert for later the same day.

Example criteria:

- Date: today
- Earliest time: 18:00
- Latest time: 21:00
- Courts: any or selected courts

When a booking is cancelled:

1. Confirm the released slot is today.
2. Find matching subscriptions.
3. Send notification.

The alert does not reserve the slot.

---

# Court Lighting

## 11. Court Lighting Requirement

Each court may be linked to a network-controlled 220V lighting relay/switch.

The application must never directly expose mains control details to the member browser.

Create an abstraction such as:

```csharp
public interface ICourtLightingService
{
    Task TurnOnAsync(int courtId, CancellationToken cancellationToken);
    Task TurnOffAsync(int courtId, CancellationToken cancellationToken);
    Task<CourtLightStatus> GetStatusAsync(int courtId, CancellationToken cancellationToken);
}
```

The physical implementation may use:

- HTTP
- MQTT
- Modbus/TCP
- Vendor API
- Another Ethernet relay protocol

Do not hard-code a specific relay vendor into booking logic.

---

## 12. Turn On Lights Button

For an eligible current booking, show:

```text
Court 2
19:15 – 20:00

[ Turn on Lights ]
```

The button applies only to:

- That booking.
- That booking's assigned court.

Authorised users:

- Original booking member.
- Accepted second player/opponent.
- Administrator.

A member must not activate lights for:

- Another court.
- Another unrelated booking.
- Cancelled booking.
- Expired booking.
- Future booking outside the configured activation window.

---

## 13. Light Activation Window

Make activation timing configurable.

Suggested default:

- Button becomes active 5 minutes before booking start.
- Button remains eligible until booking end.

Example:

- Booking: 19:15-20:00
- Earliest activation: 19:10

---

## 14. Light Session Duration

Default lighting duration:

- 45 minutes

However, automatic shutoff must not normally exceed booking end.

Use:

```text
ScheduledOffAt =
MIN(
    ActivatedAt + ConfiguredLightingDuration,
    BookingEnd
)
```

Example:

- Booking: 19:15-20:00
- Activated: 19:30
- Configured duration: 45 minutes
- Scheduled off: 20:00, not 20:15

Any grace period after booking end must be explicit configuration.

---

## 15. Server-Side Lighting Timer

Do not rely on a browser JavaScript timer.

The server must persist the lighting session.

Suggested fields:

- ID
- Booking ID
- Court ID
- Lighting device ID
- Activated by member ID
- Activated at UTC
- Scheduled off at UTC
- Turned off at UTC
- Status
- Last error if applicable

Possible statuses:

- Pending
- On
- Off
- Failed

The browser may show a visual countdown, but the server is authoritative.

Closing the browser must not leave lights on.

---

## 16. Repeated Activation

Lighting activation must be idempotent for a booking.

Repeated pressing of "Turn on Lights":

- Must not create extra timers.
- Must not extend the shutoff time.
- Should return/display the existing active session.

---

## 17. Turn Off Lights

While a booking light session is active, authorised users should see:

```text
[ Turn off Lights ]
```

Manual OFF should:

- Send OFF command.
- Mark session completed/off.
- Be idempotent.
- Create audit entry.

---

## 18. Restart and Failure Recovery

Lighting sessions must survive application restart.

On startup and periodically:

1. Find active sessions past their scheduled-off time.
2. Send OFF command.
3. Record result.

Run periodic reconciliation so an interrupted timer does not leave lights enabled indefinitely.

An OFF command should be safe to repeat.

---

## 19. Lighting Failure Behaviour

If the relay cannot be reached:

- Show a member-friendly error.
- Do not expose IP addresses, credentials or internal details.
- Log technical diagnostics.
- Do not cancel the court booking.
- Do not deduct extra credits.
- Do not change membership status.

Suggested user message:

```text
Unable to turn on the court lights.
Please contact the club if the problem continues.
```

---

## 20. Admin Lighting Control

Admin screen should show:

| Court | Light Status | Booking | Auto Off |
|---|---|---|---|
| Court 1 | Off | — | — |
| Court 2 | On | Smith v Jones | 20:00 |
| Court 3 | Off | — | — |

Admin can:

- Turn lights on.
- Turn lights off.
- View current session.
- View device status where supported.
- Override failed sessions.

Administrative light actions must be audited.

---

## 21. Lighting Network Security

Prefer that relays/controllers are accessible only over:

- Club LAN
- Private network
- VPN
- Other appropriately isolated path

Do not expose an unauthenticated Ethernet relay directly to the public internet.

Member browsers must never receive:

- Device IP credentials
- Relay passwords
- API keys
- Direct control URLs
