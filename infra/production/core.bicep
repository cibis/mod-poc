param prefix string = 'modpoc'
@minLength(2)
@maxLength(8)
param suffix string
param location string = resourceGroup().location
param deployerObjectId string = ''

// ── role definition IDs ───────────────────────────────────────────────────────
var acrPullRoleId                = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var ehDataSenderRoleId           = '2b629674-e913-4c01-ae53-ef4638d8f975'
var ehDataReceiverRoleId         = 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
var storageBlobContribRoleId     = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageBlobReaderRoleId      = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var kvSecretsUserRoleId          = '4633458b-17de-408a-b874-0445c86b69e6'
var sbDataOwnerRoleId            = '090c5cfd-751d-490a-894a-3ce6f1109419'
var readerRoleId                 = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

// ── resource names (param-derived, usable in scope/parent/name/listKeys) ──────
var kvName       = 'kv-${prefix}-${suffix}'
var acrName      = 'acr${prefix}${suffix}'
var storageName  = 'st${prefix}${suffix}'
var ehNsName     = 'evhns-${prefix}-${suffix}'
var cmdSbNsName  = 'sbcmd-${prefix}-${suffix}'
var simSbNsName  = 'sbsim-${prefix}-${suffix}'

// identity resource IDs (param-derived)
var ingestIdName     = 'id-${prefix}-ingest'
var processingIdName = 'id-${prefix}-processing'
var managementIdName = 'id-${prefix}-management'
var portalIdName     = 'id-${prefix}-portal'
var reportingIdName  = 'id-${prefix}-reporting'
var migratorIdName   = 'id-${prefix}-migrator'

// ── modules ───────────────────────────────────────────────────────────────────
module identities '../modules/identities.bicep' = {
  name: 'identities'
  params: { prefix: prefix, location: location }
}

module observability '../modules/observability.bicep' = {
  name: 'observability'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module kv '../modules/keyvault.bicep' = {
  name: 'keyvault'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module acr '../modules/registry.bicep' = {
  name: 'registry'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module storage '../modules/storage.bicep' = {
  name: 'storage'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module eventhubs '../modules/eventhubs.bicep' = {
  name: 'eventhubs'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module sbcmd '../modules/servicebus-cmd.bicep' = {
  name: 'servicebus-cmd'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module sbsim '../modules/servicebus-sim.bicep' = {
  name: 'servicebus-sim'
  params: { prefix: prefix, suffix: suffix, location: location }
}

module sql '../modules/sql.bicep' = {
  name: 'sql'
  params: {
    prefix: prefix
    suffix: suffix
    location: location
    migratorPrincipalId: identities.outputs.migratorPrincipalId
  }
}

module cae '../modules/cae.bicep' = {
  name: 'cae'
  params: {
    prefix: prefix
    suffix: suffix
    location: location
    workspaceId: observability.outputs.workspaceId
  }
}

// ── existing resource refs (names from vars, calculable at deploy-start) ──────
resource kvRef 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: kvName
}

resource acrRef 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource storageRef 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource ehNsRef 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: ehNsName
}

resource ehHubRef 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' existing = {
  parent: ehNsRef
  name: 'telemetry'
}

resource cmdNsRef 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: cmdSbNsName
}

resource simNsRef 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: simSbNsName
}

// ── Key Vault secrets ─────────────────────────────────────────────────────────
resource cmdSbIssuerKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kvRef
  name: 'cmd-sb-issuer-key'
  properties: {
    value: listKeys(
      resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', cmdSbNsName, 'collector-issuer'),
      '2022-10-01-preview'
    ).primaryKey
  }
  dependsOn: [kv, sbcmd]
}

resource simSbIssuerKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kvRef
  name: 'sim-sb-issuer-key'
  properties: {
    value: listKeys(
      resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', simSbNsName, 'sim-issuer'),
      '2022-10-01-preview'
    ).primaryKey
  }
  dependsOn: [kv, sbsim]
}

// ── AcrPull — all six identities ──────────────────────────────────────────────
resource acrPullIngest 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acrRef
  name: guid(resourceId('Microsoft.ContainerRegistry/registries', acrName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', ingestIdName), acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identities.outputs.ingestPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [acr]
}

resource acrPullProcessing 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acrRef
  name: guid(resourceId('Microsoft.ContainerRegistry/registries', acrName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', processingIdName), acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identities.outputs.processingPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [acr]
}

resource acrPullManagement 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acrRef
  name: guid(resourceId('Microsoft.ContainerRegistry/registries', acrName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', managementIdName), acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identities.outputs.managementPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [acr]
}

resource acrPullPortal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acrRef
  name: guid(resourceId('Microsoft.ContainerRegistry/registries', acrName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [acr]
}

resource acrPullReporting 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acrRef
  name: guid(resourceId('Microsoft.ContainerRegistry/registries', acrName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', reportingIdName), acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identities.outputs.reportingPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [acr]
}

resource acrPullMigrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acrRef
  name: guid(resourceId('Microsoft.ContainerRegistry/registries', acrName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', migratorIdName), acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identities.outputs.migratorPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [acr]
}

// ── Event Hubs roles ──────────────────────────────────────────────────────────
var ehHubResId = resourceId('Microsoft.EventHub/namespaces/eventhubs', ehNsName, 'telemetry')

resource ehSenderIngest 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ehHubRef
  name: guid(ehHubResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', ingestIdName), ehDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', ehDataSenderRoleId)
    principalId: identities.outputs.ingestPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [eventhubs]
}

resource ehReceiverProcessing 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ehHubRef
  name: guid(ehHubResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', processingIdName), ehDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', ehDataReceiverRoleId)
    principalId: identities.outputs.processingPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [eventhubs]
}

resource ehReceiverPortal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ehHubRef
  name: guid(ehHubResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), ehDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', ehDataReceiverRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [eventhubs]
}

// ── Storage roles ─────────────────────────────────────────────────────────────
var storageResId = resourceId('Microsoft.Storage/storageAccounts', storageName)

resource storageContribProcessing 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageRef
  name: guid(storageResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', processingIdName), storageBlobContribRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobContribRoleId)
    principalId: identities.outputs.processingPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [storage]
}

resource storageReaderPortal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageRef
  name: guid(storageResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), storageBlobReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobReaderRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [storage]
}

// ── Key Vault Secrets User roles ──────────────────────────────────────────────
var kvResId = resourceId('Microsoft.KeyVault/vaults', kvName)

resource kvSecretsManagement 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kvRef
  name: guid(kvResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', managementIdName), kvSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: identities.outputs.managementPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [kv]
}

resource kvSecretsPortal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kvRef
  name: guid(kvResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), kvSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [kv]
}

resource kvSecretsReporting 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kvRef
  name: guid(kvResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', reportingIdName), kvSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: identities.outputs.reportingPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [kv]
}

resource kvSecretsMigrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kvRef
  name: guid(kvResId, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', migratorIdName), kvSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: identities.outputs.migratorPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [kv]
}

var kvSecretsOfficerRoleId      = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var kvCertificatesOfficerRoleId = 'a4417e6f-fecd-4de8-b567-7b0420556985'

resource kvSecretsOfficerDeployer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerObjectId)) {
  scope: kvRef
  name: guid(kvResId, deployerObjectId, kvSecretsOfficerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsOfficerRoleId)
    principalId: deployerObjectId
    principalType: 'User'
  }
  dependsOn: [kv]
}

resource kvCertificatesOfficerDeployer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerObjectId)) {
  scope: kvRef
  name: guid(kvResId, deployerObjectId, kvCertificatesOfficerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvCertificatesOfficerRoleId)
    principalId: deployerObjectId
    principalType: 'User'
  }
  dependsOn: [kv]
}

// ── Service Bus Data Owner for id-portal ──────────────────────────────────────
resource sbOwnerPortalCmd 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: cmdNsRef
  name: guid(resourceId('Microsoft.ServiceBus/namespaces', cmdSbNsName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), sbDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sbDataOwnerRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [sbcmd]
}

resource sbOwnerPortalSim 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: simNsRef
  name: guid(resourceId('Microsoft.ServiceBus/namespaces', simSbNsName), resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), sbDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sbDataOwnerRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [sbsim]
}

// ── Reader on resource group for id-portal ────────────────────────────────────
resource portalRgReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(resourceGroup().id, resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', portalIdName), readerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: identities.outputs.portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output ingestIdentityId string = identities.outputs.ingestId
output ingestClientId string = identities.outputs.ingestClientId
output processingIdentityId string = identities.outputs.processingId
output processingClientId string = identities.outputs.processingClientId
output managementIdentityId string = identities.outputs.managementId
output managementClientId string = identities.outputs.managementClientId
output portalIdentityId string = identities.outputs.portalId
output portalClientId string = identities.outputs.portalClientId
output reportingIdentityId string = identities.outputs.reportingId
output reportingClientId string = identities.outputs.reportingClientId
output migratorIdentityId string = identities.outputs.migratorId
output migratorClientId string = identities.outputs.migratorClientId
output migratorPrincipalId string = identities.outputs.migratorPrincipalId
output ingestPrincipalId string = identities.outputs.ingestPrincipalId
output processingPrincipalId string = identities.outputs.processingPrincipalId
output managementPrincipalId string = identities.outputs.managementPrincipalId
output portalPrincipalId string = identities.outputs.portalPrincipalId
output reportingPrincipalId string = identities.outputs.reportingPrincipalId

output appInsightsConnectionString string = observability.outputs.appInsightsConnectionString

output kvUri string = kv.outputs.kvUri
output kvName string = kv.outputs.kvName

output acrLoginServer string = acr.outputs.acrLoginServer

output storageAccountName string = storage.outputs.storageAccountName
output checkpointBlobContainerUrl string = storage.outputs.checkpointBlobContainerUrl

output ehFqdn string = eventhubs.outputs.ehFqdn

output cmdSbFqdn string = sbcmd.outputs.cmdSbFqdn
output simSbFqdn string = sbsim.outputs.simSbFqdn

output sqlConnectionString string = sql.outputs.sqlConnectionString

output caeId string = cae.outputs.caeId
output caeName string = cae.outputs.caeName
output caeDefaultDomain string = cae.outputs.caeDefaultDomain

output cmdSbIssuerKeySecretUri string = '${kv.outputs.kvUri}secrets/cmd-sb-issuer-key/'
output simSbIssuerKeySecretUri string = '${kv.outputs.kvUri}secrets/sim-sb-issuer-key/'
output portalJwtKeySecretUri string = '${kv.outputs.kvUri}secrets/portal-jwt-key/'
output reportingJwtKeySecretUri string = '${kv.outputs.kvUri}secrets/reporting-jwt-key/'
