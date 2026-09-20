param prefix string
param suffix string
param location string

resource ns 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sbcmd-${prefix}-${suffix}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource issuerRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  parent: ns
  name: 'collector-issuer'
  properties: {
    rights: [
      'Listen'
      'Send'
    ]
  }
}

output cmdSbNamespaceName string = ns.name
output cmdSbNamespaceId string = ns.id
output cmdSbFqdn string = '${ns.name}.servicebus.windows.net'
output cmdAuthRuleId string = issuerRule.id
