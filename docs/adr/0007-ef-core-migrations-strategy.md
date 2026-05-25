# 7. EF Core Migrations Strategy

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: database, ef-core, migrations, ci-cd, deployment

## Context and Problem Statement

The platform persists all transactional data in a single PostgreSQL 16 Flexible Server with one schema per bounded context: `identity`, `catalog`, `pricing`, `booking`, `payment`, `sync`, `messaging`, `reviews`, `loyalty`, `notifications`, `admin` (§3, §5). The modular monolith (ADR-0001) gives every context its own module project; §20.3 rule 3 — "Schema ownership. Each agent owns its schema's migrations. Cross-schema FKs are allowed but coordinated via PR review" — assigns migration authorship to the module that owns the schema.

The §16.3 CI/CD pipeline specifies the runtime model: "EF Core migrations are generated locally (`dotnet ef migrations add`) and committed. The `Migrator` console app runs as the first step of every deploy: in CI against staging DB to verify migrations apply cleanly; in CD invoked as a one-shot Container App Job before API revision update. Migrations must be **additive and backwards-compatible** with the running app (expand-then-contract: add columns nullable → deploy app → backfill → make NOT NULL in next release)."

The §16.2 cd-prod pipeline performs a blue/green Container App revision shift with a 15-minute soak. During the soak window, the old and new API revisions both serve traffic against the *same* migrated database. This is the structural reason expand-then-contract is mandatory: the old revision must still work against the new schema.

## Decision Drivers

- **Modular monolith with per-context schemas** (ADR-0001, §5) — schema authorship and migration authorship must align with module ownership.
- **Blue/green deploy with revision overlap** (§16.2 cd-prod) — schema must be backwards-compatible with the *previous* app version, not just the new one.
- **Local-author, repo-commit migrations** (§16.3) — no generate-on-deploy or auto-migration in production. Every schema change is reviewed in a PR.
- **No API process runs migrations in production** — `Database.MigrateAsync()` on app startup is forbidden; production migrations happen in a separate one-shot `Migrator` Container App Job.
- **Expand-then-contract rule** (§16.3) — additive nullable column → deploy app that writes both → backfill → next release tightens NOT NULL and removes old column.
- **Cross-schema FKs allowed, cross-schema joins forbidden** (§20.3) — referential integrity at the DB layer; query joins remain in-application via MediatR.
- **Phase 2 extraction escape hatch** (ADR-0001) — per-context schemas can be lifted to separate databases later. Migration ownership being per-module is what makes that possible.

## Considered Options

- **One DbContext per bounded context + Migrator console app + expand-then-contract** — Each module declares its own DbContext, owns its schema, owns its migrations; a separate Migrator process applies them.
- **One global DbContext** — Single DbContext spanning all entities; one migration history table.
- **Auto-migration on API startup** — `Database.MigrateAsync()` in `Program.cs` before host start.
- **Hand-written SQL migrations (e.g., DbUp, Flyway)** — Versioned SQL files instead of EF model-driven migrations.

## Decision Outcome

Chosen option: **"One DbContext per bounded context + Migrator console app + expand-then-contract"**, because it is the only option that satisfies the §20.3 schema-ownership rule, the §16.2 blue/green overlap requirement, the ADR-0001 extraction escape hatch, and the §16.3 explicit guidance — simultaneously and without compromise.

### Positive Consequences

- Each module's DbContext (`CatalogDbContext`, `BookingDbContext`, etc.) targets one schema. The DbContext, its entity configurations, and its migrations live in one project (e.g., `VrBook.Module.Catalog.Infrastructure`).
- Each schema has its own `__EFMigrationsHistory` table, scoped to the module's schema (`catalog.__EFMigrationsHistory`, `booking.__EFMigrationsHistory`, etc.). A migration to one schema does not lock or rewrite another.
- Migrations are generated locally by the owning agent (`dotnet ef migrations add AddPropertyRatingAverage --project src/VrBook.Module.Catalog.Infrastructure`) and committed as plain `.cs` files in the module's `Migrations/` folder. Reviewed in a PR like any code change.
- The `VrBook.Migrator` console app composes references to all module DbContexts and, on `Run()`, invokes `await dbContext.Database.MigrateAsync()` for each in deterministic order. Cross-schema FK dependencies dictate the order; the order is encoded in the Migrator code and reviewed.
- In CI: the Migrator runs against a Testcontainers-managed Postgres in the integration-test job — every PR proves the full migration set applies cleanly from empty.
- In staging CD: the Migrator runs against the staging DB before the API revision update. If migrations fail, the API deploy is aborted.
- In prod CD: the Migrator is a one-shot Container App Job invocation. Only after success does the API blue/green revision shift begin (§16.2).
- Expand-then-contract is the *only* allowed migration shape. A PR that adds a NOT NULL column without a backfill plan is rejected in review.
- For Phase 2 extraction: a module's schema is already self-contained — pointing its DbContext at a different connection string is a config change, not a schema split.

### Negative Consequences / Trade-offs

- Multiple DbContexts can be a source of confusion — engineers must remember which entities belong in which context. Mitigated by physical project boundaries (a `Property` entity cannot be referenced from `BookingDbContext` because the project doesn't reference `VrBook.Module.Catalog.Infrastructure`).
- Cross-context queries require either an in-memory join (call Catalog via MediatR, then query Booking) or a denormalised read-side projection. We accept this — it is the price of the modular boundary.
- The expand-then-contract rule means *every* schema change that ends in a tightening (e.g., add NOT NULL) is at least a two-release journey. This is slower than "stop the world, migrate, restart" but is the only safe shape for blue/green deploys.
- The Migrator must know about every module DbContext — a new module means a one-line addition to the Migrator's composition. Acceptable coupling; it is the *only* code in the system that legitimately references every module.
- Cross-schema FKs require coordination between the schemas that participate. Mitigated by §20.3 ("lead reviews all cross-schema FKs") and by adding FKs in the *referencing* schema's migration only after the *referenced* schema's migration has shipped.

## Pros and Cons of the Options

### One DbContext per bounded context + Migrator + expand-then-contract

- Good, because aligns with bounded-context boundaries, schema ownership, and the Phase 2 extraction story.
- Good, because the production deploy story (§16.2, §16.3) is exactly the runtime shape this design produces.
- Good, because each module's CI can run the module's migrations in isolation (faster integration-test loop than running the whole world).
- Good, because removing a module in Phase 2 (or splitting it into a separate database) is a configuration change.
- Bad, because requires expand-then-contract discipline — non-trivial reviewer cost.
- Bad, because cross-context joins are not allowed at the SQL level — pushed into application code.

### One global DbContext

- Good, because simplest possible EF Core configuration.
- Good, because cross-context joins are trivial.
- Bad, because directly contradicts §20.3 schema ownership — multiple agents modifying the same DbContext is the merge-conflict bullseye we are explicitly avoiding.
- Bad, because Phase 2 extraction becomes a major rewrite — there is no schema-shaped seam to extract along.
- Bad, because a migration touching the booking schema would generate a migration that EF Core's snapshot considers globally relevant — every PR's migration diff is noisy.
- Bad, because module reuse (e.g., the same Catalog module powering Phase 2's multi-tenant SaaS) is harder when the entities are entangled in one DbContext.

### Auto-migration on API startup

- Good, because zero deploy ceremony — push the image, the app migrates itself.
- Bad, because §16.3 explicitly forbids this. The reason is that during blue/green overlap (§16.2), the *first* new API replica to start would migrate the DB while old replicas are still running against the old schema — exactly the breaking scenario expand-then-contract is designed to prevent.
- Bad, because migration failures crash the API instead of failing a separate job — the deploy *partially* succeeds and the API is in a crash loop.
- Bad, because no review gate — the migration runs in production the first time it ever runs against the production schema. Catastrophic for any non-trivial change.

### Hand-written SQL migrations (DbUp / Flyway)

- Good, because the migration *is* the SQL — no EF Core generation black box to reason about.
- Good, because DBAs (when we have them) can audit the actual DDL trivially.
- Bad, because we lose EF Core's model snapshot diffing — a developer who forgets to write the migration ships entity-class drift from the database with no warning.
- Bad, because the §23.4 sample handler uses EF Core querying — pairing EF Core for runtime with raw SQL for migrations means the model snapshot still has to be maintained somewhere.
- Bad, because adopted purely for migration purity, this is paying the cost of two systems for the benefit of one.

## Migrator Application

`src/VrBook.Migrator/Program.cs` — minimal console host:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddAzureKeyVault(/* … */);

// register every module DbContext in deterministic order
builder.Services.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<PricingDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<BookingDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<PaymentDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<MessagingDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<LoyaltyDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(/* … */));
builder.Services.AddDbContext<AdminDbContext>(o => o.UseNpgsql(/* … */));

using var app = builder.Build();

foreach (var ctxType in MigrationOrder.InDependencyOrder)
{
    using var scope = app.Services.CreateScope();
    var ctx = (DbContext)scope.ServiceProvider.GetRequiredService(ctxType);
    var pending = await ctx.Database.GetPendingMigrationsAsync();
    if (pending.Any())
    {
        Log.Information("Applying {Count} migrations to {Schema}", pending.Count(), ctxType.Name);
        await ctx.Database.MigrateAsync();
    }
}
```

In production this runs as a Container App Job invoked by the cd-prod workflow before the API revision update. Idempotent: if there are no pending migrations, it does nothing and exits 0. Exit non-zero on any failure; the workflow aborts and the API revision update never happens.

## Expand-then-Contract Pattern

The §16.3 rule, restated as a checklist for every column-tightening change:

1. **Release N — Expand.** Add the new column as nullable (or with a default). Migration ships. Deploy app version N which *writes* to the new column and *reads from* either old or new (preferring new when present, falling back to old).
2. **Backfill.** Run a one-shot data migration (`dotnet run --project VrBook.Migrator -- backfill <name>`) that populates the new column for existing rows.
3. **Release N+1 — Contract.** Migration tightens the column to NOT NULL and removes the old column. Deploy app version N+1 which writes and reads only the new column.

A PR that proposes both steps in one release is rejected with the rule cited. The only exception is a brand-new table with no consumers — those are atomic by definition.

## Why the API Never Runs Migrations

The single most important property of this design — repeated for emphasis because it is the property that most teams violate — is that the API process *never* applies migrations in production. The reasons compound:

1. **Blue/green overlap.** During the §16.2 15-minute soak window, old-revision and new-revision API replicas serve traffic against the same database. If the new revision auto-migrated on startup, the old revision would suddenly be querying a schema it doesn't understand. Expand-then-contract avoids the schema mismatch; running migrations from a separate Job before either revision rolls keeps it predictable.
2. **Failure isolation.** A migration failure should fail the deploy, not crash-loop the API. A crash-looping API on startup is harder to diagnose and rollback than a failed Job invocation with a clear exit code and full log.
3. **Permission scope.** The Migrator runs with a Managed Identity that has DDL privileges on each schema. The API runs with a Managed Identity that has only DML privileges. Defence-in-depth — an exploited API cannot rewrite the schema.
4. **Concurrency.** If three API replicas start simultaneously (autoscaler reacting to load), three of them attempting `Database.MigrateAsync()` race for the migration history table. EF Core handles this via advisory lock, but the failure modes (lock timeouts, partial application) are uglier than the single-Job invariant.
5. **Audit trail.** Every Job invocation logs its own deploy ID, the migrations applied, and the runtime. A startup-time migration is buried in API startup logs.

## CI Verification

The integration-test job in `ci.yml` spins a fresh Postgres via Testcontainers, runs the Migrator console app against it, and only then runs the integration test suite. Every PR therefore proves that the full migration set applies cleanly from empty — which guards against the class of bug where a migration is committed without its corresponding entity-class change (or vice versa).

A separate CI job, `migration-replay`, runs the Migrator against a copy of the staging database (restored from a recent backup) before a PR is allowed to merge to `develop`. This catches migrations whose `Up` works on an empty database but fails against the production-shaped data (e.g., a NOT NULL tightening without a backfill plan).

## Cross-Schema FK Coordination

§20.3 allows cross-schema FKs but requires lead review. The workflow:

1. The *referenced* schema (e.g., `catalog.properties`) is unchanged.
2. The *referencing* schema (e.g., `booking.bookings`) adds a `property_id uuid` column.
3. The migration declares the FK: `entity.HasOne<Property>().WithMany().HasForeignKey(b => b.PropertyId).HasConstraintName("fk_bookings_property_id");` with the constraint targeting `catalog.properties(id)`.
4. EF Core does not know the `Property` entity exists in `BookingDbContext` — we use the `Configure` method's `HasOne<Property>()` form pointing at a *shadow* navigation, or we declare the FK in raw SQL inside the migration.
5. The lead reviews to confirm the cross-schema dependency is justified (typically: tight referential integrity is required and a soft reference would risk orphaned rows).

The Phase 2 extraction story for cross-schema FKs: when a module is extracted to its own database, the FK becomes an application-level invariant (we cannot enforce FKs across database instances). The reviewing lead's role is to ensure each cross-schema FK is one we are comfortable demoting to an application invariant if extraction happens.

## Links

- [Proposal §3 Solution Architecture Overview](../../BookingApp_Proposal.md)
- [Proposal §5 Database Schema](../../BookingApp_Proposal.md)
- [Proposal §16.2 Pipelines (blue/green revision shift)](../../BookingApp_Proposal.md)
- [Proposal §16.3 Database Migrations](../../BookingApp_Proposal.md)
- [Proposal §20.3 Coordination Rules (schema ownership)](../../BookingApp_Proposal.md)
- ADR-0001 Modular Monolith
- [EF Core Migrations docs](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)
- [Pramod Sadalage & Martin Fowler — Evolutionary Database Design](https://martinfowler.com/articles/evodb.html)
