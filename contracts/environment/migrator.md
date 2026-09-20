# Contract: Environment variables — `job-dbmigrate` (platform: Mod.DbMigrator)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

Also applies: `common.md` (production-space .NET services only; not collectors).

| Variable | Example / default | Notes |
|---|---|---|
| SQL_CONNECTION | | As Entra admin (id-migrator) |
| AZURE_CLIENT_ID | | id-migrator client id |
| IDENTITY_INGEST_NAME / _OBJECT_ID | `id-modpoc-ingest` / GUID | One pair per service identity: INGEST, PROCESSING, MANAGEMENT, PORTAL, REPORTING |
| DEVELOPER_PRINCIPALS | `dev1@example.com:GUID;…` | Optional. `name:objectId` pairs of developers granted local-development database access |
| SEED_DEMO_DATA | `true` | |
| ADMIN_PASSWORD | secret | Key Vault `admin-password` |
| CUSTOMER_PASSWORD | secret | Key Vault `customer-password` (one shared password for all seeded customer users) |
