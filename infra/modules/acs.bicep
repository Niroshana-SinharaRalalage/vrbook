// acs.bicep — Azure Communication Services Email resource per ADR-0011 + REPLAN
// Slice 0.5. Provisions:
//   - Communication Services namespace (acs-vrbook-{env})
//   - Email Services parent (acs-email-vrbook-{env})
//   - Azure-managed default domain (no DNS records needed; sender domain ends
//     in .azurecomm.net). Custom domain wiring is OPS work; deferred to a
//     post-Slice-7 hardening pass per REPLAN.
//   - Writes the primary connection string into Key Vault as the existing
//     'acs-connection-string' secret reference (apiSecrets[] in main.bicep).
//     The KV write lives inside this module so the connection string never
//     leaves the deployment as a module output (linter rule
//     outputs-should-not-contain-secrets).

@description('Environment short name (dev | staging | prod).')
param env string

@description('Common tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

@description('Data location. United States is the lowest-friction default; revisit for EU pilot.')
param dataLocation string = 'United States'

@description('Existing Key Vault name to write the ACS connection string into.')
param keyVaultName string

resource cs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-vrbook-${env}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
    // Slice 4 verify fix: without this link, every send returns
    // 404 DomainNotLinked. The Communication Service holds the
    // connection string, the Email Service owns the verified domain;
    // ACS only authorises sends through linked domains.
    linkedDomains: [
      defaultDomain.id
    ]
  }
}

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'acs-email-vrbook-${env}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
  }
}

// Azure-managed domain — donotreply@<random>.azurecomm.net. Pre-warmed by
// Microsoft; no DNS records required. Custom-domain DKIM/SPF is OPS work.
resource defaultDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

resource acsSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: kv
  name: 'acs-connection-string'
  properties: {
    value: cs.listKeys().primaryConnectionString
    contentType: 'text/plain'
  }
}

output id string = cs.id
output name string = cs.name
output emailServiceName string = emailService.name
output senderDomain string = defaultDomain.properties.mailFromSenderDomain
