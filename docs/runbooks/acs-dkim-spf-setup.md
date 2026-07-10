# Runbook — ACS custom sender domain + DKIM/SPF/DMARC (OPS.8)

Wire a **custom** sender domain for ACS Email with DKIM + SPF (+ DMARC) so
booking-confirmation email passes authentication and lands in the inbox. **Hard
go-live gate** — sending from the Azure-managed `*.azurecomm.net` domain (the
current default in `infra/modules/acs.bicep`) tanks deliverability from day one.

**⚠️ Long pole:** DNS propagation + ACS domain verification take hours (up to
~24h). **Start this first**, before the engineering slices.

## Resources (from `infra/modules/acs.bicep`)

- ACS namespace `acs-vrbook-<env>`, Email service `acs-email-vrbook-<env>`.
- Today: Azure-managed domain `donotreply@<random>.azurecomm.net`; `acs-sender-address` KV secret points at it.

## Procedure

1. **Add the custom domain** to the Email service (portal: ACS Email → Provision domains → Add custom domain, e.g. `mail.vrbook.com`) — or add a `Microsoft.Communication/emailServices/domains` resource with `domainManagement: CustomerManaged` to `acs.bicep`.

2. **Publish the DNS records** Azure shows for the domain, at your DNS provider:
   - **Domain verification** TXT.
   - **DKIM**: two CNAMEs (`selector1-…` / `selector2-…`) → Azure's DKIM hosts.
   - **SPF**: TXT `v=spf1 include:spf.protection.outlook.com -all` (use the exact include ACS specifies).
   - **DMARC** (recommended): TXT `_dmarc` `v=DMARC1; p=quarantine; rua=mailto:dmarc@vrbook.com`.

3. **Verify** in the portal until domain + DKIM + SPF show **Verified** (re-check after propagation).

4. **Connect the domain** to the ACS Email service and create/confirm the MailFrom sender (e.g. `no-reply@mail.vrbook.com`).

5. **Point the app at the custom sender:**
   ```bash
   az keyvault secret set --vault-name kv-vrbook-<env> \
     --name acs-sender-address --value 'no-reply@mail.vrbook.com'
   ```
   Then roll a new API/worker revision (secretRef binds at revision-provision time).

6. **Verify deliverability** (go-live gate): trigger a booking-confirmation email to an external mailbox (Gmail/Outlook) → open *Show original* → confirm **SPF=pass, DKIM=pass, DMARC=pass** and the alignment domain is the custom domain. Check it lands in Inbox, not Spam.

## Rollback

Repoint `acs-sender-address` to the Azure-managed `*.azurecomm.net` sender + roll a revision. (Deliverability from the managed domain is poor but functional.)

## Go-live gate

Custom domain + DKIM + SPF Verified; one external test email passes SPF+DKIM+DMARC alignment and lands in Inbox.
