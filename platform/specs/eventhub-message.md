# Internal contract: Event Hubs telemetry message (ingest → processing)

Internal to the `platform` module: implement once in `Mod.Platform.Common` and use from both sides.

- Event Hubs namespace (Standard). Event hub `telemetry`, 8 partitions, retention 24 h. Consumer group `processing`.
- One Event Hubs event per accepted HTTP batch.
- Partition key: collectorId (lowercase GUID string).
- Body: gzip-compressed UTF-8 JSON of the batch envelope as received.
- Application properties (all string values):

| Property | Value |
|---|---|
| mod.collectorId | From the certificate (authoritative) |
| mod.tenantId | From the registry |
| mod.siteId | From the registry |
| mod.batchId | From the envelope |
| mod.receivedAt | Ingest time, ISO-8601 UTC |
| mod.schemaVersion | `"1.0"` |
| mod.eventCount | Decimal string |
| mod.contentEncoding | `"gzip"` |
| mod.certThumbprint | SHA-256 thumbprint, lowercase hex |

- Consumers attribute tenant and site from these properties, never from the body.
- Checkpoints: blob container `checkpoints` in the production storage account, using the standard Azure.Messaging.EventHubs.Processor blob checkpoint layout (the KEDA `azure-eventhub` scaler reads it with `checkpointStrategy: blobMetadata`).
