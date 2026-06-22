// container-app-job.bicep — Parameterized Container App Job (Schedule | Event | Manual).
// Used for the iCal sync worker (cron */5 * * * *), DB migrator (manual), DLQ replayer, etc.

@description('Container App Job name (e.g. caj-vrbook-sync-prod).')
param name string

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object

@description('Container Apps Environment resource id.')
param environmentId string

@description('Container image, fully qualified.')
param containerImage string

@description('ACR login server.')
param registryServer string

@description('User-assigned managed identity resource id.')
param userAssignedIdentityId string

@description('Workload profile name (Consumption | d4 | ...).')
param workloadProfileName string = 'Consumption'

@description('Trigger type for the job.')
@allowed([
  'Schedule'
  'Event'
  'Manual'
])
param triggerType string = 'Schedule'

@description('Cron expression (required when triggerType=Schedule). E.g. "*/5 * * * *".')
param cronExpression string = '*/5 * * * *'

@description('Maximum time a replica may run, in seconds.')
param replicaTimeoutSeconds int = 1800

@description('Number of concurrent replicas per execution.')
param parallelism int = 1

@description('Replica completion count — how many of the parallel replicas must succeed.')
param replicaCompletionCount int = 1

@description('Replica retry limit.')
param replicaRetryLimit int = 3

@description('CPU cores.')
param cpu string = '0.5'

@description('Memory string.')
param memory string = '1Gi'

@description('Environment variables. Each item: { name, value? | secretRef? }.')
param envVars array = []

@description('Container entrypoint arguments (e.g. ["--mode=completion"]). Empty = use image default.')
param commandArgs array = []

@description('KV-backed secrets. Each item: { name, keyVaultSecretName }.')
param secrets array = []

@description('Key Vault name used to construct keyVaultUrl for secrets.')
param keyVaultName string = ''

@description('For Event-triggered jobs: scale rules array (KEDA shape). Ignored for Schedule/Manual.')
param eventScaleRules array = []

@description('For Event-triggered jobs: min executions.')
param minExecutions int = 0

@description('For Event-triggered jobs: max executions.')
param maxExecutions int = 10

@description('For Event-triggered jobs: polling interval seconds.')
param pollingInterval int = 30

var jobSecrets = [for s in secrets: {
  name: s.name
  keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/${s.keyVaultSecretName}'
  identity: userAssignedIdentityId
}]

var scheduleTriggerConfig = {
  scheduleTriggerConfig: {
    cronExpression: cronExpression
    parallelism: parallelism
    replicaCompletionCount: replicaCompletionCount
  }
}

var manualTriggerConfig = {
  manualTriggerConfig: {
    parallelism: parallelism
    replicaCompletionCount: replicaCompletionCount
  }
}

var eventTriggerConfig = {
  eventTriggerConfig: {
    parallelism: parallelism
    replicaCompletionCount: replicaCompletionCount
    scale: {
      minExecutions: minExecutions
      maxExecutions: maxExecutions
      pollingInterval: pollingInterval
      rules: eventScaleRules
    }
  }
}

var triggerConfig = triggerType == 'Schedule'
  ? scheduleTriggerConfig
  : triggerType == 'Manual' ? manualTriggerConfig : eventTriggerConfig

resource job 'Microsoft.App/jobs@2025-01-01' = {
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
    configuration: union(
      {
        triggerType: triggerType
        replicaTimeout: replicaTimeoutSeconds
        replicaRetryLimit: replicaRetryLimit
        registries: [
          {
            server: registryServer
            identity: userAssignedIdentityId
          }
        ]
        secrets: jobSecrets
      },
      triggerConfig
    )
    template: {
      containers: [
        union(
          {
            name: 'job'
            image: containerImage
            resources: {
              cpu: json(cpu)
              memory: memory
            }
            env: envVars
          },
          empty(commandArgs) ? {} : { args: commandArgs }
        )
      ]
    }
  }
}

output id string = job.id
output name string = job.name
