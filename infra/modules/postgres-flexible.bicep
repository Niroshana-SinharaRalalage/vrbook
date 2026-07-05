// postgres-flexible.bicep — PostgreSQL Flexible Server v16.
// Two networking modes:
//   * VNet-injected (default): publicNetworkAccess='Disabled', delegated subnet.
//   * Public-access (staging only per OPS.INFRA.1): publicNetworkAccess='Enabled',
//     no delegated subnet, IP firewall rules deployed as child resources.
// The public variant matches LankaConnect's staging posture — internet-reachable
// but IP-allowlisted. Prod stays VNet-injected (parameter default).
//
// HA toggle, sku, and storage are env-driven.
// Admin password is taken from Key Vault (the caller passes a getSecret() reference
// via a parameter — never a literal).

@description('Environment short name (dev | staging | prod).')
param env string

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

@description('Resource ID of the delegated/data subnet for VNet injection. Ignored when publicNetworkAccess=Enabled.')
param subnetId string

@description('Resource ID of the private DNS zone privatelink.postgres.database.azure.com. Ignored when publicNetworkAccess=Enabled.')
param privateDnsZoneId string

@description('OPS.INFRA.1 — networking mode. Default Disabled = VNet-injected (private, prod-safe). Enabled = public with IP allowlist (staging LankaConnect parity).')
@allowed([
  'Enabled'
  'Disabled'
])
param publicNetworkAccess string = 'Disabled'

@description('OPS.INFRA.1 — IP allowlist for the public-access mode. Each entry is { name, startIp, endIp }. Ignored when publicNetworkAccess=Disabled.')
param firewallRules array = []

@description('OPS.INFRA.1 rev 2 — override the derived server name. Empty (default) = psql-vrbook-{env}. Set to e.g. psql-vrbook-staging-v2 for the blue/green rebuild.')
param serverNameOverride string = ''

@description('SKU name without tier prefix (e.g. Standard_B2s, Standard_D2ds_v5, Standard_D4ds_v5).')
param skuName string = 'Standard_D2ds_v5'

@description('Tier (Burstable | GeneralPurpose | MemoryOptimized).')
@allowed([
  'Burstable'
  'GeneralPurpose'
  'MemoryOptimized'
])
param skuTier string = 'GeneralPurpose'

@description('Storage size in GB.')
param storageSizeGB int = 128

@description('Backup retention days (proposal: 14 default, 35 prod).')
param backupRetentionDays int = 14

@description('Geo-redundant backup. Off for Phase 1 (single region).')
param geoRedundantBackup string = 'Disabled'

@description('Enable zone-redundant HA (prod only).')
param haEnabled bool = false

@description('Postgres administrator login.')
param administratorLogin string

@description('Postgres administrator password (passed in via getSecret() from a KV module reference).')
@secure()
param administratorLoginPassword string

@description('Postgres major version.')
param postgresVersion string = '16'

var serverName = empty(serverNameOverride) ? 'psql-vrbook-${env}' : serverNameOverride

resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: postgresVersion
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: geoRedundantBackup
    }
    network: publicNetworkAccess == 'Enabled' ? {
      publicNetworkAccess: 'Enabled'
    } : {
      delegatedSubnetResourceId: subnetId
      privateDnsZoneArmResourceId: privateDnsZoneId
      publicNetworkAccess: 'Disabled'
    }
    highAvailability: {
      mode: haEnabled ? 'ZoneRedundant' : 'Disabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: subscription().tenantId
    }
  }
}

// OPS.INFRA.1 — IP firewall rules for public-access mode.
// Empty array in private mode; caller passes the allowlist for staging.
resource firewall 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = [for rule in firewallRules: {
  parent: pg
  name: rule.name
  properties: {
    startIpAddress: rule.startIp
    endIpAddress: rule.endIp
  }
}]

resource requireSslConfig 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: pg
  name: 'require_secure_transport'
  properties: {
    value: 'on'
    source: 'user-override'
  }
}

output id string = pg.id
output name string = pg.name
output fqdn string = pg.properties.fullyQualifiedDomainName
