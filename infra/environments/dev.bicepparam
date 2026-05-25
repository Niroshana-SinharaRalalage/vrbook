using '../main.bicep'

param env = 'dev'
param location = 'eastus2'

// pgAdminPassword is supplied by the CI/CD pipeline at deploy time via
// `--parameters pgAdminPassword=$(BOOTSTRAP_PG_PWD)` or a getSecret() reference.
// Do NOT add it here — bicepparam files are committed.

// Image tags default to a placeholder in main.bicep; the pipeline overrides them per build.
