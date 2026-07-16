// front-door.bicep — Azure Front Door Standard + WAF Default Rule Set.
// Single backend pool pointing at the API Container App FQDN.
// Caching enabled for /_next/static/* and the listing pages.

@description('Environment short name (dev | staging | prod).')
param env string

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

@description('Origin host name (API Container App FQDN, e.g. ca-vrbook-api-prod.proudwave.eastus2.azurecontainerapps.io).')
param originHostName string

var profileName = 'fd-vrbook-${env}'
var endpointName = 'fde-vrbook-${env}'
var wafPolicyName = 'wafvrbook${env}'

resource profile 'Microsoft.Cdn/profiles@2024-09-01' = {
  name: profileName
  location: 'global'
  tags: tags
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-09-01' = {
  parent: profile
  name: endpointName
  location: 'global'
  tags: tags
  properties: {
    enabledState: 'Enabled'
  }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2024-09-01' = {
  parent: profile
  name: 'api'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    healthProbeSettings: {
      probePath: '/health/ready'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 30
    }
    sessionAffinityState: 'Disabled'
  }
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2024-09-01' = {
  parent: originGroup
  name: 'api-origin'
  properties: {
    hostName: originHostName
    httpPort: 80
    httpsPort: 443
    originHostHeader: originHostName
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}

resource cacheRuleSet 'Microsoft.Cdn/profiles/ruleSets@2024-09-01' = {
  parent: profile
  name: 'staticcache'
}

resource nextStaticRule 'Microsoft.Cdn/profiles/ruleSets/rules@2024-09-01' = {
  parent: cacheRuleSet
  name: 'cacheNextStatic'
  properties: {
    order: 1
    matchProcessingBehavior: 'Continue'
    conditions: [
      {
        name: 'UrlPath'
        parameters: {
          typeName: 'DeliveryRuleUrlPathMatchConditionParameters'
          operator: 'BeginsWith'
          negateCondition: false
          matchValues: [
            '/_next/static/'
          ]
        }
      }
    ]
    actions: [
      {
        name: 'RouteConfigurationOverride'
        parameters: {
          typeName: 'DeliveryRuleRouteConfigurationOverrideActionParameters'
          cacheConfiguration: {
            cacheBehavior: 'OverrideAlways'
            cacheDuration: '7.00:00:00'
            queryStringCachingBehavior: 'IgnoreQueryString'
            isCompressionEnabled: 'Enabled'
          }
        }
      }
    ]
  }
}

resource listingsRule 'Microsoft.Cdn/profiles/ruleSets/rules@2024-09-01' = {
  parent: cacheRuleSet
  name: 'cacheListings'
  properties: {
    order: 2
    matchProcessingBehavior: 'Continue'
    conditions: [
      {
        name: 'UrlPath'
        parameters: {
          typeName: 'DeliveryRuleUrlPathMatchConditionParameters'
          operator: 'BeginsWith'
          negateCondition: false
          matchValues: [
            '/listings'
            '/properties'
            '/search'
          ]
        }
      }
    ]
    actions: [
      {
        name: 'RouteConfigurationOverride'
        parameters: {
          typeName: 'DeliveryRuleRouteConfigurationOverrideActionParameters'
          cacheConfiguration: {
            cacheBehavior: 'OverrideIfOriginMissing'
            cacheDuration: '0.00:05:00'
            queryStringCachingBehavior: 'IncludeSpecifiedQueryStrings'
            queryParameters: 'page,sort,location,checkin,checkout,guests'
            isCompressionEnabled: 'Enabled'
          }
        }
      }
    ]
  }
}

// VRB-307 — WAF runs in Detection first (log-only) to avoid false-positive
// blocks, then flips to Prevention after a clean observation window at prod
// cutover (that flip + the 429 synthetic proof are the VRB-301/prod-arm step).
@description('WAF enforcement mode. Detection = log only (safe launch default); Prevention = block. Flip to Prevention at prod cutover after a clean window.')
@allowed([
  'Detection'
  'Prevention'
])
param wafMode string = 'Detection'

@description('VRB-307 — rate-limit threshold: max requests per client IP per minute before the RateLimitRule trips (logged in Detection, blocked in Prevention).')
param rateLimitThreshold int = 100

resource wafPolicy 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: wafPolicyName
  location: 'global'
  tags: tags
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: wafMode
      requestBodyCheck: 'Enabled'
    }
    // VRB-307 — per-client-IP rate limit across the whole public surface
    // (Front Door groups the count by client IP automatically). A later pass
    // may add a tighter limit scoped to /api/v1/*auth/login paths (brute-force).
    customRules: {
      rules: [
        {
          name: 'RateLimitPerClientIp'
          priority: 100
          enabledState: 'Enabled'
          ruleType: 'RateLimitRule'
          rateLimitDurationInMinutes: 1
          rateLimitThreshold: rateLimitThreshold
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              negateCondition: false
              matchValue: [
                '/'
              ]
            }
          ]
          action: 'Block'
        }
      ]
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
          ruleSetAction: 'Block'
        }
      ]
    }
  }
}

resource securityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2024-09-01' = {
  parent: profile
  name: 'waf-default'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: wafPolicy.id
      }
      associations: [
        {
          domains: [
            {
              id: endpoint.id
            }
          ]
          patternsToMatch: [
            '/*'
          ]
        }
      ]
    }
  }
}

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-09-01' = {
  parent: endpoint
  name: 'default'
  properties: {
    originGroup: {
      id: originGroup.id
    }
    supportedProtocols: [
      'Http'
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
    httpsRedirect: 'Enabled'
    ruleSets: [
      {
        id: cacheRuleSet.id
      }
    ]
  }
  dependsOn: [
    origin
  ]
}

output endpointHostName string = endpoint.properties.hostName
output profileName string = profile.name
output wafPolicyId string = wafPolicy.id
