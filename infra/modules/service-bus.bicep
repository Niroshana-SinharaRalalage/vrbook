// service-bus.bicep — Standard namespace + topics for VRBook async messaging.
// Topics from §15.1: bookings, notifications, sync.
// Each topic gets a default subscription with dead-lettering on message expiration enabled.
// Managed Identity is the preferred auth path (per §14.4); the AuthorizationRule below is
// a fallback for tooling that doesn't support AAD yet (e.g. local dev) and is exposed as
// a "connection name" output for KV seeding by the pipeline.

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

@description('Topic names to create.')
param topicNames array = [
  'bookings'
  'notifications'
  'sync'
]

@description('Default subscription name created under every topic.')
param defaultSubscriptionName string = 'default'

var namespaceName = 'sb-vrbook-${env}'

resource ns 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    zoneRedundant: false
    disableLocalAuth: false
  }
}

resource topics 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = [for t in topicNames: {
  parent: ns
  name: t
  properties: {
    defaultMessageTimeToLive: 'P14D'
    enableBatchedOperations: true
    enablePartitioning: false
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    supportOrdering: true
  }
}]

resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = [for (t, i) in topicNames: {
  parent: topics[i]
  name: defaultSubscriptionName
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
    requiresSession: false
    defaultMessageTimeToLive: 'P14D'
  }
}]

// Listen+Send rule used only as a fallback (KV-seeded). Managed Identity is the primary auth path.
resource sendListenRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2024-01-01' = {
  parent: ns
  name: 'app-send-listen'
  properties: {
    rights: [
      'Listen'
      'Send'
    ]
  }
}

output id string = ns.id
output name string = ns.name
output endpoint string = 'sb://${ns.name}.servicebus.windows.net/'
output namespaceFqdn string = '${ns.name}.servicebus.windows.net'
output connectionAuthRuleName string = sendListenRule.name
