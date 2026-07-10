# Runbook — Stripe key rotation + go-live key swap (OPS.6)

Rotate the Stripe API secret, webhook signing secret, and publishable key, and
verify end-to-end. Same procedure for (a) the launch swap test→LIVE and (b) any
later rotation. **Hard go-live gate** — no real payments without LIVE keys.

## Key Vault secrets (already bound in `infra/main.bicep` via secretRef)

| KV secret | App setting | Notes |
|---|---|---|
| `stripe-secret` | `Stripe__SecretKey` | `sk_live_…` (use a **restricted** key with only the capabilities we need). |
| `stripe-webhook-secret` | `Stripe__WebhookSecret` | `whsec_…` from the LIVE webhook endpoint. |
| `stripe-publishable-key` | `Stripe__PublishableKey` | `pk_live_…`. |

Container Apps bind `secretRef` at **revision-provision time** (see [`reference_kv_secret_bind_before_deploy`](../../.claude/projects/c--Work-BookingApp/memory/reference_kv_secret_bind_before_deploy.md)) — updating a KV secret does NOT hot-reload a running revision. You must roll a new revision after the swap.

## Procedure

1. **Create the LIVE webhook endpoint** in the Stripe dashboard (LIVE mode) pointing at `https://<api-fqdn>/api/v1/payments/stripe/webhook` (confirm the exact route in `PaymentsController`). Subscribe to the events the handler consumes (`payment_intent.succeeded`, `charge.refunded`, `account.updated`, …). Copy its signing secret.

2. **Set the three secrets** (KV):
   ```bash
   KV=kv-vrbook-<env>
   az keyvault secret set --vault-name $KV --name stripe-secret          --value 'sk_live_…'
   az keyvault secret set --vault-name $KV --name stripe-webhook-secret  --value 'whsec_…'
   az keyvault secret set --vault-name $KV --name stripe-publishable-key --value 'pk_live_…'
   ```

3. **Roll a new API revision** so the secretRefs re-resolve. Prefer a fresh deploy (re-run `cd-staging-api` / prod deploy). If bumping manually, pass the current healthy image explicitly (see [`reference_containerapp_manual_revision_image_trap`](../../.claude/projects/c--Work-BookingApp/memory/reference_containerapp_manual_revision_image_trap.md)):
   ```bash
   az containerapp update -n ca-vrbook-api-<env> -g rg-vrbook-<env> \
     --image <acr>.azurecr.io/vrbook-api:<current-healthy-sha> \
     --revision-suffix rotate$(date +%s | tail -c 6)
   ```

4. **Verify one live-mode transaction end-to-end** (the go-live gate): make a real (small) booking through the UI with a live card → confirm the PaymentIntent in the Stripe dashboard → confirm the webhook delivered (200 in Stripe's webhook log) → confirm the booking flips to **Confirmed** and the email sends. Refund the test charge.

5. **Rotation (later):** repeat 2–3 with the new keys, then in Stripe **revoke the old restricted key** and delete the old webhook endpoint after confirming the new one delivers.

## Rollback

Re-set the KV secrets to the previous values + roll a revision. Keep the prior restricted key active until the new one is verified.

## Go-live gate

LIVE keys in KV + one verified live-mode transaction (payment → webhook → booking Confirmed → email). Old test keys removed from LIVE. No `sk_test_`/`pk_test_` in the prod environment.
