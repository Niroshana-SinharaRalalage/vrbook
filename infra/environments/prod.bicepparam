using '../main.bicep'

param env = 'prod'
param location = 'eastus2'

// pgAdminPassword is supplied by the CI/CD pipeline from a bootstrap KV via getSecret().
// Image tags default to a placeholder in main.bicep; pipeline overrides them per build.
