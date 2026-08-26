# Unanswered product and deployment questions

The specifications define the required behaviour but intentionally leave the following deployment or policy choices open. The application currently uses the conservative defaults described below. These answers are needed before a production launch; none require weakening booking, credit, idempotency, or lighting safety rules.

## Payments

1. Which Stripe account, currency configuration, success/cancel URLs, webhook signing secret, and price identifiers should production use? The current gateway is explicitly development-only; payment state is nevertheless activated only by the authenticated server webhook.
2. Should a membership purchased before an existing membership expires extend from the current expiry (the current behaviour), or overlap/start immediately?
3. Are payment refunds performed in Stripe by an administrator, and which membership/credit revocation policy applies after a chargeback?

## Email and notifications

4. Which transactional email provider, sender domain/address, and public base URL should be used for confirmation, reset, split approval, cancellation alerts, and ladder messages? Notifications are durably queued in the database, but a production delivery adapter requires these details.
5. What retention period and retry schedule should apply to sent and failed notifications?

## Club policy

6. Confirm `Europe/Dublin`, 45-minute slots, one peak booking per local calendar day, a two-hour refund cutoff, a 30-minute split invitation, and cancellation of the booking when a split invitation is declined or expires.
7. If an odd number of smallest credit units must be split, should the original booker pay the extra unit (current deterministic behaviour) or should the opponent?
8. May an opponent who has not accepted a split invitation use the lights? The current implementation treats a named opponent as a participant for pay-all bookings and requires an accepted split before the normal UI should expose controls; confirm whether server authorization must also require split acceptance.
9. What hours may members create same-day cancellation watches, and should watches be limited in number per member?
10. Should administrators be able to create bookings for non-members, guests, or coaches, and which audit reason fields are mandatory for each override?

## Ladder

11. How long should challenges remain open, who may submit the first result, and must the *other* player (rather than either player) confirm it? Current defaults are 14 days and confirmation by either participant.
12. What happens to rankings when a member leaves, membership expires, or a challenge expires?

## Lighting and infrastructure

13. Which relay protocol/vendor, device inventory, network route, secret-store integration, health-check interval, and explicit post-booking grace period should production use?
14. Should administrators be allowed to activate lights without an associated booking? The current member API always requires a booking; a separate audited emergency/admin device operation has not been exposed without confirmation of this policy.

## Known integration gaps requiring deployment inputs

- A production Stripe checkout adapter and official Stripe signature verifier replace `DevelopmentPaymentGateway` once Stripe details are supplied.
- A production email sender/queue worker requires the provider and sender-domain answers above.
- A production network lighting provider requires relay/network details; the mock remains suitable for development and automated tests.
- The checked-in SQL invariant migration assumes the EF-created schema. A generated provider-specific full baseline migration should be produced and reviewed in the target PostgreSQL environment before deployment.
- End-to-end browser, PostgreSQL concurrency, and physical-relay acceptance tests require the corresponding running infrastructure.
