# platform — database, ingest-api, management-api, processing

Cloud data path for the MOD PoC. Four deployables built from one solution.

| Project | Container app | Role |
|---|---|---|
| `Mod.Platform.Common` | — | Shared library (no deployable) |
| `Mod.DbMigrator` | `job-dbmigrate` | Applies the full Azure SQL schema, roles, RLS and seed data |
| `Mod.IngestApi` | `ca-ingest` | Authenticates collectors, validates envelopes, writes to Event Hubs |
| `Mod.ManagementApi` | `ca-mgmt` | Collector enrolment, config, health, certificate renewal, command-channel tokens |
| `Mod.Processing` | `ca-processing` | Event Hubs consumer: de-duplicate, validate, attribute, rollup, dead-letter, replay |

## Build

```
dotnet build Mod.Platform.slnx
```

## Container images

Each image is built with `az acr build` using `platform/` as the build context:

```
az acr build -t mod/ingest-api     -f docker/ingest-api.Dockerfile     .
az acr build -t mod/management-api -f docker/management-api.Dockerfile  .
az acr build -t mod/processing     -f docker/processing.Dockerfile      .
az acr build -t mod/db-migrator    -f docker/db-migrator.Dockerfile     .
```

All images run as a non-root system user on port 8080. The migrator exits with a non-zero code on failure.

## Local environment

Copy the relevant example file, fill in the values, and source before running:

```
cp .env.ingest.example     .env.ingest.local
cp .env.management.example .env.management.local
cp .env.processing.example .env.processing.local
cp .env.migrator.example   .env.migrator.local
```

Local runs authenticate to Azure with `DefaultAzureCredential` (your `az login` session).
The management-api additionally accepts `CA_PFX_PATH` to load the CA from a local PFX file instead of Key Vault.

## Departures from production architecture

The following are intentional PoC simplifications or demo-only features. They must not be carried into a production system without re-evaluation.

| Departure | Detail |
|---|---|
| **SQL columnstore raw store** | Raw telemetry events are stored in an Azure SQL columnstore index rather than Azure Data Lake Storage Gen2 (ADLS). Simplifies the PoC; production would land raw events in ADLS and use Synapse or Fabric for analytics. |
| **No ADLS archive** | No cold-path archive to ADLS. All data lives in Azure SQL. |
| **Demo processing delay knob** | *Demo scaffolding.* `Mod.Processing` exposes a configurable artificial delay (`PROCESSING_DELAY_MS`) to let the simulator UI demonstrate end-to-end latency. Remove before production. |
| **No Front Door** | Ingress is directly to Azure Container Apps without Azure Front Door or a WAF. A production deployment would place Front Door in front of the ingest and management APIs. |
| **Public ingress** | Both `ca-ingest` and `ca-mgmt` are exposed on public Container Apps ingress. A production system would restrict to private endpoints or VNET integration. |
| **Exportable PoC CA key** | *Demo scaffolding.* The collector CA private key is stored in Key Vault with `exportable: true` to support the `CA_PFX_PATH` development path. Production CAs must use non-exportable HSM-backed keys. |
| **Unsigned command requests** | The management-api issues command-channel messages without a request signature. Production commands must be signed so the collector can verify origin. |
| **Local accounts** | The portal and customer APIs use local username/password accounts (PBKDF2-hashed, stored in `access.LocalUser`). Production would use Azure AD / Entra ID. |
| **Pooled-only tenancy** | All tenants share the same Azure SQL database with row-level security. The architecture supports isolated (per-tenant database) and pooled tenancy; only pooled is implemented here. |
