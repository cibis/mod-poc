# Module: platform — database, ingest-api, management-api, processing

The cloud data path and the owner of the database schema. One .NET solution, four deployables:

| Project | Deploys as | Role |
|---|---|---|
| `Mod.DbMigrator` | job `job-dbmigrate` | Owns and applies the whole Azure SQL schema, security and seed |
| `Mod.IngestApi` | `ca-ingest` | Thin ingest API: authenticate collector, validate envelope, append to Event Hubs, acknowledge |
| `Mod.ManagementApi` | `ca-mgmt` | Collector-facing control endpoint: enrolment, config, health, renewal, command-channel tokens |
| `Mod.Processing` | `ca-processing` | Event Hubs consumer: de-duplicate, validate, attribute, gaps, raw store, rollups, dead letters, replay |

This module never initiates a connection to a site and has no knowledge of the simulator (it is denied access to the `sim` schema). It does not create tenants, sites, collectors, enrolment tokens or queues — the portal module does.

## How to work in this module

This file is deliberately short. The detailed specification is split by task: **read only the spec and contract files listed for the task you are doing**, and nothing else outside this module. One task per session; `/clear` between tasks.

| Task | Spec files (this module) | Shared files (under `/contracts/` unless shown otherwise) |
|---|---|---|
| Common library (`Mod.Platform.Common`) | `specs/eventhub-message.md` | `canonical-event.md`, `ingest-api.md` (envelope section), `collector-certificates.md` |
| Database migrator (`Mod.DbMigrator`) | `specs/database.md` | `database/*` (all five), `demo-dataset.md`, `browser-auth.md` (password format only), `environment/migrator.md`, `images.md` |
| Ingest API (`Mod.IngestApi`) | `specs/ingest-api.md`, `specs/eventhub-message.md` | `canonical-event.md`, `ingest-api.md`, `collector-certificates.md`, `database/overview.md`, `database/registry.md` (Collector, Certificate), `database/access.md`, `environment/common.md`, `environment/ingest.md`, `images.md` |
| Management API (`Mod.ManagementApi`) | `specs/management-api.md` | `management-api.md`, `collector-certificates.md`, `command-channel.md`, `database/overview.md`, `database/registry.md`, `database/telemetry.md` (Reconciliation), `database/access.md`, `environment/common.md`, `environment/management.md`, `images.md` |
| Processing (`Mod.Processing`) | `specs/processing.md`, `specs/eventhub-message.md` | `canonical-event.md`, `dead-letter-reason-codes.md`, `database/overview.md`, `database/registry.md` (read), `database/telemetry.md`, `database/access.md`, `environment/common.md`, `environment/processing.md`, `images.md` |
| Dockerfiles, solution, README | this file | `images.md` |

Recommended order: Common → DbMigrator → IngestApi → ManagementApi → Processing.

## Technology

- .NET 10. ASP.NET Core minimal APIs (ingest, management), worker service with a `/healthz` endpoint (processing), console app (migrator).
- `Azure.Messaging.EventHubs` (producer in ingest), `Azure.Messaging.EventHubs.Processor` + `Azure.Storage.Blobs` (processing, blob checkpoint store), `Azure.Security.KeyVault.Secrets` (management, CA), `Azure.Identity`.
- `Microsoft.Data.SqlClient` + Dapper; `SqlBulkCopy` and table-valued parameters/`MERGE` in processing.
- DbUp (`dbup-sqlserver`) with embedded scripts; `Microsoft.Extensions.Identity.Core` `PasswordHasher` (v3) in the migrator.
- ASP.NET Core rate limiter (ingest); `Microsoft.Extensions.Resilience` (processing SQL retries, circuit breaker); `IMemoryCache` for registry lookups (≤ 30 s).

## Layout

```
platform/
  CLAUDE.md
  Mod.Platform.slnx
  docker/                         ingest-api.Dockerfile, management-api.Dockerfile,
                                  processing.Dockerfile, db-migrator.Dockerfile (context = platform/)
  src/
    Mod.Platform.Common/          shared inside this module only:
                                  canonical event + batch DTOs, Event Hubs property names,
                                  client-certificate parsing and validation, registry readers,
                                  SQL connection factory, problem-details helpers
    Mod.DbMigrator/               Program.cs, Scripts/0001_….sql …, Steps/UsersStep, SeedStep
    Mod.IngestApi/                Endpoints/BatchEndpoint, Validation/EnvelopeValidator, EventHub/BatchPublisher
    Mod.ManagementApi/            Endpoints/*, Pki/CertificateAuthority, Config/ConfigDocumentBuilder,
                                  ServiceBus/SasTokenFactory, Housekeeping/HealthHistoryPurge
    Mod.Processing/               Hosting/ProcessorHost, Pipeline/*, Replay/DeadLetterReplayService,
                                  Housekeeping/RetentionService
  README.md                       includes the departures this module implements
```

Implement in this order: Common and DbMigrator, then IngestApi, ManagementApi, Processing.

## Done when

- `dotnet build` passes on `Mod.Platform.slnx`.
- Each Dockerfile builds with `az acr build` using `platform/` as context.
- Each service runs locally with `DefaultAzureCredential` against real Azure resources when env vars are set; management-api supports a development-only `CA_PFX_PATH` for a file-based CA.
- README lists departures: SQL columnstore raw store, no ADLS archive, demo processing delay knob, no Front Door, public ingress, exportable PoC CA key, unsigned command requests, local accounts, Pooled-only tenancy.
- `.env.ingest.example`, `.env.management.example`, `.env.processing.example`, `.env.migrator.example` committed at the module root, listing every variable of the matching `contracts/environment/` file with empty values. No secret in any tracked file.
