# Architecture Decision Records

This directory holds the Architecture Decision Records (ADRs) for the Phase 1 direct-booking vacation rental platform. Each ADR follows the [MADR](https://adr.github.io/madr/) template and explains *why* a specific architectural choice in [`BookingApp_Proposal.md`](../../BookingApp_Proposal.md) was made.

The proposal is the source of truth for *what* is being built. These ADRs are the source of truth for *why*.

## Status Legend

- **Accepted** — Decision is in effect; the rest of the codebase depends on it.
- **Proposed** — Under discussion; not yet binding.
- **Superseded** — Replaced by a later ADR (linked).
- **Deprecated** — No longer in effect; left for historical context.

## Index

| # | Title | Status | Summary |
|---|---|---|---|
| [0001](./0001-modular-monolith.md) | Modular Monolith for Phase 1 | Accepted | Single deploy unit with 11 bounded-context modules, in-process MediatR, per-context Postgres schemas; extraction escape hatch preserved for Phase 2. |
| [0002](./0002-monorepo.md) | Monorepo Layout | Accepted | One repo for backend, frontend, contracts, infra, and tests — enables atomic cross-cutting changes and parallel agent worktrees off a single source of truth. |
| [0003](./0003-stripe-connect-express.md) | Stripe Connect Express | Accepted | Platform-branded checkout with Stripe-managed onboarding and tax-form handling; multi-tenant-ready without re-platforming for Phase 2. |
| [0004](./0004-sendgrid-transactional-email.md) | SendGrid for Transactional Email | **Superseded by [0011](./0011-azure-communication-services-email.md)** | Original Phase 1 choice — kept for historical context. Reversed on 2026-05-26 in favor of ACS to match LankaConnect. |
| [0005](./0005-signalr-serverless.md) | Azure SignalR Service in Serverless Mode | Accepted | Managed real-time service holds WebSocket connections so the API stays stateless and Container Apps can scale to zero; no backplane needed. |
| [0006](./0006-nextjs-app-router.md) | Next.js 14 (App Router) for the Web Layer | Accepted | SEO-critical SSR + ISR for public listing pages; RSC for data-heavy renders; same TS codebase serves SSR public and CSR admin segments. |
| [0007](./0007-ef-core-migrations-strategy.md) | EF Core Migrations Strategy | Accepted | One DbContext per bounded context; migrations authored locally; expand-then-contract enforced; Migrator Container App Job runs before each API deploy. |
| [0008](./0008-bicep-modules.md) | Bicep for Infrastructure as Code | Accepted | Azure-native IaC with per-module + per-environment structure; `what-if` gate in CI; no-drift policy with weekly detection. |
| [0009](./0009-branch-strategy.md) | Branch Strategy, Worktrees, and Release Versioning | Accepted | `main` = prod, `develop` = staging, agents work in `feat/aN-*` worktrees; conventional commits drive semver tags on `main`. |
| [0010](./0010-observability-stack.md) | Observability Stack — Serilog + App Insights + Log Analytics | Accepted | Structured logging with standard properties (userId, traceId, bookingId, propertyId, correlationId); PII redacted at the sink; Workbook + alerts deployed as code. |
| [0011](./0011-azure-communication-services-email.md) | Azure Communication Services Email (supersedes ADR-0004) | Accepted | Switch transactional email from SendGrid to ACS to match LankaConnect's existing stack; one vendor, managed-identity auth, single Azure bill. |

## How to add a new ADR

1. Copy the most recent ADR as a template and rename to `NNNN-kebab-title.md` with the next available number.
2. Set `Status: Proposed` and the current date.
3. Open a PR. The PR conversation is the discussion record.
4. On merge, flip `Status` to `Accepted` (or `Rejected` and leave the file for history).
5. If the ADR supersedes a previous one, set the previous ADR's `Status` to `Superseded by ADR-NNNN` in a follow-up PR.

## Conventions

- Every ADR uses the MADR template (Status / Date / Deciders / Tags, then Context / Drivers / Options / Outcome / Pros and Cons / Links).
- Every ADR links back to the relevant section(s) of `BookingApp_Proposal.md`.
- Decisions are *not* edited in place after acceptance — they are superseded by a new ADR. The history is the asset.
