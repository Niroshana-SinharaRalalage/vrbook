# 1. Modular Monolith for Phase 1

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: architecture, backend, ddd, deployment

## Context and Problem Statement

Phase 1 of the direct-booking platform must ship in 8 weeks with a small (9-agent) parallel team and very tight operational headcount (no dedicated SRE, no Kubernetes operator). The proposal's §1 calls the deliverable "a Phase 1 direct-booking platform … on Azure Container Apps" with "eleven bounded contexts (Identity, Catalog, Pricing, Booking, Payment, Sync, Messaging, Reviews, Loyalty, Notifications, Admin/Reporting)." Eleven bounded contexts read at first glance like an invitation to eleven microservices.

The §3.4 patterns table is explicit about how those contexts collaborate: "Internal communication uses MediatR in-process notifications; cross-process communication (workers, email dispatch, sync jobs) uses Azure Service Bus." That is a deliberate split — in-process messaging *within* the request-scoped transactional boundary, asynchronous messaging *across* worker processes — and it only works cleanly if the contexts ship in one deployable. The single most important engineering constraint, "zero double-bookings" (§1), is owned by the Booking context but reaches into Sync (conflict detection), Payment (auth/capture), and Pricing (snapshot). Splitting any of those across a network boundary in Phase 1 would push a distributed-transactions problem onto a team that has not yet stood up its first observability dashboard.

Phase 2 (§22) explicitly anticipates multi-tenant SaaS, additional OTA channels, AI pricing, and a mobile client. Each of those is a plausible candidate for later extraction. The architecture must therefore be modular enough that a Phase 2 team can lift, e.g., Pricing or Sync into its own service without rewriting the bounded context.

## Decision Drivers

- 8-week delivery window with 9 parallel agents — operational simplicity dominates.
- Zero-double-booking invariant spans Booking, Sync, Payment, Pricing — requires a single transactional context to be enforceable cheaply.
- Container Apps target — one deploy unit per workload is the cheapest scale-to-zero shape.
- DDD bounded contexts already exist (§4) — module boundaries are designed, not improvised.
- Phase 2 may legitimately extract a module — escape hatch must be real, not aspirational.
- Team size and on-call rotation cannot support 11 independently deployable services.

## Considered Options

- **Modular Monolith** — One ASP.NET Core API + a few workers, all sharing one Postgres with per-context schemas, MediatR in-process, Service Bus for cross-process.
- **Microservices per Bounded Context** — 11 services, each with its own database, communicating exclusively over Service Bus and HTTP.
- **Macro-services (Booking-Core vs Sync vs Notifications)** — Split only the workloads with genuinely different scale or schedule profiles.
- **Big Ball of Mud (single project, no module boundaries)** — All code in one assembly, no enforced context separation.

## Decision Outcome

Chosen option: **"Modular Monolith"**, because it is the only option that lets a 9-agent team meet the 8-week deadline while preserving the bounded-context structure the domain requires and the operational simplicity Container Apps rewards. The Phase 2 escape hatch is explicit: each module's public surface is a `MediatR` request/notification contract plus its database schema; replacing the in-process MediatR dispatch with a Service Bus publisher (and the cross-schema read with an HTTP call) is a mechanical change, not a redesign.

### Positive Consequences

- One deploy unit (the API), three Container App Jobs (Sync, Booking, Notifications) — minimum viable operational surface.
- Strong consistency where it matters most: booking placement, payment-intent creation, and outbox-event persistence all happen in one Postgres transaction.
- In-process MediatR is microsecond-latency, no serialization, no retry policy to design — the booking-placement happy path is one HTTP request and one DB commit.
- Per-context schemas (Identity, Catalog, Pricing, Booking, Payment, Sync, Messaging, Reviews, Loyalty, Notifications, Admin) make schema ownership unambiguous (see §20.3: "Each agent owns its schema's migrations") and enable future extraction one schema at a time.
- The OpenAPI surface and the shared `VrBook.Contracts` library are the natural seam for a future extraction — they already exist as deliverables.
- Cost: one Postgres Flexible Server, one Redis, one Container Apps Environment — well inside the §15.2 prod budget of ~$1,200/month light.

### Negative Consequences / Trade-offs

- A noisy module (e.g., a pricing-engine bug eating CPU) affects the whole API process. Mitigation: workers are separate Container Apps Jobs so background load is already isolated.
- Independent scaling is per-process, not per-context. Mitigation: in Phase 1 traffic profile, the API process is one workload; KEDA autoscaling on Container Apps handles it.
- Risk of "modular monolith in name only" — modules grow cross-references and become a ball of mud. Mitigation: enforced via solution structure (one `*.Module` project per context), `dotnet list reference` review in CI, and ADR-0007's per-context DbContext rule.
- Phase 2 extraction is mechanical but not free — an extracted module pays for HTTP serialization, eventual consistency, and a new deploy pipeline.

## Pros and Cons of the Options

### Modular Monolith

- Good, because one deploy and one database is the cheapest possible operational shape for an 8-week team.
- Good, because in-process MediatR dispatches are essentially free — no retry, no idempotency, no serialization design needed within a request.
- Good, because the §7 booking state machine and the §9 Stripe sequence both rely on transactional consistency between booking rows and payment-intent rows; one DB makes this a `BEGIN; … COMMIT;`.
- Good, because module boundaries are still enforced (per-context schemas, per-module assembly, MediatR-only public surface).
- Bad, because a memory leak or hot loop in one module can degrade the whole API.
- Bad, because one Postgres becomes a long-term scaling ceiling if data growth surprises us — but Phase 1 traffic is bounded by one owner's properties (see §21 R7).

### Microservices per Bounded Context

- Good, because each module scales and deploys independently.
- Good, because module ownership is enforced by network boundaries — no accidental coupling possible.
- Bad, because 11 services × CI pipeline × Dockerfile × Bicep module × on-call runbook is multiple person-weeks of overhead before any feature ships.
- Bad, because the zero-double-booking invariant now requires a saga across Booking, Sync, and Payment — distributed transactions, compensating actions, and eventual consistency on the most critical user-visible operation.
- Bad, because §9.3's booking-placement sequence (which already touches DB, Redis, Stripe, and Service Bus in one request) would become an inter-service orchestration.
- Bad, because 11 services need 11 sets of secrets, 11 sets of identities, 11 sets of dashboards — the operational tail is huge for a team without an SRE.

### Macro-services

- Good, because it captures most of the deployment-isolation benefit at a fraction of the cost.
- Good, because it aligns with the §3.2 container diagram (API + 3 workers) which is already a 4-service split by *workload type*, not by bounded context.
- Bad, because choosing which contexts go in which macro-service is itself a design problem we don't need to solve in W1 — the modular monolith subsumes this and the §3.2 split already gives us the *workload* separation.

### Big Ball of Mud

- Good, because it ships fastest in the first week.
- Bad, because it makes the Phase 2 extraction escape hatch impossible — there are no boundaries to extract along.
- Bad, because parallel agent work becomes a continuous merge conflict on the same files.

## Phase 2 Escape Hatch

The proposal §22.4 calls out multi-tenant SaaS as a Phase 2 line item, and §22.2 envisions an additional Booking.com Connectivity Partner integration that may want its own deploy cadence. The following invariants make Phase 2 extraction tractable:

1. **One DbContext per module** (see ADR-0007) — a module's data is reachable only through its own DbContext, which means the schema can be lifted to a separate database with no application-code change beyond the connection string.
2. **MediatR is the only in-process API surface** — every cross-module call is `IRequest<T>` or `INotification`. Replacing the in-process publisher with a Service Bus publisher is one DI registration change.
3. **`VrBook.Contracts` already defines the inter-module DTOs and event payloads.** Those become the wire contracts of the extracted service unchanged.
4. **Workers are already separate Container Apps** (Sync, Booking, Notification per §3.2). Extracting a context that owns one of those workers means the context already has a process to call its own.
5. **No cross-schema joins in application code.** Cross-schema *FKs* are allowed (§20.3) for referential integrity, but query joins across schemas are prohibited — modules read each other only through MediatR.

These invariants are checked in code review (cross-schema joins surface in EF Core configuration) and in CI (`VrBook.Module.X` project references only `VrBook.Module.X.Contracts` from other modules, never `VrBook.Module.X.Domain` or `.Infrastructure`).

## Solution Layout (Concrete)

```
/src
  VrBook.Api/                     # composition root — references every module
  VrBook.Contracts/               # cross-module DTOs and event payloads (sacred)
  VrBook.SharedKernel/            # primitive value objects (Money, DateRange)
  VrBook.Module.Identity/         # Domain + Application + Infrastructure
  VrBook.Module.Catalog/
  VrBook.Module.Pricing/
  VrBook.Module.Booking/          # the core context — owns the state machine
  VrBook.Module.Payment/
  VrBook.Module.Sync/
  VrBook.Module.Messaging/
  VrBook.Module.Reviews/
  VrBook.Module.Loyalty/
  VrBook.Module.Notifications/
  VrBook.Module.Admin/
  VrBook.Workers.Booking/         # KEDA Service-Bus-triggered Container App
  VrBook.Workers.Notifications/
  VrBook.Workers.Sync/            # cron-scheduled Container App Job
  VrBook.Migrator/                # one-shot Container App Job, see ADR-0007
```

Each module's project structure follows Clean Architecture (§3.4): `Domain` (entities, value objects, events), `Application` (handlers, validators, behaviours), `Infrastructure` (DbContext, EF configs, external clients). Composition happens in `VrBook.Api/Program.cs` via per-module `IServiceCollection` extension methods (`services.AddBookingModule(configuration)`, etc.).

## Runtime Topology

The §3.2 container diagram already realises the modular-monolith deployment shape:

- **One API Container App** hosts all 11 modules — every HTTP endpoint, every MediatR handler.
- **Three worker Container Apps** (Booking, Notifications, Sync) subscribe to Service Bus topics and run module handlers in worker-host processes that compose only the modules they need.
- **One Migrator Container App Job** runs schema migrations before each API revision (ADR-0007).

Cross-process communication is exclusively Service Bus messages whose payloads are types from `VrBook.Contracts`. The same DTO type that an in-process `MediatR.INotification` handler receives, a worker receives as a Service-Bus-deserialised payload. Extracting a module to its own service means swapping its `MediatR.Publish` for a Service Bus publish — nothing about the message shape changes.

## Anti-Patterns We Explicitly Refuse

- **Direct cross-module DbContext access.** `BookingDbContext` cannot reach `Property` entities; it asks Catalog via MediatR (`IRequest<PropertySummary>`).
- **Cross-module EF Core navigation properties.** A `Booking` entity has a `PropertyId` value, not a `Property` reference.
- **"Shared" `Common` modules.** Anything truly shared is either a primitive (`Money`, `DateRange` in `SharedKernel`) or a contract (in `Contracts`). There is no `VrBook.Common.BusinessLogic` dumping ground.
- **In-process events that cross transactional boundaries silently.** Domain events are published *after* the transaction commits (outbox pattern, §3.4), not from inside the aggregate's `Save()`.

## When We Would Reconsider

We would reopen this ADR if any of the following becomes true:

- A module's traffic shape diverges sharply from the rest (e.g., Sync becomes a high-throughput data-pipeline workload). Extract Sync first.
- A regulatory boundary requires data isolation that one Postgres cannot provide (e.g., EU data residency for a future region).
- A module needs to ship on a fundamentally different cadence than the rest (e.g., a fast-iterating AI pricing service that wants daily deploys while Booking deploys monthly).
- Team size grows past ~25 engineers and the merge-conflict surface on the monolith starts costing more than the operational overhead of splitting would.

None of these conditions hold for Phase 1. We expect at least one to hold by mid-Phase-2.

## Links

- [Proposal §1 Executive Summary](../../BookingApp_Proposal.md)
- [Proposal §3 Solution Architecture Overview](../../BookingApp_Proposal.md)
- [Proposal §4 Bounded Contexts & Domain Model](../../BookingApp_Proposal.md)
- [Proposal §7 Booking State Machine](../../BookingApp_Proposal.md)
- [Proposal §22 Phase 2 Roadmap](../../BookingApp_Proposal.md)
- ADR-0002 Monorepo
- ADR-0007 EF Core Migrations Strategy
- Sam Newman, *Monolith to Microservices* — the modular-monolith starting point
