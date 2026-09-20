param prefix string = 'modpoc'
@minLength(2)
@maxLength(8)
param suffix string
param location string = resourceGroup().location
param productionResourceGroup string
param portalPrincipalId string

var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'
var acrName           = 'acr${prefix}${suffix}'

// ── Log Analytics workspace ───────────────────────────────────────────────────
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'law-${prefix}-${suffix}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

// ── Container Apps environment ────────────────────────────────────────────────
module cae '../modules/cae.bicep' = {
  name: 'cae'
  params: {
    prefix: prefix
    suffix: suffix
    location: location
    workspaceId: workspace.id
  }
}

// ── Collector pull identity ───────────────────────────────────────────────────
resource collectorPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-collector-pull'
  location: location
}

// ── AcrPull on production registry (cross-RG) ────────────────────────────────
module collectorPullAcrRole '../modules/acr-pull.bicep' = {
  name: 'collector-pull-acr-role'
  scope: resourceGroup(productionResourceGroup)
  params: {
    acrName: acrName
    principalId: collectorPullIdentity.properties.principalId
  }
}

// ── Contributor on controller RG for id-portal ────────────────────────────────
resource portalContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: resourceGroup()
  name: guid(resourceGroup().id, portalPrincipalId, contributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
    principalId: portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output caeId string = cae.outputs.caeId
output caeName string = cae.outputs.caeName
output caeDefaultDomain string = cae.outputs.caeDefaultDomain
output collectorPullIdentityId string = collectorPullIdentity.id
output collectorPullClientId string = collectorPullIdentity.properties.clientId
