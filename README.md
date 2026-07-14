# Direct-Booking Vacation Rental Platform

> Phase 1 — an 8-week, modular-monolith build of a direct-booking platform for vacation rentals, with two-way AirBnB iCal sync, Stripe Connect Express payments, and an admin dashboard.
>
> **Working plan & agent entry point:** [`CLAUDE.md`](./CLAUDE.md) → [`docs/AGENT-PLAYBOOK.md`](./docs/AGENT-PLAYBOOK.md) → [`docs/stories/BOARD.md`](./docs/stories/BOARD.md). The original [`BookingApp_Proposal.md`](./BookingApp_Proposal.md) is **historical** — superseded as the working spec by the [`docs/`](./docs/) spec set (retained for ADR provenance).

---

## Repository layout

```
.
├── BookingApp_Proposal.md       # Historical proposal (superseded — see docs/ + CLAUDE.md)
├── src/                         # .NET 8 backend (Clean Architecture, modular monolith)
│   ├── VrBook.Domain/           # Aggregates, entities, value objects, domain events (pure)
│   ├── VrBook.Application/      # Use cases, MediatR handlers, pipeline behaviors
│   ├── VrBook.Infrastructure/   # EF Core, Redis, Service Bus, Blob, external adapters
│   ├── VrBook.Api/              # ASP.NET Core host, controllers, OpenAPI, auth
│   ├── VrBook.Migrator/         # Console app — runs EF migrations as a Container App init step
│   ├── VrBook.Contracts/        # Shared DTOs, domain event records, enums, interfaces
│   ├── VrBook.Modules.*/        # Per-bounded-context modules (Identity, Catalog, Pricing, …)
│   └── VrBook.sln
├── web/                         # Next.js 14 (App Router) — public site + /admin
├── infra/                       # Bicep IaC (modules + per-env parameter files)
├── contracts/                   # OpenAPI spec + contract change log
├── docs/                        # ADRs, runbooks, security threat model, B2C policy notes
├── docker-compose.yml           # Local Postgres + Redis + Azurite + Service Bus emulator
├── .github/workflows/           # ci.yml, cd-staging.yml, cd-prod.yml
└── README.md
```

## Quick start (local dev)

Prerequisites:
- .NET 8 SDK
- Node.js 20+ (current scaffold tested on 22 LTS)
- Docker Desktop
- (Optional) Stripe CLI for webhook forwarding

```powershell
# 1. Bring up local infra (Postgres + Redis + Azurite + Service Bus emulator)
docker compose up -d

# 2. Apply database migrations
dotnet run --project src/VrBook.Migrator

# 3. Run the API
dotnet run --project src/VrBook.Api
# → http://localhost:8080  (Swagger UI at /swagger)

# 4. Run the web app
cd web
npm install
npm run dev
# → http://localhost:3000
```

## Architecture in one paragraph

A .NET 8 **modular monolith** with eleven bounded contexts (Identity, Catalog, Pricing, Booking, Payment, Sync, Messaging, Reviews, Loyalty, Notifications, Admin). In-process traffic uses MediatR; cross-process traffic (workers, email dispatch, sync jobs) uses Azure Service Bus. PostgreSQL 16 holds all transactional data with one schema per context. Redis enforces booking holds via `SET NX PX`. The frontend is **Next.js 14 (App Router)** — SSR public pages for SEO, CSR for the admin. AirBnB sync is two-way iCal with a tentative-booking grace window for conflict resolution. Payments use **Stripe Connect Express**. All Azure compute runs on **Container Apps**. See [the proposal](./BookingApp_Proposal.md) §3 for the C4 diagrams.

## Documentation

- [Architecture Decision Records](./docs/adr/)
- [Runbooks](./docs/runbooks/)
- [Security threat model](./docs/security/threat-model.md)
- [OpenAPI spec](./contracts/openapi.yaml)
- [B2C policy notes](./docs/b2c/)

## Build & test

```powershell
dotnet build src/VrBook.sln
dotnet test  src/VrBook.sln

cd web
npm run lint
npm test
npm run build
```

## Contributing

The codebase is built by parallel agents (A0–A9, F1, O1) per [proposal §20](./BookingApp_Proposal.md). Read **Coordination Rules** in §20.3 before touching `src/VrBook.Contracts` or `contracts/openapi.yaml` — any change there requires a PR labelled `contracts-change` and a bump to `contracts/CHANGELOG.md`.

## License

Proprietary — all rights reserved. Internal use only.
