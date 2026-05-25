// container-app.bicep — Parameterized Container App (API, workers, etc.).
// Built from the §23.5 sample, generalized for env vars, scale rules, and KV-backed secrets.
//
// Secrets are referenced via keyVaultUrl + identity (the user-assigned MI must have
// "Key Vault Secrets User" role on the vault — wired in managed-identity.bicep).

@description('Container App name (e.g. ca-vrbook-api-prod).')
param name string

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object

@description('Container Apps Environment resource id.')
param environmentId string

@description('Container image, fully qualified (e.g. crvrbookprod.azurecr.io/api:abc123).')
param containerImage string

@description('ACR login server (e.g. crvrbookprod.azurecr.io) — used for the registries config.')
param registryServer string

@description('User-assigned managed identity resource id used for ACR pull, KV secret refs, and AzureAD-auth dependencies.')
param userAssignedIdentityId string

@description('Workload profile name (Consumption | d4 | ...). Defaults to Consumption.')
param workloadProfileName string = 'Consumption'

@description('Container HTTP target port.')
param targetPort int = 8080

@description('Expose ingress externally.')
param externalIngress bool = true

@description('Min replicas.')
param minReplicas int = 1

@description('Max replicas.')
param maxReplicas int = 10

@description('CPU cores (e.g. 1, 0.5).')
param cpu string = '1.0'

@description('Memory string (e.g. 2Gi, 1Gi).')
param memory string = '2Gi'

@description('Environment variables. Each item: { name: string, value?: string, secretRef?: string }.')
param envVars array = []

@description('KV-backed secrets. Each item: { name: string, keyVaultSecretName: string }.')
param secrets array = []

@description('Key Vault name used to construct keyVaultUrl for secrets.')
param keyVaultName string = ''

@description('Scale rule type: http | kedaServiceBus | none.')
@allowed([
  'http'
  'kedaServiceBus'
  'none'
])
param scaleRuleType string = 'http'

@description('HTTP concurrent requests target (when scaleRuleType=http).')
param httpConcurrentRequests int = 50

@description('Service Bus namespace fully-qualified name (when scaleRuleType=kedaServiceBus).')
param serviceBusNamespace string = ''

@description('Service Bus topic name to scale on (when scaleRuleType=kedaServiceBus).')
param serviceBusTopicName string = ''

@description('Service Bus subscription name to scale on (when scaleRuleType=kedaServiceBus).')
param serviceBusSubscriptionName string = ''

@description('Message count per replica trigger (when scaleRuleType=kedaServiceBus).')
param serviceBusMessageCount int = 10

@description('Include liveness/readiness probes (disable for non-HTTP workers).')
param includeProbes bool = true

@description('Revisions mode: single | multiple.')
@allowed([
  'single'
  'multiple'
])
param revisionsMode string = 'single'

// Build the secrets array in the keyVaultUrl + identity form.
var caSecrets = [for s in secrets: {
  name: s.name
  keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/${s.keyVaultSecretName}'
  identity: userAssignedIdentityId
}]

// Build scale rules.
var httpRule = [
  {
    name: 'http-scale'
    http: {
      metadata: {
        concurrentRequests: string(httpConcurrentRequests)
      }
    }
  }
]

var sbRule = [
  {
    name: 'sb-scale'
    custom: {
      type: 'azure-servicebus'
      metadata: {
        namespace: serviceBusNamespace
        topicName: serviceBusTopicName
        subscriptionName: serviceBusSubscriptionName
        messageCount: string(serviceBusMessageCount)
      }
      identity: userAssignedIdentityId
    }
  }
]

var scaleRules = scaleRuleType == 'http'
  ? httpRule
  : scaleRuleType == 'kedaServiceBus' ? sbRule : []

var probesArray = includeProbes ? [
  {
    type: 'Liveness'
    httpGet: {
      path: '/health/live'
      port: targetPort
    }
    periodSeconds: 30
  }
  {
    type: 'Readiness'
    httpGet: {
      path: '/health/ready'
      port: targetPort
    }
    periodSeconds: 10
  }
] : []

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: workloadProfileName
    configuration: {
      activeRevisionsMode: revisionsMode
      ingress: {
        external: externalIngress
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: registryServer
          identity: userAssignedIdentityId
        }
      ]
      secrets: caSecrets
    }
    template: {
      containers: [
        {
          name: 'app'
          image: containerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: envVars
          probes: probesArray
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: scaleRules
      }
    }
  }
}

output id string = app.id
output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
output principalId string = app.identity.principalId
output latestRevisionName string = app.properties.latestRevisionName
