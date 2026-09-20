# Contract: Environment variables — `ca-processing` (platform: Mod.Processing)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

Also applies: `common.md` (production-space .NET services only; not collectors).

| Variable | Example / default | Notes |
|---|---|---|
| EVENTHUB_FQDN | | |
| EVENTHUB_NAME | `telemetry` | |
| EVENTHUB_CONSUMER_GROUP | `processing` | |
| CHECKPOINT_BLOB_CONTAINER_URL | `https://stmodpocx.blob.core.windows.net/checkpoints` | |
| WINDOW_GRACE_SECONDS | 120 | A minute window is closed at MinuteStart + 60 s + grace |
| DEDUPE_RETENTION_DAYS | 14 | |
| RAW_RETENTION_DAYS | 7 | |
| PROCESSING_DELAY_MS | 20 | DEMO KNOB: artificial delay per batch so backlog scaling is visible at small fleet sizes. 0 disables |
| MAPPING_CACHE_SECONDS | 30 | |
