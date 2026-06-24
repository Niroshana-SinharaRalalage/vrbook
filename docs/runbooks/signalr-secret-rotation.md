# SignalR secret rotation

The Azure SignalR Service connection string lives in Key Vault under
`signalr-cs`. Since Slice 7, the Bicep template at
`infra/modules/signalr.bicep` writes this secret automatically on every
deploy from `sigr.listKeys().primaryConnectionString` — mirroring the
ACS pattern in `infra/modules/acs.bicep`.

## When to rotate

- A connection-string leak (e.g. key surfaced in a log or git history).
- The Phase-2 multi-tenant cutover when the SKU bumps from `Standard_S1`
  to a higher tier.
- 12-month routine rotation per the platform secret-rotation policy.

## How to rotate (zero downtime)

The SignalR Service holds two keys — `primary` and `secondary`. Rotate
primary while the API keeps reading from KV (which Bicep just rewrote to
the new value). The token TTL is 1 hour; existing connections continue
working until their next negotiate.

```pwsh
# 1. Confirm both keys work in the current state.
az signalr key list --name sr-vrbook-<env> --resource-group rg-vrbook-<env> --output table

# 2. Regenerate the primary key.
az signalr key renew --name sr-vrbook-<env> --resource-group rg-vrbook-<env> --key-type primary

# 3. Re-run azd up so the Bicep KV-write picks up the new primary.
azd up

# 4. Restart the API container so the lazy ServiceHubContext picks up the new value.
az containerapp revision restart --name ca-vrbook-api-<env> --resource-group rg-vrbook-<env>

# 5. Verify clients can negotiate against the new key.
curl -i https://ca-vrbook-api-<env>.../api/v1/realtime/negotiate \
  -H "Authorization: Bearer <dev-token>"
# Expect 200 with { url, accessToken, expiresAt } — accessToken changes within the hour.
```

## How to verify Bicep wrote the secret

```pwsh
az keyvault secret show --name signalr-cs --vault-name kv-vrbook-<env> --query value -o tsv
# Expect: Endpoint=https://sr-vrbook-<env>.service.signalr.net;AccessKey=...;...
# If you see "pending-bicep-deploy", the Slice 7 KV-write didn't fire — re-run azd up.
```

## Failure paths

| Symptom | Likely cause | Fix |
|---|---|---|
| `/api/v1/realtime/negotiate` returns 503 | KV value still `pending-bicep-deploy` | Re-run `azd up`; restart Container App. |
| Negotiate returns 401 | Caller is unauthenticated | Add `Authorization` header. |
| Negotiate succeeds but client can't connect | Stale token; KV was rotated but the container caches the old hub context | Restart the Container App revision. |
| Pushes don't reach the browser | Wrong `UserId` in the token | Confirm the API's `currentUser.UserId` matches the `sub` claim shape (Guid as string). |

## See also

- `docs/runbooks/realtime-degradation.md` — what to check when the
  dashboard pill stops updating.
- `docs/SLICE7_PLAN.md` §2.6 + §2.7 for the Negotiate + Bicep design.
