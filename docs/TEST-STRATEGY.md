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
- **`VrBook.Architecture.Tests/EndpointCoverageArchTest.cs`** — the coverage guard. **Today** it asserts every action carries an explicit access decision (`[Authorize]`/`[AllowAnonymous]`/`[ExemptFromCrossTenantMatrix]`). Its own XML-doc notes the **second half is not yet enforced**: "every authenticated action appears in `RouteMatrix.GetAll()` OR is exempt." **VRB-300 closes that second half** — that is the real endpoint-coverage gate.
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

The gate is the **strengthened `EndpointCoverageArchTest`** (VRB-300): every authenticated controller action must **either appear in `RouteMatrix.GetAll()` or carry `[ExemptFromCrossTenantMatrix("reason")]`** — the build fails naming any action that is neither. This turns "cover the endpoint" from a request into a build break: a new endpoint without a matrix row (and, per ENGINEERING-RULES §3, its contract tests) cannot go green. Exemptions are for genuinely un-matrixable actions only, each with a documented reason — never a silent skip.

Note the split of concerns: the **matrix + arch test** guarantee *every endpoint is authorization-covered and accounted for*; the **per-module `Contract/*` classes** add the *happy-path / validation / error-contract / idempotency* assertions the status-set matrix intentionally does not make. Both are required for an endpoint to be "covered."

## Running it

- **Local (Docker up):** `dotnet test tests/VrBook.Api.IntegrationTests/ --filter Category=Integration`.
- **Local (Docker off):** unit only — `dotnet test src/VrBook.sln --filter "Category!=Integration"`. The API suite is skipped; don't mistake that for green.
- **CI:** the integration job runs the full suite against a service-container/Testcontainer Postgres; it is a **blocking gate** on `develop` and in `cd-prod.yml`.
- **Staging:** the anonymous Playwright smoke + the curl smoke already assert the deployed contract is live; the full contract suite is a build-time gate, not a staging job.

## Verification loop (per playbook §5)

Unit + integration green → **full API contract suite green** → then merge. A red suite blocks the merge, full stop. If your change legitimately alters a contract, update the contract test **and** the OpenAPI/Pact artifact in the same PR — a drift-gate failure is a real failure, not noise.
