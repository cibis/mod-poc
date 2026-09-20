param prefix string
param suffix string
param location string
param migratorPrincipalId string

resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${prefix}-${suffix}'
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: 'id-${prefix}-migrator'
      sid: migratorPrincipalId
      tenantId: subscription().tenantId
    }
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: 'mod'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    autoPauseDelay: 60
    minCapacity: json('0.5')
  }
}

output sqlServerFqdn string = server.properties.fullyQualifiedDomainName
output sqlServerName string = server.name
output sqlConnectionString string = 'Server=tcp:${server.properties.fullyQualifiedDomainName},1433;Initial Catalog=mod;Authentication=Active Directory Managed Identity;Encrypt=True;'
