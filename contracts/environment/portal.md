# Contract: Environment variables — `ca-portal` (portal)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

Also applies: `common.md` (production-space .NET services only; not collectors).

| Variable | Example / default | Notes |
|---|---|---|
| KEYVAULT_URI | | |
| PORTAL_JWT_KEY | secret (≥ 32 bytes, base64) | From Key Vault secret `portal-jwt-key` |
| CMD_SB_FQDN | | Product command namespace (managed identity) |
| SIM_SB_FQDN | | Simulator namespace (managed identity for queue management and receiving `sim-status`) |
| SIM_SB_SAS_KEY_NAME | `sim-issuer` | |
| SIM_SB_SAS_KEY | secret | From Key Vault secret `sim-sb-issuer-key`; used only to mint collector tokens |
| EVENTHUB_FQDN, EVENTHUB_NAME | | Partition properties for lag metrics (Event Hubs Data Receiver) |
| EVENTHUB_CONSUMER_GROUP | `processing` | Checkpoints to compare against |
| CHECKPOINT_BLOB_CONTAINER_URL | | Read-only |
| PRODUCTION_SUBSCRIPTION_ID, PRODUCTION_RESOURCE_GROUP | | ARM reads (replicas, metrics) |
| INGEST_APP_NAME, PROCESSING_APP_NAME, MANAGEMENT_APP_NAME, PORTAL_APP_NAME, REPORTING_APP_NAME | `ca-ingest`, `ca-processing`, `ca-mgmt`, `ca-portal`, `ca-reporting` | Running replica counts shown by the simulator |
| CONTROLLER_SUBSCRIPTION_ID, CONTROLLER_RESOURCE_GROUP | | Where collector container apps are created |
| CONTROLLER_ENVIRONMENT_ID | resource id | Controller-space Container Apps environment |
| CONTROLLER_LOCATION | `northeurope` | |
| COLLECTOR_IMAGE | `acrmodpocx.azurecr.io/mod/collector:{tag}` | |
| COLLECTOR_PULL_IDENTITY_ID | resource id | User-assigned identity with AcrPull, lives in controller RG |
| MANAGEMENT_URL | | Passed to collectors |
