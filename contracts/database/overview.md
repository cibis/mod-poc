# Contract: Database — overview

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Owned by the platform module (`platform/src/Mod.DbMigrator`). Other modules never create or alter schema objects. Column types are SQL Server types. All timestamps are `datetime2(3)` UTC unless stated. GUIDs are `uniqueidentifier`. Three schemas: `registry` (control plane), `telemetry` (data plane), `sim` (demo scaffolding).

Files: `registry.md`, `telemetry.md`, `sim.md` (tables), `access.md` (roles, identities, row-level security). Read only the files your task needs.
