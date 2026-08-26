# Squash Club Management Website

ASP.NET Core 8, Identity, EF Core and PostgreSQL implementation of the squash-club specification. It includes responsive member booking UI, account APIs, memberships and products, configurable courts/opening/peak/closure rules, integer-unit credits, transactional bookings, split approval, cancellation alerts, payments, ladders, server-managed lighting, external APIs, administration APIs, audit records and automated service tests.

## Prerequisites and database

- .NET 8 SDK
- PostgreSQL 15+

```bash
createdb squashclub
export ConnectionStrings__Club='Host=localhost;Database=squashclub;Username=postgres;Password=...'
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SquashClub.Web
psql squashclub < db/migrations/001_initial.sql
```

Development uses EF Core to create the complete schema and seeds three courts, opening hours, peak periods, three membership products, three credit packages, system settings, a club ladder and a mock lighting device. Set `Seed__MemberPassword` to create `alex@club.test` and `admin@club.test`; no password is committed. Production deployment should create a full EF migration against the chosen PostgreSQL version before first launch. The SQL migration in `db/migrations` independently reinforces the critical concurrency and idempotency indexes.

## Secrets and integrations

Use environment variables, user secrets, or a production secret store. Never commit them:

```bash
export ExternalApi__Key='client-key'
export ExternalApi__HmacSecret='long-random-secret'
export Payments__HmacSecret='stripe-webhook-secret'
```

The development payment gateway returns a non-payable `development://` checkout reference. Memberships and credits are activated only through the authenticated payment webhook, never through a browser redirect. `IEmailNotificationSender`, `IPaymentGateway`, and `ILightingProvider` are replaceable integration boundaries. Development email is logged without message bodies, and the mock relay has no network credentials.

## External API authentication

Every external request sends:

- `X-Api-Key`
- `X-Timestamp`: current Unix seconds
- `X-Signature`: uppercase hex HMAC-SHA256 of `<timestamp>.<payload>`

The debit payload is its `externalTransactionId`; balance lookup uses the member GUID; payment confirmation uses the provider event ID. Requests outside a five-minute window are rejected. `POST /api/webhooks/credits/debit` accepts decimal credits with at most two decimal places and converts them exactly to integer units (`100 units = 1 credit`). Its external transaction ID is unique, so duplicate and concurrent delivery cannot debit twice. `GET /api/members/{memberId}/credits` uses the same authentication.

## Lighting

`CourtLightingService` verifies the booking, court, activation window and participant. A split opponent must first accept. It persists one pending session before calling the relay, never extends repeated requests, caps shutoff at booking end, and audits commands. A hosted worker repeatedly turns off overdue pending/on sessions, including sessions recovered after restart. Device details and errors never reach the browser.

## Run checks

```bash
dotnet restore
dotnet build SquashClub.sln --no-restore
dotnet test SquashClub.sln --no-build
```

The test suite covers membership eligibility, server pricing, fractional units, peak restrictions, split approval and payer refunds, external idempotency, availability/closures, same-day alerts, payment idempotency, lighting authorization/idempotency/timing/reconciliation and ledger integrity. PostgreSQL concurrency and physical-integration acceptance tests should additionally run in the deployment environment.

## Production decisions

See [`unanswered_questions.md`](unanswered_questions.md) for provider credentials, club-policy choices and infrastructure inputs still needed from the club. Those questions are intentionally separated from source code and contain the currently implemented defaults.
