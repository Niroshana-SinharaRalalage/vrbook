// redis.bicep — Azure Cache for Redis, Standard C1, non-clustered, TLS 1.2 min.
// Public access disabled; Private Endpoint into snet-data.

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

@description('Subnet for the private endpoint.')
param subnetId string

@description('Private DNS zone id for privatelink.redis.cache.windows.net.')
param privateDnsZoneId string

@description('Redis SKU family (C = Standard/Basic).')
param skuFamily string = 'C'

@description('Redis SKU name.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

@description('Redis capacity (C1 = 1 = 1GB).')
param skuCapacity int = 1

var redisName = 'redis-vrbook-${env}'

resource redis 'Microsoft.Cache/redis@2024-11-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: {
      name: skuName
      family: skuFamily
      capacity: skuCapacity
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    redisConfiguration: {
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: 'pe-${redisName}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'redis-link'
        properties: {
          privateLinkServiceId: redis.id
          groupIds: [
            'redisCache'
          ]
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
        name: 'redis'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output id string = redis.id
output name string = redis.name
output hostName string = redis.properties.hostName
output port int = redis.properties.sslPort
