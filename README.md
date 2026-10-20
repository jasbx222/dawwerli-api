# Dawwerli API

ASP.NET Core 8 + PostgreSQL backend for «دوّرلي». Clean architecture:

```
src/Dawwerli.Domain          entities, enums, pure rules (ContactFilter, OfferPolicy, TrustPolicy, BusinessRules)
src/Dawwerli.Application     services per module, DTOs, validators, interfaces (storage, SMS, push, clock…)
src/Dawwerli.Infrastructure  EF Core (snake_case, migrations, seeder), JWT, phone encryption, storage, SMS, jobs worker
src/Dawwerli.Api             controllers (/api/v1, /api/admin/v1), SignalR /hubs/live, ProblemDetails, idempotency, rate limits, Swagger
tests/Dawwerli.Tests         domain unit tests + API integration tests on real PostgreSQL (5-offer race, early slots, idempotency, OTP lock)
```

All limits, timers and prices live in `Business` (appsettings) → copied once into the `settings` table by the seeder → editable from
`PUT /api/admin/v1/settings`. Arabic copy lives in `Texts`. Reference data (18 governorates + adjacency, categories with field schemas
and images, banned words) is in `src/Dawwerli.Infrastructure/Persistence/Seed`.

## Run on Windows (with the Android emulator)

Option A — Docker Desktop:
1. `copy .env.example .env` and put your own random values in it.
2. `docker compose up --build` → API on http://localhost:5080, Swagger at http://localhost:5080/swagger.
3. The emulator reaches your PC at `http://10.0.2.2:5080` (already the app's default).

Option B — .NET 8 SDK + PostgreSQL installed locally:
1. Create a database `dawwerli`.
2. `copy src\Dawwerli.Api\appsettings.Development.example.json src\Dawwerli.Api\appsettings.Development.json`, fill the CHANGE_ME values.
3. `cd src\Dawwerli.Api` → `dotnet run --urls http://0.0.0.0:5080` (migrations + seed run on startup).
4. Allow port 5080 in Windows Firewall if a real phone on Wi‑Fi should connect (use the PC's LAN IP as the app base URL).

Admin: `POST /api/v1/auth/admin/login` with `AdminSeed` credentials, then verify pages / approve top-ups and ads under `/api/admin/v1`.

## Tests

```
dotnet test            # needs PostgreSQL; override with DAWWERLI_TEST_DB="Host=...;Database=dawwerli_test;Username=...;Password=..."
scripts/smoke.sh       # curl end-to-end flow against a running API
```

## Needs real third-party keys

- SMS / WhatsApp OTP: set `Sms:Provider=Http`, `Sms:Url`, `Sms:ApiKey`, `Sms:SenderId` (generic JSON gateway; adapt `HttpSmsSender` to the chosen provider). Until then `Otp:ExposeCodeInResponse=true` (development only).
- Push (FCM): replace `LogPushSender` with an FCM sender (service-account JSON). Notifications are already stored and shown in-app.
- Payments: top-ups are manual (ZainCash / FIB / Qi transfer + reference + receipt, approved by admin). A merchant API can approve automatically later.
- Object storage: `LocalStorage` (signed URLs) → swap for S3/MinIO behind `IStorage` in production.
