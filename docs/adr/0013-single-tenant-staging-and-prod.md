# 13. Single Entra External ID tenant for both staging and prod

- Status: Accepted
- Date: 2026-05-31
- Deciders: Solutions Architecture
- Tags: identity, entra, tenants, environment-isolation, supersession

## Context

[[0012-entra-external-id-over-b2c]] established Entra External ID (CIAM) as the
identity provider replacing the now-deprecated AD B2C. The original intent was
two separate External ID tenants — one for staging, one for prod — mirroring
typical environment isolation patterns and giving us a sacrificial place to
exercise user-flow + token-claim changes before they hit production users.

During bootstrap (2026-05-30 → 2026-05-31) we created the prod External tenant
(`vrbook.onmicrosoft.com`) first because the friendly subdomain we wanted
(`vrbook`) was global and we couldn't reserve it twice. We then attempted to
create a second tenant `vrbookstaging.onmicrosoft.com`. The Microsoft Entra
admin portal accepted the request and reported "tenant provisioning in
progress." After several hours of polling, the staging tenant never appeared
under our subscription, never sent a confirmation, and never produced an error
trail we could act on. Re-attempts produced "tenant name already taken" — i.e.
the failed-but-still-reserved tenant blocked the name.

We are out of patience to debug Microsoft's tenant provisioning pipeline as a
prerequisite to landing the first deploy.

## Decision

**Use a single Entra External ID tenant (`vrbook.onmicrosoft.com`) for both
staging and production environments.** Separate identity contexts inside that
single tenant via:

- **Distinct app registrations per environment.** `api-staging`, `api-prod`,
  `web-staging`, `web-prod`. Each environment's API and web app trust only
  their own audience/clientId. Token issued for `api-staging` cannot be
  accepted by `api-prod`'s JWT validator.
- **Distinct user flows per environment** (later, when sign-up policies start
  diverging). Today both environments share the default
  `SignUpSignIn_v1` flow because nothing prod-specific has been customized yet.
- **Distinct app secrets / federated credentials per environment.** The GitHub
  OIDC federated credential for `cd-staging` only grants tokens for the
  staging app registrations; `cd-prod` will get its own credential.
- **One global support contact / brand / privacy URL.** Acceptable because
  both environments serve the same brand to the same legal entity. Branding
  divergence (different prod-vs-staging look) is not a requirement.

## Consequences

### Positive

- **Unblocks staging deploy today.** No further Microsoft-side provisioning
  dependency.
- **Lower cost.** External ID is metered per MAU; one tenant pools the small
  staging-test population with prod.
- **Simpler local dev.** Engineers point at one `entraInstance` URL; only the
  app registration switches.
- **One admin console to manage user flows, branding, identity providers.**

### Negative

- **Cross-environment blast radius for tenant-wide settings.** Changing the
  tenant's default user flow or enabling a new identity provider affects both
  staging and prod simultaneously. Mitigation: all tenant-wide changes go
  through a Tier-2 change review and are tested against staging app
  registrations first.
- **Cannot run destructive tenant-level test scenarios** (rotating tenant
  signing keys, hard-disabling all sign-ups). Mitigation: those scenarios are
  covered by a separate **dev sandbox tenant** that a future engineer can
  spin up (~minutes once Microsoft's tenant provisioning is working again).
- **Same external email domain for verification emails.** Both prod and
  staging users get verification emails from `vrbook.onmicrosoft.com`. Once
  we wire ACS for transactional email ([[0011-azure-communication-services-email]]),
  we'll route verification through ACS with a `staging.vrbook.com` from-address
  for staging.
- **Audit logs are intermingled.** Tenant-level sign-in logs include both
  staging and prod sign-ins. Filtering by `appId` in Log Analytics gives a
  clean per-environment view; the analytics query has been added to the
  staging dashboard.

## Alternatives Considered

### A. Two separate External ID tenants (original plan)

Rejected because Microsoft's tenant provisioning silently failed and we lack
a fast, observable path to fix it.

### B. Use the workforce Entra tenant for staging

Rejected. Workforce tenants don't expose the External ID / CIAM user-flow
features. Customers cannot self-sign-up against a workforce tenant without
licensed admins inviting them, which doesn't fit the VRBook UX.

### C. Single tenant for both, but no app-registration separation

Rejected. Without distinct app registrations per environment, a token minted
for staging would be valid against prod, and vice versa. That collapses the
security boundary we need.

## Revisit if

- Microsoft's tenant provisioning becomes reliable enough that we can stand up
  a second tenant within the SLO window for an emergency cutover.
- Staging starts needing tenant-level customization (custom domain on user
  flows, divergent branding) that meaningfully conflicts with prod.
- Compliance review requires hard tenant-level isolation between prod user
  data and any non-prod environment.
