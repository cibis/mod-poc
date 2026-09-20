# Spec: production space (`production/core.bicep`, `production/apps.bicep`)

Part of the `infra` module. Module rules and layout: `infra/CLAUDE.md`.

`core.bicep`:
- Log Analytics workspace (PerGB2018, 30-day retention, 1 GB daily cap) and workspace-based Application Insights.
- Key Vault (RBAC authorisation, soft delete on, purge protection **off** so `down.sh` can purge — departure).
- Container Registry Basic, admin user disabled.
- Storage account Standard_LRS, container `checkpoints`.
- Event Hubs namespace Standard, 1 TU, auto-inflate on (max 2); event hub `telemetry` (8 partitions, 1-day retention); consumer group `processing`.
- Service Bus namespace **cmd** (Basic) with SAS rule `collector-issuer` (Listen, Send); key written to Key Vault secret `cmd-sb-issuer-key` via `listKeys`.
- Service Bus namespace **sim** (Basic) — demo scaffolding — with queue `sim-status` (TTL 30 s) and SAS rule `sim-issuer` (Listen, Send); key → Key Vault secret `sim-sb-issuer-key`.
- Azure SQL logical server (Entra-only authentication; Entra admin = `id-migrator`; public network access on with "Allow Azure services" rule — departure) and database `mod`: serverless General Purpose Gen5, max 2 vCores, min 0.5, auto-pause 60 min.
- User-assigned identities: `id-ingest`, `id-processing`, `id-management`, `id-portal`, `id-reporting`, `id-migrator`.
- Role assignments (least privilege):

| Identity | Roles |
|---|---|
| all six | AcrPull on the registry |
| id-ingest | Azure Event Hubs Data Sender (event hub) |
| id-processing | Azure Event Hubs Data Receiver (event hub); Storage Blob Data Contributor (storage) |
| id-management | Key Vault Secrets User |
| id-portal | Key Vault Secrets User; Azure Service Bus Data Owner (cmd and sim namespaces); Azure Event Hubs Data Receiver; Storage Blob Data Reader; Reader on `rg-{prefix}-prod` |
| id-reporting | Key Vault Secrets User |
| id-migrator | Key Vault Secrets User |
| developers (from `up.sh --developer`) | Assigned by `up.sh`, not Bicep: see `specs/scripts.md` (Developer access) |

- Container Apps environment (Consumption workload profile, not zone-redundant) linked to the workspace.

`apps.bicep` (parameters: image tag, outputs of core and controller):

| App | Identity | Ingress | Scale | Resources |
|---|---|---|---|---|
| ca-ingest | id-ingest | external, `clientCertificateMode: require` | min 1, max 4, HTTP 50 concurrent | 0.5 vCPU / 1 GiB |
| ca-processing | id-processing | none | min 0, max 8, KEDA `azure-eventhub` (below) | 0.5 / 1 |
| ca-mgmt | id-management | external, `clientCertificateMode: accept` | min 1, max 2 | 0.25 / 0.5 |
| ca-portal | id-portal | external | min 1, max 1 (single replica required) | 0.5 / 1 |
| ca-reporting | id-reporting | external | min 0, max 1 (single replica required) | 0.25 / 0.5 |
| job-dbmigrate | id-migrator | none | manual trigger, 1 replica, timeout 10 min | 0.5 / 1 |

- KEDA scale rule for ca-processing: type `azure-eventhub`, metadata `eventHubNamespace`, `eventHubName: telemetry`, `consumerGroup: processing`, `unprocessedEventThreshold: 64`, `checkpointStrategy: blobMetadata`, `blobContainer: checkpoints`, `storageAccountName`; authenticate with the `id-processing` managed identity (Container Apps scale-rule identity). Confirm the metadata names against current Container Apps/KEDA documentation.
- Secrets in container apps are Key Vault references (`keyVaultUrl` + identity), never literal values.
- Health probes per `contracts/images.md`.
