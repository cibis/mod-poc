param prefix string
param suffix string
param location string

resource ns 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: 'evhns-${prefix}-${suffix}'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    isAutoInflateEnabled: true
    maximumThroughputUnits: 2
  }
}

resource hub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: ns
  name: 'telemetry'
  properties: {
    partitionCount: 8
    messageRetentionInDays: 1
  }
}

resource processingCg 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2024-01-01' = {
  parent: hub
  name: 'processing'
}

output ehNamespaceName string = ns.name
output ehNamespaceId string = ns.id
output ehHubId string = hub.id
output ehFqdn string = '${ns.name}.servicebus.windows.net'
