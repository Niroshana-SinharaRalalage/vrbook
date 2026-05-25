# 4. SendGrid for Transactional Email

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: notifications, email, third-party, deliverability

## Context and Problem Statement

The §13 email catalog defines 18 transactional templates (booking confirmation, owner-action-required, refund issued, review request, password reset, message-fallback, etc.). The §2.2 KPI table sets email deliverability at "≥ 98% (delivered ÷ sent)" as a Month-3 target — a substantive number that constrains the choice of provider, because anything that is not a properly warmed transactional ESP with SPF/DKIM/DMARC tooling will not meet it.

The §1 Executive Summary's recommendation table lists SendGrid as the email choice with the caveat "with `INotificationSender` abstraction." Section 10.2 specifies the fallback flow: "If the recipient has no active connection for > 10 minutes (tracked via heartbeat + last-seen-at), the Notification Worker sends an email fallback." Section 21 R6 flags deliverability as a launch risk: "SendGrid shared IP at first; warm domain reputation by sending verification emails to a seed list pre-launch; SPF/DKIM/DMARC in W1."

The candidate set is the four mainstream transactional ESPs: SendGrid, AWS SES, Postmark, and Mailgun. All four can technically deliver mail. The decision is about the *operational shape* — template tooling, deliverability story, developer experience, and lock-in posture.

## Decision Drivers

- 98% deliverability target (§2.2) from Month-3 — provider must be a true transactional ESP, not a generic SMTP relay.
- 18 templates with i18n hooks (Phase 1 is English-only but §22.5 mentions Spanish then French) — a usable template editor matters.
- Template authoring is owned by product/marketing, not engineering — the editor must be non-engineer-usable.
- We are not on AWS — the rest of the platform runs on Azure (§15). An AWS-native dependency adds an out-of-region SDK and credential management surface.
- SPF/DKIM/DMARC setup must happen in W1 (§21 R6) — provider DNS tooling matters.
- Cold-start IP reputation is a known risk (§21 R6) — shared IP with warm-up is needed Phase 1; dedicated IP available later.
- Provider lock-in must be reversible — wrap in `INotificationSender` (§1 recommendation table).

## Considered Options

- **SendGrid (Twilio SendGrid)** — Mature transactional ESP; visual template editor; strong shared-IP warmup; broad SDK ecosystem.
- **AWS SES** — Cheapest per-mail; raw API; minimal template tooling; AWS-native.
- **Postmark (ActiveCampaign)** — Best-in-class transactional deliverability; strict transactional-only ToS; smaller template editor.
- **Mailgun (Sinch)** — Strong API; mixed marketing/transactional positioning; developer-leaning.

## Decision Outcome

Chosen option: **"SendGrid"**, because of the combination of (a) mature template editor that lets non-engineers author the 18 templates without a build cycle, (b) Azure-region availability (Twilio SendGrid runs out of multiple regions including the US-East datacenters we deploy to), (c) shared-IP warm-up that lets us hit the 98% target without buying and warming a dedicated IP for launch, and (d) the §1 architectural decision to abstract behind `INotificationSender` makes the choice reversible if deliverability or pricing surprises us.

### Positive Consequences

- The 18 templates from §13 live in SendGrid Dynamic Templates with Handlebars variables — product/marketing edits content, engineering ships data shapes.
- Shared-IP warm-up handled by SendGrid means we ship Phase 1 without buying and warming a dedicated IP.
- `INotificationSender` abstraction in the Notifications module means the rest of the codebase calls `await _sender.SendAsync(NotificationKey.BookingConfirmed, recipient, data)` and never mentions SendGrid by name.
- SendGrid Event Webhook gives us `delivered`, `bounce`, `open`, `click`, `spam_report`, `unsubscribe` events into the platform — feeds the `email.sent` and `email.delivered` metrics in §17.2.
- API key is stored in Key Vault (§14.4) and resolved via Container Apps secret reference at runtime — same pattern as Stripe.
- SendGrid Sender Authentication walks us through SPF, DKIM, and DMARC DNS records in W1 — checks the §21 R6 mitigation off cleanly.

### Negative Consequences / Trade-offs

- Per-email cost is higher than SES — at Phase 1 volume (estimated < 5,000 emails/month) the absolute cost is negligible.
- Twilio's pricing tier changes have historically been disruptive — abstraction behind `INotificationSender` is the insurance policy.
- Sandbox mode behaviour differs subtly from production — integration tests use a stub `INotificationSender` rather than calling the SendGrid sandbox.
- Template-editor changes are out-of-band from git — risk of an undocumented template edit causing a production rendering bug. Mitigation: template JSON exported and committed to `/contracts/email-templates/` on every change as part of the release checklist.

## Pros and Cons of the Options

### SendGrid

- Good, because the Dynamic Templates editor is genuinely non-engineer-usable — content owners can edit copy and preview with sample data without a deploy.
- Good, because shared-IP warm-up is automatic; we don't have to engineer a manual warm-up plan in Phase 1.
- Good, because SDK (`SendGrid` NuGet) is well-maintained and supports template-id + dynamic data pattern that maps cleanly to our `NotificationKey` enum.
- Good, because Event Webhook covers the §17.2 metrics surface without extra work.
- Good, because Sender Authentication tooling makes the §21 R6 DNS-record setup a guided checklist.
- Bad, because per-email cost is higher than SES — irrelevant at Phase 1 volume, watch in Phase 2.
- Bad, because template lives outside git — needs a process to capture template JSON in repo on change.

### AWS SES

- Good, because cheapest per-mail by a wide margin — Phase 2 high-volume escape valve.
- Good, because we already have AWS-shaped tooling in the .NET ecosystem if we ever needed it.
- Bad, because we run on Azure — adding AWS credentials and an AWS SDK for one feature is meaningful operational tail.
- Bad, because SES has *no usable template editor* — templates are JSON blobs uploaded via API, authored by engineers, edited via git PRs. Wrong tool for content owned by product/marketing.
- Bad, because SES requires a *production-access request* (out-of-sandbox) that historically takes days and asks for sending-volume justifications — friction in W1.
- Bad, because deliverability on SES is a function of *our* IP warm-up, not the provider's — more work for the team at launch.

### Postmark

- Good, because deliverability is arguably best-in-class for pure transactional mail — they aggressively police senders.
- Good, because the template editor is competent.
- Bad, because Postmark's ToS forbids marketing-adjacent mail — review-request emails and loyalty-tier-upgrade emails could be borderline. The risk of an account suspension over a templating judgement call is non-trivial.
- Bad, because the template editor and SDK ecosystem are smaller than SendGrid's — fewer engineers know the API.
- Bad, because no clear price advantage at our volume.

### Mailgun

- Good, because strong API and developer ergonomics.
- Good, because price-competitive.
- Bad, because the template editor is weaker than SendGrid's — product/marketing edits would route back through engineering.
- Bad, because Sinch's pivot toward bundled communications has made the roadmap less predictable than SendGrid's.
- Bad, because deliverability reputation has been more volatile in industry reports than SendGrid's or Postmark's.

## The `INotificationSender` Abstraction

The interface lives in `VrBook.Module.Notifications.Contracts` and is the *only* notification surface the rest of the codebase sees:

```csharp
public interface INotificationSender
{
    Task SendAsync(
        NotificationKey key,
        EmailRecipient to,
        IReadOnlyDictionary<string, object> templateData,
        string correlationId,
        CancellationToken ct);
}
```

- `NotificationKey` is the enum that names the 18 templates from §13 (`BookingConfirmation`, `OwnerActionRequired`, `RefundIssued`, etc.).
- The default `SendGridNotificationSender` implementation maps `NotificationKey` → SendGrid `template_id` via a configured dictionary.
- A `StubNotificationSender` writes to the structured log in integration tests; assertions are made on the log entries.
- A future `SesNotificationSender` or `PostmarkNotificationSender` is one class. The contract does not change.
- Template data shape per key is enforced by typed record types per `NotificationKey` — the dispatcher serialises to the dictionary at the boundary. A template author who reads `{{guestFirstName}}` in their template has a guarantee from the type system that `guestFirstName` is in the data.

## Outbound Path (Worker, not Request)

The §3.2 container diagram puts email dispatch in the Notification Worker, not the API. The API publishes a `NotificationRequested` event to Service Bus; the Notification Worker consumes, hydrates the template data, and calls `INotificationSender.SendAsync`. Reasons:

- Send latency does not block the user request.
- SendGrid rate limits (and 5xx) are isolated to the worker; the API never sees them.
- Retries are KEDA/Service Bus dead-letter-queue concerns, not request-thread concerns.
- §10.2 step 4 — "If the recipient has no active connection for > 10 minutes, the Notification Worker sends an email fallback" — already lives in the worker; sharing the dispatch surface is the simplification.

The 18-template catalog from §13 plus DLQ-based retry semantics plus a 98% deliverability target plus per-template metric emission (`email.sent` with `templateKey` and `status` dimensions per §17.2) is the worker's full mandate.

## DNS Setup (W1)

SendGrid Sender Authentication produces, for each sending domain:

- CNAME records for DKIM (typically `s1._domainkey.example.com` and `s2._domainkey.example.com` pointing to SendGrid).
- A CNAME for the return-path / bounce domain (`em.example.com`).
- A TXT record for SPF (`v=spf1 include:sendgrid.net ~all`).
- A TXT record for DMARC at `_dmarc.example.com` (`v=DMARC1; p=quarantine; rua=mailto:dmarc-reports@example.com`).

§21 R6 schedules these DNS records for W1 with mailbox-provider warm-up traffic (seed list, internal-stakeholder test sends) through W4 to build domain reputation before guest-facing volume hits in W5/W6.

## Why Abstract at All

The `INotificationSender` abstraction (§1 explicit requirement) is sometimes critiqued as YAGNI — "you're never going to swap email providers." That critique misses three concrete uses for the abstraction that pay off in Phase 1, not Phase 2:

1. **Integration testing without network.** The integration test suite injects a `StubNotificationSender` that records sends to an in-memory buffer; tests assert on the recorded sends. No fakery against the SendGrid API; no shared sandbox state across test runs.
2. **Development without spam.** Local-dev environments inject a `ConsoleNotificationSender` that logs the would-be send and never calls SendGrid. New developers running the API don't accidentally email a guest.
3. **Per-environment template-id resolution.** The `SendGridNotificationSender` resolves `NotificationKey → SendGrid template_id` from configuration, so staging templates and prod templates can differ without code change.

The fourth use — actually swapping providers — is the insurance policy, not the rationale.

## Links

- [Proposal §1 Executive Summary (recommendation table)](../../BookingApp_Proposal.md)
- [Proposal §2.2 Primary KPIs (deliverability target)](../../BookingApp_Proposal.md)
- [Proposal §10.2 Message Flow (email fallback)](../../BookingApp_Proposal.md)
- [Proposal §13 Email Notification Catalog](../../BookingApp_Proposal.md)
- [Proposal §17.2 Metrics (`email.sent`)](../../BookingApp_Proposal.md)
- [Proposal §21 R6 Email deliverability risk](../../BookingApp_Proposal.md)
- [SendGrid Sender Authentication docs](https://docs.sendgrid.com/ui/account-and-settings/how-to-set-up-domain-authentication)
