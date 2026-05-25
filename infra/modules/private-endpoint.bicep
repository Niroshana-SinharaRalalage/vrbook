// private-endpoint.bicep — Generic Private Endpoint helper with DNS zone group.

@description('Private Endpoint name.')
param name string

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object

@description('Subnet resource ID to place the endpoint in.')
param subnetId string

@description('Resource ID of the target private-link-enabled resource (Postgres / Redis / Storage / etc.).')
param privateLinkServiceId string

@description('Group IDs for the PE — e.g. ["postgresqlServer"], ["redisCache"], ["blob"].')
param groupIds array

@description('Private DNS zone resource ID to attach (single-zone PEs).')
param privateDnsZoneId string

resource pe 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${name}-link'
        properties: {
          privateLinkServiceId: privateLinkServiceId
          groupIds: groupIds
        }
      }
    ]
  }
}

resource peDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: pe
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'default'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output id string = pe.id
output name string = pe.name
