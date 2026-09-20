param prefix string
param suffix string
param location string

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${prefix}-${suffix}'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

output kvUri string = kv.properties.vaultUri
output kvName string = kv.name
output kvId string = kv.id
