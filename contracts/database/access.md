# Contract: Database — access (roles, identities, row-level security)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Conventions: see `overview.md` (types, timestamps, GUIDs).

Each service connects with its own user-assigned managed identity, mapped to a contained database user and one role. Connection strings use `Authentication=Active Directory Managed Identity; User Id={client id}`.

| Role | Identity | Rights |
|---|---|---|
| role_ingest | id-ingest | SELECT `registry.Collector`, `registry.Certificate` |
| role_processing | id-processing | SELECT `registry.Collector`, `registry.Site`, `registry.Asset`, `registry.AssetSourceMapping`; SELECT, INSERT, UPDATE, DELETE on schema `telemetry` |
| role_management | id-management | SELECT on schema `registry`; INSERT/UPDATE `registry.Certificate`, `registry.CollectorHealth`, `registry.CollectorHealthHistory` (plus DELETE for purge); UPDATE `registry.Collector`, `registry.EnrolmentToken`; INSERT `registry.AuditLog`; SELECT, INSERT, UPDATE `telemetry.Reconciliation` |
| role_portal | id-portal | Full DML on schemas `registry` and `sim`; SELECT on schema `telemetry`; UPDATE `telemetry.DeadLetter` |
| role_reporting | id-reporting | SELECT `registry.Tenant`, `registry.Site`, `registry.Line`, `registry.Asset`, `registry.AppUser`; SELECT `telemetry.RollupMinute`, `telemetry.CollectorFreshness`, `telemetry.Reconciliation`, `telemetry.SequenceGap` |
| db owner | id-migrator (SQL Entra admin) | Schema migrations, users, seed |
| developers (optional) | Entra users in `DEVELOPER_PRINCIPALS` | `db_datareader` + `db_datawriter` for local development. Not members of any `role_*`, so RLS does not filter them: tenant isolation is only demonstrated by the deployed apps |

- **Explicit DENY on schema `sim`** for role_ingest, role_processing, role_management and role_reporting. This structurally prevents platform logic from reading simulator data.
- **Row-level security:** security policy `security.TenantPolicy` with predicate function `security.fn_TenantPredicate(@TenantId)` on every `registry` and `telemetry` table that has `TenantId`, **except `registry.AppUser`** (filter and block predicates). The predicate returns true when the caller is **not** a member of role_reporting, or when `@TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)`. Reporting-api must call `sp_set_session_context @key=N'TenantId', @value=…, @read_only=1` on every opened connection before any query.
