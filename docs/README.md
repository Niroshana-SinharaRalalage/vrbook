# /docs

Living documentation for the VrBook platform. The authoritative spec is still
[`../BookingApp_Proposal.md`](../BookingApp_Proposal.md) — this directory tracks the
decisions, runbooks, and security artifacts that the proposal references.

| Directory | What lives here |
|---|---|
| [`adr/`](./adr/) | Architecture Decision Records (MADR format). One file per decision; index in [`adr/README.md`](./adr/README.md). |
| [`runbooks/`](./runbooks/) | On-call playbooks for Sev2 + Sev3 alerts (proposal §17.4). |
| [`security/`](./security/) | Threat model, OWASP compliance notes, security review checklist. |
| [`b2c/`](./b2c/) | AD B2C tenant configuration, user-flow exports, custom policy notes. |

Add new documents next to their peers. Cross-link liberally — the proposal links into
here; runbooks should link to relevant ADRs and Bicep modules.
