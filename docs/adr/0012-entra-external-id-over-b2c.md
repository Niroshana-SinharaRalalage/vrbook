# 12. Microsoft Entra External ID as the identity provider (replaces proposal §14 AD B2C)

- Status: Accepted
- Date: 2026-05-26
- Deciders: Solutions Architecture
- Tags: identity, b2c, ciam, entra, supersession

## Context and Problem Statement

Proposal §14 specifies **Azure AD B2C** as the identity provider for guest, owner, and admin authentication. The choice was made in early 2026 based on B2C being the mature, well-documented Microsoft CIAM option with user flows for SignUpSignIn / PasswordReset / ProfileEdit, custom extension attributes (`extension_isOwner`, `extension_isAdmin`), and JWT bearer integration via ASP.NET Core's `JwtBearer` handler.

During bootstrap (2026-05-26), attempting to create the dev B2C tenant surfaced an architectural reality I should have caught at proposal time: **Azure AD B2C has been in late-stage sunset since May 1, 2025.** Per [Microsoft's FAQ](https://learn.microsoft.com/en-us/azure/active-directory-b2c/faq):

> "Effective May 1, 2025, Azure AD B2C will no longer be available to purchase for new customers, but current Azure AD B2C customers can continue using the product."

The replacement is **Microsoft Entra External ID** (specifically, the "external tenant configuration" — a separate tenant kind from workforce tenants). For a fresh Azure subscription with no prior B2C tenant — VrBook's situation — new B2C tenant creation is at minimum *uncertain* and is being reported as outright blocked in community Q&A.

This ADR records the pivot from B2C to Entra External ID, the architecture impact, and the migration of files already written against the B2C assumption.

## Decision Drivers

- **Microsoft's stated direction.** B2C is in maintenance until May 2030; Entra External ID is the active product line for net-new CIAM.
- **Avoid throwaway work.** A1 (Identity module) is the only place we've coded against B2C-specific URL patterns. Catching this before any other module ships keeps the blast radius small.
- **Feature parity check.** Proposal §14.1 *explicitly chose user flows over custom policies* (R3 in §21). Entra External ID supports user flows. Its custom-policy gap (the one feature B2C had that External ID doesn't fully match yet) does not apply to us.
- **Single Microsoft ecosystem.** Same Azure subscription, same MI story for ACS/SignalR/Blob — no new vendor.
- **Claim names are stable.** Tokens issued by Entra External ID use the standard `oid`, `email`, `email_verified`, `name` claims — same as B2C. Custom attributes still surface as `extension_*` claims. The A1 code reading these (`HttpCurrentUser`, `UserProvisioningMiddleware`) is unaffected.

## Considered Options

- **A) Force B2C creation through workarounds (e.g., partner channels, contact Microsoft sales).** Possible but slow and uncertain; builds on a sunsetting product.
- **B) Pivot to Microsoft Entra External ID now.** Update A1's URL patterns, rename config keys, regenerate docs/scripts. ~30 lines of code touched.
- **C) Use plain Azure AD (workforce tenant) with self-service sign-up.** Wrong fit: workforce tenants are for employees + B2B guests, not anonymous consumers. SaaS-style sign-up flows would require heavy customization.
- **D) Switch identity provider entirely (Auth0, Clerk, Okta CIC).** Out of scope. Proposal §14 anchored on Microsoft for the same reasons (single billing, KV integration, etc.) and those reasons still hold.

## Decision Outcome

Chosen: **Option B — Microsoft Entra External ID**.

### Positive Consequences

- Building on Microsoft's current product line, not a 4-year-from-EOL one.
- No future B2C → Entra migration needed.
- Standard Microsoft.Identity.Web / ASP.NET Core `JwtBearer` integration; URL pattern is conventional `{tenant}.ciamlogin.com/{tenantId}/v2.0/.well-known/openid-configuration`.
- App registrations are done in the standard Entra App registrations blade — same UI as workforce tenant apps.
- Managed identity story unchanged.

### Negative Consequences / Trade-offs

- The `vrbook-claude-sp` service principal lives in the **workforce tenant**, not the External tenant. App-registration commands for `vrbook-api` / `vrbook-web` must be run by the user via interactive `az login --tenant <external-tenant>.onmicrosoft.com --allow-no-subscriptions`. We script the steps; the user pastes a 5-command block once.
- One-time documentation churn: `docs/b2c/*` becomes `docs/identity/*`; setup runbook is rewritten end-to-end for the new portal flow.
- Tenant provisioning takes up to 30 minutes (vs ~2 min for B2C). One-time wait.
- Microsoft documentation around External ID is younger; some community answers are still B2C-flavored. Mitigated by linking to current Microsoft docs everywhere we reference setup.
- `Microsoft.Identity.Web` package is the more idiomatic choice over vanilla `JwtBearer`. We're staying with vanilla `JwtBearer` for Phase 1 (smaller dep surface, easier to reason about). Migration to Microsoft.Identity.Web is a one-line dependency swap if we want richer features (multi-tenant, MSAL token cache) later.

## Pros and Cons of the Options

### A) Force B2C through workarounds

- Good, because the proposal already documents B2C end-to-end.
- Bad, because we'd be building on a product Microsoft has explicitly stopped onboarding new customers to.
- Bad, because the migration to External ID is inevitable by 2030.

### B) Entra External ID (chosen)

- Good, because it is Microsoft's actively-developed CIAM product.
- Good, because our use case (user flows, not custom policies) is fully covered.
- Good, because A1 changes are confined to URL strings and config-key names — no logic changes.
- Bad, because admin operations require interactive login in the External tenant (mitigated: rare, scripted).

### C) Plain Azure AD with self-service sign-up

- Bad, because workforce tenants are not designed for anonymous consumer sign-up at scale.

### D) Third-party CIAM (Auth0/Clerk/Okta)

- Bad, because adds a vendor + bill + secret to rotate. Proposal §14 already rejected this implicitly by choosing B2C.

## Migration impact

Concrete changes triggered by this ADR (applied in this PR):

| File | Change |
|---|---|
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs` | Authority URL pattern changes from B2C-specific `{instance}/{domain}/{policy}/v2.0` to External-ID `https://{tenant}.ciamlogin.com/{tenantId}/v2.0`. Issuer validation rule simplified (no policy in path). |
| `src/VrBook.Api/appsettings.json` | `AzureAdB2C` section → `EntraExternalId` section (Instance, TenantId, Domain, ClientId). No SignUpSignInPolicyId — Entra External ID doesn't use that concept. |
| `web/.env.local.example`, proposal §23.3 env-var list | `NEXT_PUBLIC_B2C_AUTHORITY` → `NEXT_PUBLIC_ENTRA_AUTHORITY`; `NEXT_PUBLIC_B2C_CLIENT_ID` → `NEXT_PUBLIC_ENTRA_CLIENT_ID`. |
| `docs/b2c/` directory | Renamed to `docs/identity/`. README + setup.md rewritten from scratch against current Microsoft Entra docs. |
| `infra/scripts/20-b2c-apps.ps1` | Renamed to `20-entra-apps.ps1`. Three `az ad app create` commands stay structurally identical; only the target tenant changes. Extension-attribute creation remains valid (the External tenant uses the same Graph API). |
| `infra/scripts/grant-self-admin.ps1` | Tenant param + claim names updated. |
| `infra/scripts/CLAUDE-ACCESS.md` + `README.md` | Reference rewrites for runbook commands. |
| `contracts/CHANGELOG.md` | Documented as a contract surface change (env var renames). |
| `docs/adr/README.md` | New entry for ADR-0012; ADR-0009 unaffected. The proposal's §14 stays as written (historical), but `docs/identity/README.md` notes the supersession. |

What does **not** change:
- `User` aggregate, `HttpCurrentUser`, `UserProvisioningMiddleware`, `AuditLogBehavior`, `IdentityDbContext`, every controller. All read JWT claims via standard names that Entra External ID emits identically.
- `DevAuth` (synthetic auth handler). Provider-agnostic.
- The §13 email notification catalog. Unrelated.
- ADR-0011 (ACS email). Unrelated.

## Links

- [Proposal §14 — Identity & Security Architecture (now superseded by this ADR)](../../BookingApp_Proposal.md)
- [Microsoft FAQ — Azure AD B2C end of sale](https://learn.microsoft.com/en-us/azure/active-directory-b2c/faq)
- [Microsoft Entra External ID — External Tenant Quickstart](https://learn.microsoft.com/en-us/entra/external-id/customers/quickstart-tenant-setup)
- [Microsoft Entra External ID — App registration](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app)
- [docs/identity/setup.md](../identity/setup.md) — the operational runbook for this environment

---

## Post-cutover correction (2026-06-26)

The role-claim flow originally described under "Claim names are stable" (custom `extension_*` attributes on the API app registration) **was abandoned during OPS.M.0 close-out**. Entra External ID's user-flow-issued access tokens do not reliably emit app-level `extension_*` claims even when `optionalClaims.accessToken` is configured.

Roles now ship as **Entra App Roles** on the API app registration. Tokens carry a native `roles` claim that ASP.NET JwtBearer maps to `ClaimTypes.Role` automatically. See [`docs/identity/roles-architecture.md`](../identity/roles-architecture.md) for the design and [`docs/identity/runbooks/entra-external-id-setup.md`](../identity/runbooks/entra-external-id-setup.md) §7 for the operational procedure.

This correction does **not** change the provider decision in this ADR — Entra External ID remains the identity provider. Only the role-bearing claim channel changed.
