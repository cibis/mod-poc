@minLength(1)
param prefix string
@minLength(2)
param suffix string
param location string

resource sa 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${prefix}${suffix}'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: sa
  name: 'default'
}

resource checkpoints 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'checkpoints'
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountName string = sa.name
output storageId string = sa.id
output checkpointBlobContainerUrl string = '${sa.properties.primaryEndpoints.blob}checkpoints'
