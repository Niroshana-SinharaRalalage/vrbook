# Realtime degradation triage

When the dashboard pill (`/admin`) stops updating live and the "Live"
indicator goes muted, work through this list top-to-bottom.

## 1. Is the negotiate endpoint up?

```bash
curl -i -H "Authorization: Bearer <token>" \
  "https://ca-vrbook-api-<env>.../api/v1/realtime/negotiate"
```

| Status | Meaning | Fix |
|---|---|---|
| `200` | Auth + SignalR both fine; problem is on the client | Browser DevTools → Console / Network for WebSocket errors |
| `401` | Auth token rejected | Re-login; check DevAuth persona cookie |
| `503` `realtime.unavailable` | Server can't reach SignalR Service | Step 2 |
| `5xx` other | API itself is unhealthy | Check `/healthz` + container logs |

## 2. Is `signalr-cs` populated in Key Vault?

```bash
az keyvault secret show --vault-name kv-vrbook-<env> \
  --name signalr-cs --query value -o tsv
```

- Real value (`Endpoint=https://sr-vrbook-<env>...`) → SignalR side; go to step 3.
- `pending-bicep-deploy` → the Slice 7 Bicep KV-write didn't run. Re-run
  `azd up`; verify [`docs/runbooks/signalr-secret-rotation.md`](signalr-secret-rotation.md).
- Empty / 404 → secret was deleted; restore from soft-delete or re-run
  `azd up` to recreate.

## 3. Is the SignalR Service healthy?

```bash
az signalr show --name sr-vrbook-<env> --resource-group rg-vrbook-<env> \
  --query "{state:provisioningState,host:hostName,units:sku.capacity}"
```

- `Succeeded` + a hostname → the service is up. Go to step 4.
- `Failed` / `Deleting` → re-deploy via `azd up`.
- Service unit count exceeded → bump SKU capacity in `infra/modules/signalr.bicep`.

## 4. Did the Container App pick up the connection string?

The API caches the `ServiceHubContext` for the process lifetime; if the
secret was rotated since the container started, it's stuck on the old
value.

```bash
az containerapp revision restart \
  --name ca-vrbook-api-<env> --resource-group rg-vrbook-<env>
```

Then re-test step 1.

## 5. Is the client reaching SignalR over WebSocket?

Open browser DevTools → Network → WS tab. After the page loads you should see:

- A `negotiate` POST → 200 with `{ url, accessToken }`.
- A WebSocket connection to `sr-vrbook-<env>.service.signalr.net/client/...`.

If the WS handshake fails:

- Corporate proxy blocking WSS — try a different network.
- Browser extension blocking the connection — try incognito.
- SignalR Service CORS misconfigured — verify `cors.allowedOrigins` in
  `infra/modules/signalr.bicep` still includes `*` (Phase 1) or your
  origin (production).

## 6. What about the pill?

The dashboard has an always-on visibility-refetch fallback: even when
SignalR is fully down, focusing the `/admin` tab refetches the
Tentative count once per focus. So the pill is **never** stale by
more than a tab-focus event.

If the pill is stuck even after re-focusing the tab:

- The `/api/v1/admin/bookings` query itself is failing — check the
  Network tab; expect 200 with a JSON array.
- Auth cookie expired — log in again.

## 7. Worst case: kill realtime entirely

To disable SignalR client-side without a redeploy (e.g. quick triage),
in the browser console:

```js
// Forces the hook to perceive itself as Disconnected; visibility-refetch
// fallback continues to work. Refresh after to fully reset.
localStorage.setItem('vrbook.realtime.kill', '1');
```

(This kill switch is checked in `connection.ts`; if you don't see the
check, it's a Phase-2 add — for now, refresh the page to reset.)

## See also

- `docs/runbooks/signalr-secret-rotation.md` — KV secret lifecycle.
- `docs/SLICE7_PLAN.md` §2.11 — connection lifecycle design.
