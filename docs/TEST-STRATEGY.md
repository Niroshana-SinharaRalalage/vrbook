# VrBook — Test Strategy (the API contract suite + the layers under it)

The safety net that lets many agents ship into one codebase without breaking each other. The **API contract suite (VRB-300)** is the load-bearing layer: every HTTP endpoint has contract tests, and the suite must be green before any merge. This doc defines the tooling, fixtures, test-data, and auth so an agent adds coverage the same way every time — no new harness per story.

## The pyramid (what runs where)

| Layer | Framework | Needs | Runs | Owns |
|---|---|---|---|---|
| **Unit** | xUnit (backend) · Vitest (web) | nothing (no Docker) | local + CI (`Category=Unit` / `!=Integration`) every push | the story's lane |
| **Integration / API contract** (VRB-300) | xUnit + `WebApplicationFactory<Program>` + **Testcontainers Postgres** | Docker | CI job + local when Docker is up (`Category=Integration`) | Lane TEST owns the harness; **each lane writes the tests for endpoints it ships** |
| **Contract drift** | Pact (`VrBook.Api.PactTests`) + OpenAPI drift gate | Docker | CI (existing) | DEVOPS |
| **E2E** | Playwright (`web/tests/e2e`) against staging | deployed staging + Entra personas | nightly (informational) + anonymous smoke (blocking) | WEB lanes |

The API contract suite is **integration-level** (real HTTP through the real middleware pipeline against a real Postgres), not unit-level — that's what makes it a *contract* test: it exercises auth, RLS, validation, and serialization exactly as production does.

## The harness (build on it — do NOT stand up a new one)

`tests/VrBook.Api.IntegrationTests/` already provides everything VRB-300 needs:

- **`Multitenancy/TwoTenantApiFixture.cs`** — a `WebApplicationFactory<Program>` that starts a `postgres:16-alpine` Testcontainer, applies **every module's migrations in production order**, and seeds a deterministic two-tenant world: `TenantA`/`TenantB` (fixed GUIDs), `OwnerA`/`OwnerB` (`tenant_admin` memberships), a `PlatformAdmin` (no membership), one `Property` per tenant, and the `user_identities` rows the provisioning middleware expects. Use `[Collection(nameof(TwoTenantApiCollection))]` to share one container across a test class.
- **`CreateClientAs(persona)`** — returns an `HttpClient` for `"OwnerA"` / `"OwnerB"` / `"PlatformAdmin"` / `null` (anonymous). This is how you assert **authorization and tenant isolation**: call an endpoint as `OwnerB` against `TenantA`'s resource and assert 403/404.
- **`Multitenancy/TwoTenantTestAuthHandler.cs` + `Auth/TestAuthHandler.cs`** — replace the production JwtBearer scheme with a test handler that synthesizes an Entra-shaped `ClaimsPrincipal` from the `X-Test-Persona` header. Downstream production middleware (`UserProvisioningMiddleware`, `TenantAuthorizationBehavior`, RLS interceptor) treats it identically to a real token. **Never** add a test-only `[AllowAnonymous]` or bypass to production code to make a test pass — the arch tests (`OpsOps2_AdminSurfaceAndTestBackdoorTests`) fail on exactly that.
- **`RlsBypassScope`** — for cross-tenant seed setup only; never in the assertion path.

New tenants/users/resources a scenario needs are seeded **inside the test** under an `RlsBypassScope`, using the real domain factories (`Property.Create`, `User.Provision`, …) with unique value-object instances per principal (EF `OwnsOne` re-stamps a shared VO — see the fixture's `NewAddress()` note). Deterministic GUIDs so failures are grep-able.

### The auth/isolation matrix already exists (extend it, don't rebuild)

Slice OPS.M.10 already shipped the coverage backbone — VRB-300 **completes and extends** it:

- **`Multitenancy/RouteMatrix.cs`** — the **single source of truth** enumeration of endpoint × persona × tenant. Each row is a `Cell(description, verb, route, persona, targetTenant, acceptedStatuses[], bodyFactory?)`. It covers **~29 of the ~98 controller actions** today, focused on **authorization + cross-tenant isolation** (the `AcceptedStatuses` *set* proves "which statuses are acceptable for this persona at this tenant," not the business outcome). **Adding an endpoint = adding a row here.**
- **`Multitenancy/CrossTenantEndpointMatrix.cs`** — the `[Theory]` (`[MemberData(RouteMatrix.GetAll)]`) that runs every cell: substitutes `{tenantId}`, sends as the persona, asserts status ∈ `AcceptedStatuses`.
- **`VrBook.Architecture.Tests/EndpointCoverageArchTest.cs`** — the coverage guard, **both halves now enforced (VRB-300)**. `Every_controller_action_carries_an_explicit_access_decision` is the access-decision half (`[Authorize]`/`[AllowAnonymous]`/`[ExemptFromCrossTenantMatrix]`). `Every_authenticated_action_appears_in_the_route_matrix_or_is_exempt` is the matrix-membership half: every **authenticated** action (behind `[Authorize]`, not `[AllowAnonymous]`) must appear in `RouteMatrix.GetAll()` — matched on **HTTP verb + route template (route-parameter names/constraints normalised away)** — or carry `[ExemptFromCrossTenantMatrix("reason")]`. The build **fails naming every uncovered action**. `Coverage_gate_bites_when_a_matrix_row_is_removed` proves the gate is not decorative: dropping a row turns it red. This is a **blocking, Docker-free** arch test (it reads `RouteMatrix.GetAll()` as pure data), so a new endpoint without a matrix row cannot merge.
- **`ExemptFromCrossTenantMatrixAttribute`** (`VrBook.Api.Common`) — marks an action deliberately out of the matrix, with a **required non-empty reason**.

## What every endpoint's contract tests must cover

For each endpoint a story adds or changes (this is a DoD line in [`ENGINEERING-RULES.md`](ENGINEERING-RULES.md) §3):

1. **Happy path** — valid request as the correct persona → expected 2xx + response body shape.
2. **Authentication** — anonymous (`CreateClientAs(null)`) → 401 on authed routes.
3. **Authorization / tenant isolation** — wrong tenant (`OwnerB` at `TenantA`'s resource) → 403/404; `PlatformAdmin`-only routes reject `OwnerA`; tenant-scoped writes honour `HasTenantRole`.
4. **Validation** — malformed / missing / out-of-range input → 400 with the validation problem shape.
5. **Error contract** — the documented status + `application/problem+json` body (note the Hellang-middleware caveat: custom fields on 4xx/5xx are stripped — assert status + problem `type`, not custom detail fields).
6. **Idempotency** — for mutating endpoints that promise it, a repeated request with the same key does not double-apply.

## Coverage enforcement (no silent gaps)

The gate is the **strengthened `EndpointCoverageArchTest`** (VRB-300, shipped): every authenticated controller action must **either appear in `RouteMatrix.GetAll()` or carry `[ExemptFromCrossTenantMatrix("reason")]`** — the build fails naming any action that is neither. This turns "cover the endpoint" from a request into a build break: a new endpoint without a matrix row (and, per ENGINEERING-RULES §3, its contract tests) cannot go green. Exemptions are for genuinely un-matrixable actions only, each with a documented reason — never a silent skip. VRB-300 landed the coverage with **zero exemptions** — every one of the ~88 authenticated actions carries at least an authentication-required row; PlatformAdmin-only and `{tenantId}`-scoped surfaces carry their role/cross-tenant rejection rows too.

How the matrix drives an anonymous/role row without a live resource: `CrossTenantEndpointMatrix` substitutes `{tenantId}` (→ the target tenant) and `{propertyId}` (→ that tenant's seeded property), and fills every **other** route parameter with a well-formed placeholder — the authentication challenge (401) and the role/tenant gate (403) both fire *before* the handler loads the resource, so those rows are exact and resource-independent.

Note the split of concerns: the **matrix + arch test** guarantee *every endpoint is authorization-covered and accounted for*; the **per-module `Contract/*` classes** (`tests/VrBook.Api.IntegrationTests/Contract/<Module>/*`, `Category=Integration`) add the *happy-path body / error-contract (`problem+json` `type`) / role gate / idempotency* assertions the status-set matrix intentionally does not make. VRB-300 seeds this substrate for the Identity (`/me`), Catalog (`/admin/amenities`) and Platform (`/admin/platform/tenants`) surfaces; **each feature lane extends its own `Contract/<Module>/*` for the endpoints it ships** (ENGINEERING-RULES §3), including per-resource cross-tenant isolation (OwnerB at another tenant's booking/property) and validation→400 exemplars — those need module-specific seeding and are best written by the lane that owns (and can drive) the endpoint. Both the matrix row **and** the module's contract tests are required for an endpoint to be "covered."

## Running it

- **Local (Docker up):** `dotnet test tests/VrBook.Api.IntegrationTests/ --filter Category=Integration`.
- **Local (Docker off):** unit only — `dotnet test src/VrBook.sln --filter "Category!=Integration"`. The API suite is skipped; don't mistake that for green.
- **CI:** the integration job runs the full suite against a service-container/Testcontainer Postgres; it is a **blocking gate** on `develop` and in `cd-prod.yml`.
- **Staging:** the anonymous Playwright smoke + the curl smoke already assert the deployed contract is live; the full contract suite is a build-time gate, not a staging job.

## Verification loop (per playbook §5)

Unit + integration green → **full API contract suite green** → then merge. A red suite blocks the merge, full stop. If your change legitimately alters a contract, update the contract test **and** the OpenAPI/Pact artifact in the same PR — a drift-gate failure is a real failure, not noise.
