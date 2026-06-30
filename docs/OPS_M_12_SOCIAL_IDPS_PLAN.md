# Slice OPS.M.12 — Entra External ID social IdPs (Google + Microsoft)

**Status:** QUEUED. To run after Slice OPS.M.10.2 F11.8 closes.
**Owner slice:** `Slice OPS.M.12` (new).
**Audit cross-reference:** none — this is product UX gap surfaced during F11.7 staging walk.

---

## §0 Problem

Entra External ID staging tenant (`vrbook.ciamlogin.com`) currently exposes
ONE sign-in path: local accounts (email + password / email + OTP).
The product surface (`PriceQuoteWidget.tsx` post-F11.7.1) promises a
"Sign in to book" action; clicking it lands on Entra and offers only the
local-account form. The marketplace UX expectation is social sign-in
(Google + Microsoft + optionally Apple) — same shape as every consumer
booking platform.

---

## §1 Scope

In:
- **Google** as an Identity Provider on the Entra External ID tenant.
- **Microsoft personal accounts** (consumer Microsoft account) as an IdP.
- Update the sign-up/sign-in user flow to surface both buttons above the
  local-account email field.
- Verify post-sign-in claims map correctly into `User.B2CObjectId` /
  email / displayName via the M.2 `UserProvisioningMiddleware` — the
  middleware reads the `oid` claim and is identity-provider-agnostic;
  no API code change should be required.

Out of scope:
- Apple sign-in (requires Apple Developer Program enrollment + a
  separate dance; queue as M.12.1 if needed).
- LinkedIn / Twitter / Facebook (low-value for vacation rental
  marketplace).
- Production tenant — same shape but a separate slice (different
  Azure tenant + different redirect URIs + a real privacy review).

---

## §2 Sub-step sequence

```
F12.1  Register Google OAuth client (Google Cloud Console):
       - Create OAuth 2.0 Client ID, app type "Web application".
       - Authorized redirect URIs:
         https://vrbook.ciamlogin.com/<tenant-id>/oauth2/authresp
       - Output: client_id + client_secret -> save to Azure Key Vault
         (kv-vrbook-staging) as secrets `entra-google-client-id` and
         `entra-google-client-secret`.

F12.2  Add Google IdP to Entra External ID:
       - Portal: External Identities -> All identity providers ->
         Google (or via Graph API:
         POST /identity/identityProviders).
       - Bind client_id / client_secret from KV.

F12.3  Repeat F12.1-F12.2 for Microsoft personal accounts:
       - Register Microsoft app at https://entra.microsoft.com/ ->
         App registrations (in YOUR personal-MS tenant or "Personal
         Microsoft accounts" scope).
       - Add to Entra External ID identity providers.

F12.4  Edit the sign-up/sign-in user flow to include both IdPs at the
       top of the page (before the local-account fields).

F12.5  Smoke walk (~5 min):
       1. /properties/beach-villa anonymous tab.
       2. Click `Sign in to book`.
       3. Confirm Entra page now shows "Continue with Google" +
          "Continue with Microsoft" buttons.
       4. Sign in via Google account that has NOT been seen by VrBook
          before -> verify the M.2 UserProvisioningMiddleware
          provisions a new `identity.users` row + returns to the
          property page.
       5. Click `Book this stay` -> 201 Tentative.
       6. Sign out, repeat with Microsoft account.

F12.6  Document the result + update the operator runbook with the
       new IdP options.

F12.7  Production-tenant equivalent — defer to OPS.M.12-prod.
```

Each sub-step is an Azure portal action (NOT a code commit). The whole
slice may produce zero git commits — closed via this doc's §11
close-out section.

---

## §3 Risks + open questions

| Risk | Mitigation |
|---|---|
| Google rejects the redirect URI if the Entra tenant ID format changes | Use the canonical `oauth2/authresp` endpoint format; document the exact URI in §11. |
| Microsoft "personal accounts" + Microsoft "work accounts" are different IdPs in Entra External ID | Start with personal accounts; work accounts require a separate IdP registration if needed. |
| Existing local-account users continue to work | Verified by leaving the user flow's local-account email field intact below the IdP buttons. |
| New IdP users' B2CObjectId claim shape | The `oid` claim is uniform across IdPs in Entra External ID per Microsoft docs; M.2 middleware is already provider-agnostic. |

---

## §11 Close-out (populated at end of F12.6)

(To be filled in by the implementor at end of F12.5 — what IdPs were
added, what user-flow shape ships, which test accounts validated the
flow.)
