// network.bicep — VNet, subnets, NSGs, and private DNS zones for VRBook.
// Two subnets:
//   snet-apps  — delegated to Microsoft.App/environments for Container Apps Env
//   snet-data  — hosts Private Endpoints for Postgres + Redis
// Private DNS zones:
//   privatelink.postgres.database.azure.com  (always)
//   privatelink.redis.cache.windows.net       (only when includeRedisDns=true;
//     Slice OPS.INFRA.2 gates this on the deployRedis flag from main.bicep —
//     Redis is currently not deployed anywhere, so the DNS zone was pure
//     dead weight (~$0.50/mo). Re-enable when the redis module comes back.)

@description('Environment short name (dev | staging | prod).')
param env string

@description('Whether to provision the privatelink.redis.cache.windows.net private DNS zone + VNet link. Should mirror main.bicep`s deployRedis flag — otherwise the DNS zone is orphaned.')
param includeRedisDns bool = false

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

@description('VNet address space.')
param vnetAddressPrefix string = '10.40.0.0/16'

@description('Subnet for Container Apps Environment (must be /23 minimum for workload profiles).')
param appsSubnetPrefix string = '10.40.0.0/23'

@description('Subnet for Private Endpoints (Redis PE, future PEs).')
param dataSubnetPrefix string = '10.40.2.0/24'

@description('Subnet delegated to Microsoft.DBforPostgreSQL/flexibleServers (VNet-injected Postgres).')
param pgSubnetPrefix string = '10.40.3.0/24'

var vnetName = 'vnet-vrbook-${env}'
var appsSubnetName = 'snet-apps'
var dataSubnetName = 'snet-data'
var pgSubnetName = 'snet-pg'

resource appsNsg 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-${appsSubnetName}-${env}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowVnetInbound'
        properties: {
          priority: 100
          access: 'Allow'
          direction: 'Inbound'
          protocol: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource dataNsg 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-${dataSubnetName}-${env}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowAppsToData'
        properties: {
          priority: 100
          access: 'Allow'
          direction: 'Inbound'
          protocol: '*'
          sourceAddressPrefix: appsSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: dataSubnetPrefix
          destinationPortRange: '*'
        }
      }
      {
        name: 'AllowAppsToPg'
        properties: {
          priority: 110
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourceAddressPrefix: appsSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: pgSubnetPrefix
          destinationPortRange: '5432'
        }
      }
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          access: 'Deny'
          direction: 'Inbound'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: appsSubnetName
        properties: {
          addressPrefix: appsSubnetPrefix
          networkSecurityGroup: {
            id: appsNsg.id
          }
          delegations: [
            {
              name: 'Microsoft.App.environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
      {
        name: dataSubnetName
        properties: {
          addressPrefix: dataSubnetPrefix
          networkSecurityGroup: {
            id: dataNsg.id
          }
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
      {
        name: pgSubnetName
        properties: {
          addressPrefix: pgSubnetPrefix
          networkSecurityGroup: {
            id: dataNsg.id
          }
          delegations: [
            {
              name: 'Microsoft.DBforPostgreSQL.flexibleServers'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

// Private DNS zones live at the resource group scope (global resources).
resource pgDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource redisDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = if (includeRedisDns) {
  name: 'privatelink.redis.cache.windows.net'
  location: 'global'
  tags: tags
}

resource pgDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: pgDnsZone
  name: '${vnetName}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource redisDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = if (includeRedisDns) {
  parent: redisDnsZone
  name: '${vnetName}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output appsSubnetId string = '${vnet.id}/subnets/${appsSubnetName}'
output dataSubnetId string = '${vnet.id}/subnets/${dataSubnetName}'
output pgSubnetId string = '${vnet.id}/subnets/${pgSubnetName}'
output pgPrivateDnsZoneId string = pgDnsZone.id
output redisPrivateDnsZoneId string = includeRedisDns ? redisDnsZone.id : ''
