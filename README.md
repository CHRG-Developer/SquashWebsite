# Squash Club Website – Codex Specification

This folder contains the implementation specification for a squash club management website.

## Files

- `01_FUNCTIONAL_SPEC.md` – product scope, roles, memberships, bookings, cancellations, ladders and administration.
- `02_BOOKINGS_CREDITS_LIGHTS.md` – detailed booking rules, split credits, cancellation alerts and Ethernet-controlled court lighting.
- `03_DATA_API_SECURITY.md` – suggested domain model, APIs, webhook behaviour, concurrency, security and auditing.
- `04_IMPLEMENTATION_TESTS.md` – implementation guidance for Codex and required automated/acceptance tests.

## Primary Technical Direction

For a greenfield implementation use:

- ASP.NET Core
- C#
- Entity Framework Core
- PostgreSQL or SQL Server
- ASP.NET Core Identity
- Stripe for payments
- Razor Pages or MVC
- Bootstrap or a similarly lightweight responsive framework

If an existing repository already has an established architecture, Codex should inspect and reuse it where sensible rather than replacing it.

## Key Non-Negotiable Rules

1. Court, opening hour, peak period, price and booking limits must be configurable.
2. A member may hold only one peak-time booking per calendar day by default.
3. Court bookings and credit deductions must be atomic.
4. Double-booking must be prevented at the database level.
5. Split-credit bookings require the second player's approval before their credits are deducted.
6. Same-day cancellation alerts do not reserve the released court.
7. External credit deductions must be authenticated and idempotent.
8. Court light control must be server-side and tied to a specific valid booking.
9. Light shutoff must survive browser closure and application restart.
10. Important booking, credit, ladder and lighting actions must be auditable.
