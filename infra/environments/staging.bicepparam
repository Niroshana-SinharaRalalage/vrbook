using '../main.bicep'

param env = 'staging'
param location = 'eastus2'

// pgAdminPassword fetched from the bootstrap Key Vault at deploy time via getSecret().
// Requires the KV to have `enabledForTemplateDeployment: true` (set via:
//   az keyvault update --name kv-vrbook-staging --enabled-for-template-deployment true).
// This avoids the workflow having to fetch + pass the secret inline.
param pgAdminPassword = az.getSecret(
  'ebb8304a-6374-4db0-8de5-e8678afbb5b5',   // subscription id
  'rg-vrbook-staging',                       // resource group
  'kv-vrbook-staging',                       // vault name
  'postgres-admin-password'                  // secret name
)

// Image tags default to a placeholder in main.bicep; the CI/CD pipeline overrides
// them per build with the CI-built image SHAs against ACR.

// Slice 0.1 hold flow ships on Postgres (PostgresHoldStore) — see BookingModule.
// Microsoft.Cache/Redis is retiring in 2026 and Azure Managed Redis
// (Microsoft.Cache/redisEnterprise) is ~5x cost at the smallest SKU — not
// justified for Phase 1 staging. Re-enable when prod traffic warrants by
// setting deployRedis=true here AND Features__UseRedisHoldStore=true in API
// app settings.
param deployRedis = false

// OPS.INFRA.1 rev 2 — stand up psql-vrbook-staging-v2 (public-access + IP
// firewall) alongside the live private psql-vrbook-staging for the blue/green
// rebuild. Remove this line + the pgV2 module invocation in main.bicep after
// cutover (§3 step 12 of docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md).
param deployStagingPgV2 = true
