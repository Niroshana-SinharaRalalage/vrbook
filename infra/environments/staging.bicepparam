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
