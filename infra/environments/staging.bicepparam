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

// VRB-306 — staging alert recipient (owner on-call). Single address for now;
// per-alert owners come later when there's a team (owner decision 2026-07-15).
param alertEmail = 'niroshanaks@gmail.com'
