param prefix string
param location string

resource ingest 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-ingest'
  location: location
}

resource processing 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-processing'
  location: location
}

resource management 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-management'
  location: location
}

resource portal 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-portal'
  location: location
}

resource reporting 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-reporting'
  location: location
}

resource migrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${prefix}-migrator'
  location: location
}

output ingestId string = ingest.id
output ingestClientId string = ingest.properties.clientId
output ingestPrincipalId string = ingest.properties.principalId

output processingId string = processing.id
output processingClientId string = processing.properties.clientId
output processingPrincipalId string = processing.properties.principalId

output managementId string = management.id
output managementClientId string = management.properties.clientId
output managementPrincipalId string = management.properties.principalId

output portalId string = portal.id
output portalClientId string = portal.properties.clientId
output portalPrincipalId string = portal.properties.principalId

output reportingId string = reporting.id
output reportingClientId string = reporting.properties.clientId
output reportingPrincipalId string = reporting.properties.principalId

output migratorId string = migrator.id
output migratorClientId string = migrator.properties.clientId
output migratorPrincipalId string = migrator.properties.principalId
