# Contract: Environment variables — `ca-ingest` (platform: Mod.IngestApi)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

Also applies: `common.md` (production-space .NET services only; not collectors).

| Variable | Example / default | Notes |
|---|---|---|
| EVENTHUB_FQDN | `evh-modpoc-x.servicebus.windows.net` | |
| EVENTHUB_NAME | `telemetry` | |
| COLLECTOR_CA_CERT_PEM_BASE64 | base64 of the CA public cert PEM | Set by infra after CA creation |
| INGEST_MAX_EVENTS | 500 | |
| INGEST_MAX_BYTES | 262144 | Uncompressed |
| INGEST_RATE_LIMIT_PER_COLLECTOR | 20 | Requests per second, token bucket |
| REGISTRY_CACHE_SECONDS | 30 | |
