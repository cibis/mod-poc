// DEMO SCAFFOLDING — simulator Service Bus namespace
param prefix string
param suffix string
param location string

resource ns 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sbsim-${prefix}-${suffix}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource simStatusQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: ns
  name: 'sim-status'
  properties: {
    defaultMessageTimeToLive: 'PT30S'
    lockDuration: 'PT30S'
  }
}

resource issuerRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  parent: ns
  name: 'sim-issuer'
  properties: {
    rights: [
      'Listen'
      'Send'
    ]
  }
}

output simSbNamespaceName string = ns.name
output simSbNamespaceId string = ns.id
output simSbFqdn string = '${ns.name}.servicebus.windows.net'
output simAuthRuleId string = issuerRule.id
