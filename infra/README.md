# VRBook Infrastructure (Bicep)

Infrastructure-as-Code for the Direct-Booking Vacation Rental Platform.
Derived from `BookingApp_Proposal.md` §3.3, §14, §15, §23.3, §23.5.

## Layout

```
infra/
  main.bicep                     # orchestrator (resource-group scope)
  modules/
    network.bicep                # VNet (snet-apps, snet-data), NSGs, private DNS zones
    key-vault.bicep              # Standard KV, soft-delete + purge, RBAC
    log-analytics.bicep          # PerGB2018, 90-day retention
    app-insights.bicep           # workspace-based, linked to LA
    container-apps-env.bicep     # CAE, workload profiles, VNet-integrated
    container-app.bicep          # generic Container App (API + workers)
    container-app-job.bicep      # Container App Job (Schedule | Event | Manual)
    postgres-flexible.bicep      # PG 16 Flexible Server, VNet-injected, env-sized
    redis.bicep                  # Standard C1, TLS 1.2, Private Endpoint
    service-bus.bicep            # Standard ns, topics: bookings/notifications/sync
    signalr.bicep                # Standard S1, Serverless mode
    storage.bicep                # Standard LRS hot, 3 containers, blob public off
    front-door.bicep             # Standard SKU + WAF DRS, static + listing caching
    acr.bicep                    # Premium (content trust per §14.3 A08)
    managed-identity.bicep       # User-assigned MI + KV/Storage/SB/ACR roles
    private-endpoint.bicep       # generic PE helper
  environments/
    dev.bicepparam
    staging.bicepparam
    prod.bicepparam
```

## Module purpose

| Module | Purpose |
|---|---|
| `network` | Single VNet `vnet-vrbook-{env}`; two subnets — `snet-apps` (CAE delegation) and `snet-data` (Private Endpoints). NSGs on both. Hosts private DNS zones for Postgres and Redis. |
| `key-vault` | Single KV per env, RBAC-authorised. Secrets are seeded by the CI/CD pipeline (see proposal §16). No literals in IaC. |
| `log-analytics` | Backing workspace for App Insights and Container Apps log stream. |
| `app-insights` | Workspace-based AI for API/worker telemetry. |
| `container-apps-env` | Workload-profile CAE, VNet-integrated, external ingress. Dedicated `d4` profile in prod. |
| `container-app` | Parameterised Container App. Used for API (HTTP scaling) and booking/notif workers (KEDA Service Bus scaling). |
| `container-app-job` | Parameterised Container App Job for the iCal sync worker (cron `*/5 * * * *`) and the manual DB migrator. |
| `postgres-flexible` | PG 16 Flexible Server, VNet-injected, env-sized SKU/HA/backup. Admin password sourced via `@secure()` param. |
| `redis` | Standard C1 1GB, non-clustered, TLS 1.2, Private Endpoint in snet-data. |
| `service-bus` | Standard namespace + topics (`bookings`, `notifications`, `sync`) with default subscriptions and DLQ enabled. |
| `signalr` | Serverless S1 — used for real-time messaging negotiations. |
| `storage` | Standard LRS hot tier; containers `property-images`, `message-attachments`, `feed-cache`. Public blob access off. |
| `front-door` | Standard AFD + WAF DRS 2.1 in Prevention mode. Caches `/_next/static/*` and listing pages. Deployed only in `prod` and `staging`. |
| `acr` | Premium registry (content trust). Admin user disabled; anonymous pull disabled. |
| `managed-identity` | One user-assigned MI shared by API + workers, with KV-Secrets-User, Blob-Data-Contributor, SB-Data-Owner, AcrPull role assignments. |
| `private-endpoint` | Generic helper for Private Endpoints (used by Redis explicitly; Postgres uses native VNet injection). |

## Naming convention (proposal §3.3)

- Dashed kinds: `{kind}-vrbook-{env}` (e.g. `vnet-vrbook-prod`, `kv-vrbook-prod`, `psql-vrbook-prod`, `ca-vrbook-api-prod`).
- Compacted (no dashes/lowercase only): `{kind}vrbook{env}` for ACR, Storage, WAF policies (e.g. `crvrbookprod`, `stvrbookprod`, `wafvrbookprod`).
- Resource Group: `rg-vrbook-{env}` (created out-of-band by the platform team).
- Every resource is tagged `{ env, app: 'vrbook', costCenter: 'product' }`.

## Environment matrix (proposal §15.2)

| | dev | staging | prod |
|---|---|---|---|
| Postgres SKU | `B_Standard_B2s` | `GP_Standard_D2ds_v5` | `GP_Standard_D4ds_v5` |
| Postgres HA | off | off | Zone-redundant |
| Postgres backup retention | 14d | 14d | 35d |
| Postgres storage | 128 GB | 128 GB | 256 GB |
| API min/max replicas | 0 / 5 | 1 / 5 | 1 / 10 |
| CAE workload profile | Consumption | Consumption | Consumption + `d4` |
| Front Door | off | on | on |

## Running `bicep what-if`

Pre-reqs: Azure CLI ≥ 2.60, Bicep CLI bundled with it. Authenticated to the target subscription.
The resource group must already exist (created by platform).

```bash
# dev
az deployment group what-if \
  --resource-group rg-vrbook-dev \
  --template-file infra/main.bicep \
  --parameters infra/environments/dev.bicepparam \
  --parameters pgAdminPassword="$BOOTSTRAP_PG_PWD"

# staging
az deployment group what-if \
  --resource-group rg-vrbook-staging \
  --template-file infra/main.bicep \
  --parameters infra/environments/staging.bicepparam \
  --parameters pgAdminPassword="$BOOTSTRAP_PG_PWD"

# prod
az deployment group what-if \
  --resource-group rg-vrbook-prod \
  --template-file infra/main.bicep \
  --parameters infra/environments/prod.bicepparam \
  --parameters pgAdminPassword="$BOOTSTRAP_PG_PWD"
```

Image tag overrides for a real deploy:

```bash
az deployment group create \
  --resource-group rg-vrbook-staging \
  --template-file infra/main.bicep \
  --parameters infra/environments/staging.bicepparam \
  --parameters pgAdminPassword="$BOOTSTRAP_PG_PWD" \
  --parameters apiImage="crvrbookstaging.azurecr.io/api:$GIT_SHA" \
               syncWorkerImage="crvrbookstaging.azurecr.io/sync:$GIT_SHA" \
               bookingWorkerImage="crvrbookstaging.azurecr.io/booking-worker:$GIT_SHA" \
               notificationWorkerImage="crvrbookstaging.azurecr.io/notif-worker:$GIT_SHA" \
               migratorImage="crvrbookstaging.azurecr.io/migrator:$GIT_SHA"
```

## Secrets

Every secret is a Key Vault reference. The pipeline seeds the following KV entries
before the first Container App deploys (names match `secrets[].keyVaultSecretName` in `main.bicep`):

```
postgres-cs              # Npgsql connection string
redis-cs                 # StackExchange.Redis connection string
signalr-cs               # SignalR connection string
stripe-secret            # Stripe secret key
stripe-webhook-secret    # Stripe webhook signing secret
sendgrid-key             # SendGrid API key
feed-pepper              # outbound feed token pepper
appi-cs                  # App Insights connection string
```

Container Apps reference each via `keyVaultUrl` + `identity` (the shared user-assigned MI),
which has `Key Vault Secrets User` on the vault.

## Notes / decisions beyond the proposal

- See the report returned with this commit for the full list of decisions made.
