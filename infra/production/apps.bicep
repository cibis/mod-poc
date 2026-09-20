param imageTag string
param location string = resourceGroup().location

// ── core outputs ──────────────────────────────────────────────────────────────
param caeId string
param caeDefaultDomain string

param ingestIdentityId string
param ingestClientId string
param processingIdentityId string
param processingClientId string
param managementIdentityId string
param managementClientId string
param portalIdentityId string
param portalClientId string
param reportingIdentityId string
param reportingClientId string
param migratorIdentityId string
param migratorClientId string

param ingestPrincipalId string
param processingPrincipalId string
param managementPrincipalId string
param portalPrincipalId string
param reportingPrincipalId string
param appInsightsConnectionString string
param kvUri string
param acrLoginServer string
param storageAccountName string
param checkpointBlobContainerUrl string
param ehFqdn string
param cmdSbFqdn string
param simSbFqdn string
param sqlConnectionString string

param cmdSbIssuerKeySecretUri string
param simSbIssuerKeySecretUri string
param portalJwtKeySecretUri string
param reportingJwtKeySecretUri string

// ── controller space (passed in from up.sh) ───────────────────────────────────
param controllerSubscriptionId string
param controllerResourceGroup string
param controllerEnvironmentId string
param controllerLocation string
param collectorPullIdentityId string

// ── optional developer principals for migrator ────────────────────────────────
param developerPrincipals string = ''

// ── derived values ────────────────────────────────────────────────────────────
var modEnv = 'production'
var ingestUrl = 'https://ca-ingest.${caeDefaultDomain}'
var managementUrl = 'https://ca-mgmt.${caeDefaultDomain}'
var collectorImage = '${acrLoginServer}/mod/collector:${imageTag}'

// ── identity name helpers (for principal output names in migrator env) ─────────
var ingestIdName     = last(split(ingestIdentityId, '/'))
var processingIdName = last(split(processingIdentityId, '/'))
var managementIdName = last(split(managementIdentityId, '/'))
var portalIdName     = last(split(portalIdentityId, '/'))
var reportingIdName  = last(split(reportingIdentityId, '/'))

// ── ca-ingest ─────────────────────────────────────────────────────────────────
resource caIngest 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-ingest'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${ingestIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        clientCertificateMode: 'require'
      }
    }
    template: {
      containers: [
        {
          name: 'ingest-api'
          image: '${acrLoginServer}/mod/ingest-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: ingestClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'MOD_ENVIRONMENT', value: modEnv }
            { name: 'SQL_CONNECTION', value: sqlConnectionString }
            { name: 'EVENTHUB_FQDN', value: ehFqdn }
            { name: 'EVENTHUB_NAME', value: 'telemetry' }
            { name: 'COLLECTOR_CA_CERT_PEM_BASE64', value: '' }
            { name: 'INGEST_MAX_EVENTS', value: '500' }
            { name: 'INGEST_MAX_BYTES', value: '262144' }
            { name: 'INGEST_RATE_LIMIT_PER_COLLECTOR', value: '20' }
            { name: 'REGISTRY_CACHE_SECONDS', value: '30' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/readyz', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 4
        rules: [
          {
            name: 'http-scaler'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

// ── ca-processing ─────────────────────────────────────────────────────────────
resource caProcessing 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-processing'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${processingIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      ingress: null
    }
    template: {
      containers: [
        {
          name: 'processing'
          image: '${acrLoginServer}/mod/processing:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: processingClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'MOD_ENVIRONMENT', value: modEnv }
            { name: 'SQL_CONNECTION', value: sqlConnectionString }
            { name: 'EVENTHUB_FQDN', value: ehFqdn }
            { name: 'EVENTHUB_NAME', value: 'telemetry' }
            { name: 'EVENTHUB_CONSUMER_GROUP', value: 'processing' }
            { name: 'CHECKPOINT_BLOB_CONTAINER_URL', value: checkpointBlobContainerUrl }
            { name: 'WINDOW_GRACE_SECONDS', value: '120' }
            { name: 'DEDUPE_RETENTION_DAYS', value: '14' }
            { name: 'RAW_RETENTION_DAYS', value: '7' }
            { name: 'PROCESSING_DELAY_MS', value: '20' }
            { name: 'MAPPING_CACHE_SECONDS', value: '30' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 8
        rules: [
          {
            name: 'eventhub-scaler'
            custom: {
              type: 'azure-eventhub'
              metadata: {
                eventHubNamespace: split(ehFqdn, '.')[0]
                eventHubName: 'telemetry'
                consumerGroup: 'processing'
                unprocessedEventThreshold: '64'
                checkpointStrategy: 'blobMetadata'
                blobContainer: 'checkpoints'
                storageAccountName: storageAccountName
                clientId: processingClientId
              }
            }
          }
        ]
      }
    }
  }
}

// ── ca-portal ─────────────────────────────────────────────────────────────────
resource caPortal 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-portal'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${portalIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      secrets: [
        {
          name: 'portal-jwt-key'
          keyVaultUrl: portalJwtKeySecretUri
          identity: portalIdentityId
        }
        {
          name: 'sim-sb-sas-key'
          keyVaultUrl: simSbIssuerKeySecretUri
          identity: portalIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'portal'
          image: '${acrLoginServer}/mod/portal:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: portalClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'MOD_ENVIRONMENT', value: modEnv }
            { name: 'SQL_CONNECTION', value: sqlConnectionString }
            { name: 'KEYVAULT_URI', value: kvUri }
            { name: 'PORTAL_JWT_KEY', secretRef: 'portal-jwt-key' }
            { name: 'CMD_SB_FQDN', value: cmdSbFqdn }
            { name: 'SIM_SB_FQDN', value: simSbFqdn }
            { name: 'SIM_SB_SAS_KEY_NAME', value: 'sim-issuer' }
            { name: 'SIM_SB_SAS_KEY', secretRef: 'sim-sb-sas-key' }
            { name: 'EVENTHUB_FQDN', value: ehFqdn }
            { name: 'EVENTHUB_NAME', value: 'telemetry' }
            { name: 'EVENTHUB_CONSUMER_GROUP', value: 'processing' }
            { name: 'CHECKPOINT_BLOB_CONTAINER_URL', value: checkpointBlobContainerUrl }
            { name: 'PRODUCTION_SUBSCRIPTION_ID', value: subscription().subscriptionId }
            { name: 'PRODUCTION_RESOURCE_GROUP', value: resourceGroup().name }
            { name: 'INGEST_APP_NAME', value: 'ca-ingest' }
            { name: 'PROCESSING_APP_NAME', value: 'ca-processing' }
            { name: 'MANAGEMENT_APP_NAME', value: 'ca-mgmt' }
            { name: 'PORTAL_APP_NAME', value: 'ca-portal' }
            { name: 'REPORTING_APP_NAME', value: 'ca-reporting' }
            { name: 'CONTROLLER_SUBSCRIPTION_ID', value: controllerSubscriptionId }
            { name: 'CONTROLLER_RESOURCE_GROUP', value: controllerResourceGroup }
            { name: 'CONTROLLER_ENVIRONMENT_ID', value: controllerEnvironmentId }
            { name: 'CONTROLLER_LOCATION', value: controllerLocation }
            { name: 'COLLECTOR_IMAGE', value: collectorImage }
            { name: 'COLLECTOR_PULL_IDENTITY_ID', value: collectorPullIdentityId }
            { name: 'MANAGEMENT_URL', value: managementUrl }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/readyz', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ── ca-reporting ──────────────────────────────────────────────────────────────
resource caReporting 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-reporting'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${reportingIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      secrets: [
        {
          name: 'reporting-jwt-key'
          keyVaultUrl: reportingJwtKeySecretUri
          identity: reportingIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'reporting'
          image: '${acrLoginServer}/mod/customer:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: reportingClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'MOD_ENVIRONMENT', value: modEnv }
            { name: 'SQL_CONNECTION', value: sqlConnectionString }
            { name: 'KEYVAULT_URI', value: kvUri }
            { name: 'REPORTING_JWT_KEY', secretRef: 'reporting-jwt-key' }
            { name: 'FRESHNESS_STALE_SECONDS', value: '60' }
            { name: 'LIVE_POLL_SECONDS', value: '2' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/readyz', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

// ── ca-mgmt ───────────────────────────────────────────────────────────────────
resource caMgmt 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-mgmt'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managementIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        clientCertificateMode: 'accept'
      }
      secrets: [
        {
          name: 'cmd-sb-sas-key'
          keyVaultUrl: cmdSbIssuerKeySecretUri
          identity: managementIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'management-api'
          image: '${acrLoginServer}/mod/management-api:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: managementClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'MOD_ENVIRONMENT', value: modEnv }
            { name: 'SQL_CONNECTION', value: sqlConnectionString }
            { name: 'KEYVAULT_URI', value: kvUri }
            { name: 'CA_CERT_NAME', value: 'collector-ca' }
            { name: 'CERT_VALIDITY_DAYS', value: '7' }
            { name: 'CMD_SB_FQDN', value: cmdSbFqdn }
            { name: 'CMD_SB_SAS_KEY_NAME', value: 'collector-issuer' }
            { name: 'CMD_SB_SAS_KEY', secretRef: 'cmd-sb-sas-key' }
            { name: 'INGEST_URL', value: ingestUrl }
            { name: 'MANAGEMENT_URL', value: managementUrl }
            { name: 'COMMAND_TOKEN_LIFETIME_MINUTES', value: '60' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/readyz', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

// ── job-dbmigrate ─────────────────────────────────────────────────────────────
resource jobDbMigrate 'Microsoft.App/jobs@2024-03-01' = {
  name: 'job-dbmigrate'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migratorIdentityId}': {}
    }
  }
  properties: {
    environmentId: caeId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      secrets: [
        {
          name: 'admin-password'
          keyVaultUrl: '${kvUri}secrets/admin-password/'
          identity: migratorIdentityId
        }
        {
          name: 'customer-password'
          keyVaultUrl: '${kvUri}secrets/customer-password/'
          identity: migratorIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'db-migrator'
          image: '${acrLoginServer}/mod/db-migrator:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: migratorClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'MOD_ENVIRONMENT', value: modEnv }
            { name: 'SQL_CONNECTION', value: sqlConnectionString }
            { name: 'IDENTITY_INGEST_NAME', value: ingestIdName }
            { name: 'IDENTITY_INGEST_OBJECT_ID', value: ingestPrincipalId }
            { name: 'IDENTITY_PROCESSING_NAME', value: processingIdName }
            { name: 'IDENTITY_PROCESSING_OBJECT_ID', value: processingPrincipalId }
            { name: 'IDENTITY_MANAGEMENT_NAME', value: managementIdName }
            { name: 'IDENTITY_MANAGEMENT_OBJECT_ID', value: managementPrincipalId }
            { name: 'IDENTITY_PORTAL_NAME', value: portalIdName }
            { name: 'IDENTITY_PORTAL_OBJECT_ID', value: portalPrincipalId }
            { name: 'IDENTITY_REPORTING_NAME', value: reportingIdName }
            { name: 'IDENTITY_REPORTING_OBJECT_ID', value: reportingPrincipalId }
            { name: 'DEVELOPER_PRINCIPALS', value: developerPrincipals }
            { name: 'SEED_DEMO_DATA', value: 'true' }
            { name: 'ADMIN_PASSWORD', secretRef: 'admin-password' }
            { name: 'CUSTOMER_PASSWORD', secretRef: 'customer-password' }
          ]
        }
      ]
      initContainers: []
      volumes: []
    }
  }
}
