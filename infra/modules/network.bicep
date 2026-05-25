// network.bicep — VNet, subnets, NSGs, and private DNS zones for VRBook.
// Two subnets:
//   snet-apps  — delegated to Microsoft.App/environments for Container Apps Env
//   snet-data  — hosts Private Endpoints for Postgres + Redis
// Private DNS zones:
//   privatelink.postgres.database.azure.com
//   privatelink.redis.cache.windows.net

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

@description('VNet address space.')
param vnetAddressPrefix string = '10.40.0.0/16'

@description('Subnet for Container Apps Environment (must be /23 minimum for workload profiles).')
param appsSubnetPrefix string = '10.40.0.0/23'

@description('Subnet for Private Endpoints (Postgres, Redis, etc.).')
param dataSubnetPrefix string = '10.40.2.0/24'

var vnetName = 'vnet-vrbook-${env}'
var appsSubnetName = 'snet-apps'
var dataSubnetName = 'snet-data'

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
    ]
  }
}

// Private DNS zones live at the resource group scope (global resources).
resource pgDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource redisDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
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

resource redisDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
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
output pgPrivateDnsZoneId string = pgDnsZone.id
output redisPrivateDnsZoneId string = redisDnsZone.id
