# 11. Azure Communication Services Email (supersedes ADR-0004 SendGrid)

- Status: Accepted
- Date: 2026-05-26
- Deciders: Solutions Architecture
- Tags: notifications, email, deliverability, azure, supersession

## Context and Problem Statement

[ADR-0004](./0004-sendgrid-transactional-email.md) chose SendGrid as the transactional ESP for the §13 catalog (18 templates, ≥98% deliverability KPI). The decision was based on SendGrid's mature template tooling, deliverability story, and the `INotificationSender` abstraction that allows a future swap.

Mid-bootstrap (2026-05-26), we learned the client already runs another production product, **LankaConnect**, on Azure Communication Services (ACS) Email — specifically `AzureEmailService.cs` wrapping `Azure.Communication.Email`. ACS is a fully-managed Azure-native email service with similar deliverability characteristics (it routes through Microsoft's commercial mail infrastructure, the same backbone Outlook/Office 365 uses), explicit support for custom domains + SPF/DKIM/DMARC, and a managed-identity-friendly auth story.

The question reopened: **stick with SendGrid (per ADR-0004) or switch to ACS to match LankaConnect?**

## Decision Drivers

- **Operational consistency.** The team has existing run-time experience with ACS via LankaConnect — bounce handling, attachment patterns, retry behaviour, the rate-limit shape (429s). Re-using that knowledge for VrBook removes a large class of "first-time-with-this-vendor" surprises in production.
- **Single-vendor billing.** ACS bills inside the same Azure subscription. No new vendor, no separate spend approval, no separate audit chain.
- **Identity model.** ACS supports managed identity authentication; SendGrid is API-key only. MI removes one credential to rotate.
- **Cost.** Comparable at our forecast volume (≤ 50k emails/month for the first year). ACS is ~$0.25/1k emails, SendGrid Essentials is $19.95/mo for up to 50k. Roughly even.
- **The proposal's `INotificationSender` abstraction.** The abstraction was designed for exactly this swap — providers behind an interface, A9 picks the implementation, none of the calling modules know.
- **Template story.** SendGrid has a hosted Dynamic Templates editor. ACS does not — we render Mustache via Stubble (proposal §13, "Templating") in our own code. LankaConnect already does inline Handlebars-style rendering successfully. Hosted editing is "nice to have" not "must have".

## Considered Options

- **A) Keep SendGrid (ADR-0004).** Original plan.
- **B) Switch to Azure Communication Services Email.** Match LankaConnect; one less vendor.
- **C) Both behind `INotificationSender`, pick at runtime via config.** Pluggable; over-engineered for Phase 1.

## Decision Outcome

Chosen: **Option B — Azure Communication Services Email**.

### Positive Consequences

- One vendor for both products. Same code patterns can be ported from LankaConnect's `AzureEmailService.cs`.
- Managed identity for auth — no SendGrid API key to provision, rotate, or worry about leaking.
- Single Azure bill for everything VrBook runs.
- Eliminates ADR-0004 R6 risk ("SendGrid shared IP cold start, warm domain reputation by sending verification emails to a seed list pre-launch") — ACS uses Microsoft's pre-warmed sender infrastructure for the `*.azurecomm.net` sender domain by default, and offers verified custom-domain sending with SPF/DKIM/DMARC wiring documented in 1-click form.
- Removes the `--from-env-file` SendGrid prompt from the bootstrap secret runbook.

### Negative Consequences / Trade-offs

- No hosted template editor. We render templates in code (Stubble/Mustache) which means template changes require a deploy (or, in Phase 2, a hot-reload from a `templates/` Blob container — same pattern LankaConnect uses).
- ACS is less common in the broader ecosystem; community examples + third-party tooling lean SendGrid. Stack Overflow answers for SendGrid outnumber ACS Email ~50:1.
- The §21 R6 risk ("deliverability poor at launch (cold IP)") is mitigated but not zero — custom-domain ACS still needs domain-level reputation building. Mitigation: use ACS's `donotreply@<random>.azurecomm.net` sender for initial verification emails, swap to `bookings@vrbook.example.com` after domain DKIM is verified and reputation seeded.
- One module's change blast radius: the Notifications module (A9) and ADR-0004 are touched. No downstream code touched (Booking, Identity, etc. all publish `NotificationRequested` events; the worker translates to ACS instead of SendGrid).

## Pros and Cons of the Options

### A) Keep SendGrid (ADR-0004)

- Good, because the original decision had a thorough vendor-comparison rationale (see ADR-0004).
- Good, because hosted template editor would let non-engineers tweak copy without a deploy.
- Bad, because the client maintains two email providers — SendGrid for VrBook, ACS for LankaConnect. Two sets of credentials, two SLAs, two dashboards.
- Bad, because SendGrid API-key only auth means another credential to rotate every 90 days.

### B) Azure Communication Services Email (chosen)

- Good, because matches LankaConnect's existing pattern — `AzureEmailService.cs` can be ported nearly verbatim.
- Good, because managed identity auth eliminates a secret class.
- Good, because billing/governance lives inside Azure already.
- Bad, because no hosted template editor (mitigated by Mustache/Stubble per §13).
- Bad, because thinner ecosystem (mitigated by LankaConnect prior art).

### C) Both providers behind `INotificationSender`

- Good, because flexibility — could A/B providers, fall back automatically.
- Bad, because YAGNI for Phase 1. Phase 2 can revisit when we have actual deliverability data.

## Migration impact

Concrete changes triggered by this ADR:

1. [`infra/scripts/CLAUDE-ACCESS.md`](../../infra/scripts/CLAUDE-ACCESS.md) + [`10-store-secrets.ps1`](../../infra/scripts/10-store-secrets.ps1) — remove SendGrid API key prompt; add ACS connection string seeding (or `acs-resource-id` if we use MI).
2. [`docs/b2c/README.md`](../../docs/b2c/README.md) and the env-var inventory in proposal §23.3 — replace `SendGrid__ApiKey` / `SendGrid__FromAddress` with `Acs__ConnectionString` (or MI-style `Acs__EndpointUri` + `Acs__ManagedIdentityClientId`) and `Acs__SenderAddress`.
3. [`src/Modules/VrBook.Modules.Notifications`](../../src/Modules/VrBook.Modules.Notifications) — A9 implements `INotificationSender` against `Azure.Communication.Email` instead of `SendGrid`. The `Directory.Packages.props` entry for `SendGrid` becomes `Azure.Communication.Email` (~12.x).
4. [`src/Workers/VrBook.Workers.Notifications`](../../src/Workers/VrBook.Workers.Notifications) — package reference updated, Mustache renderer unchanged, dispatch flow unchanged.
5. [Notification template catalog (§13)](../../BookingApp_Proposal.md) — unchanged. The 18 templates and their trigger semantics are provider-agnostic.
6. ACS resource provisioning — added to `infra/main.bicep` (small: `Microsoft.Communication/communicationServices` + `Microsoft.Communication/emailServices/domains`). Domain verification happens out-of-band (DNS records).

No contract changes (no DTO or event shape touched). The `INotificationSender` interface in `VrBook.Contracts.Interfaces.INotificationSender` already exposes the right shape (`NotificationDispatchRequest` → `Task`).

## Operational notes for A9

- LankaConnect uses `Azure.Communication.Email` ~1.0.x with `Azure.Identity` for MI. Same pattern works here.
- LankaConnect implements dual-provider (Azure + SMTP fallback). For VrBook Phase 1, ACS-only is fine — proposal §21 R6 acceptable risk.
- LankaConnect has a daily cleanup of failed/queued emails. Borrow the schedule.
- LankaConnect found 429 rate-limit backoff at 2s/4s/8s (max 3 attempts). Same numbers fit our volume.

## Links

- [ADR-0004 (superseded)](./0004-sendgrid-transactional-email.md)
- [Proposal §13 — Email Notification Catalog](../../BookingApp_Proposal.md)
- [Proposal §10 — Messaging System Design (email fallback flow)](../../BookingApp_Proposal.md)
- [LankaConnect AzureEmailService analysis (chat 2026-05-25 — Subagent report)](../../README.md)
- [Microsoft docs — Azure Communication Services for Email](https://learn.microsoft.com/azure/communication-services/concepts/email/email-overview)
