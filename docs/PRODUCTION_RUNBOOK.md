# FlatLedger API — Production runbook

## Database schema changes

This project does **not** use EF Core migrations. Schema is defined in:

`SocietyLedger.Infrastructure/db_schema.sql`

### Before each release

1. Diff your target database against `db_schema.sql`.
2. Apply only the **new** DDL in a maintenance window (or use a migration tool you adopt later).
3. Run smoke tests: health check `GET /health`, login, record a test payment in staging.

### Recommended practices

- Keep a `schema_versions` table or tagged SQL files (`V001__initial.sql`, `V002__add_column.sql`) as you evolve.
- Never edit production schema by hand without a recorded script in git.

## Backups (PostgreSQL)

Configure on your host (e.g. Render managed Postgres, AWS RDS):

| Item | Recommendation |
|------|----------------|
| Automated backups | Daily minimum; point-in-time recovery if available |
| Retention | 14–30 days for MVP |
| Restore drill | Quarterly restore to staging and verify login + one society’s bills |

Document your provider’s restore steps in your internal ops wiki.

## Environment variables (production)

| Variable | Purpose |
|----------|---------|
| `ConnectionStrings__DefaultConnection` | PostgreSQL |
| `JwtSettings__Key` | ≥ 32 bytes, HMAC secret |
| `JwtSettings__Issuer` / `Audience` | Token validation |
| `Razorpay__KeyId` / `KeySecret` / `WebhookSecret` | Subscriptions |
| `Frontend__BaseUrl` | Password reset links (e.g. `https://app.flatledger.com`) |
| `AllowedOrigins__N` | Each frontend origin (Vercel URLs) |

## Password reset (development)

In Development, the API logs the reset link (see API logs). In production, wire `EmailService` to SendGrid/SES and **do not** log tokens.

## Monitoring

- API: Serilog → stdout; forward Render logs to your aggregator.
- UI: `VITE_SENTRY_DSN` on Vercel.
- Alert on: health check failures, billing job errors, 5xx rate spikes.
