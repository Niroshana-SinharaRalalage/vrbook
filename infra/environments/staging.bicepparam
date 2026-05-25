using '../main.bicep'

param env = 'staging'
param location = 'eastus2'

// pgAdminPassword is supplied by the CI/CD pipeline at deploy time.
// Image tags default to a placeholder in main.bicep; pipeline overrides them per build.
