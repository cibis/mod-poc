# Spec: Mod.DbMigrator — schema, security and seed

Part of the `platform` module. Module rules and layout: `platform/CLAUDE.md`.

Owns every schema object. Runs as a Container Apps job during deployment; uses the SQL server's Entra admin identity (`id-migrator`).

## Execution steps (in order, every run, all idempotent)

1. Run pending DbUp scripts in one transaction per script. Scripts are append-only: never edit a script that has shipped; add a new one.
2. **UsersStep:** for each service identity in the environment variables, create the contained user if missing with `CREATE USER [{name}] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '{objectId}'` (avoids Microsoft Graph lookups), then add it to its role. Do the same for each entry of `DEVELOPER_PRINCIPALS`, adding it to `db_datareader` and `db_datawriter`. Idempotent via `sys.database_principals` checks.
3. **SeedStep** (when `SEED_DEMO_DATA=true`): insert any missing rows of the demo dataset (see Seed data below).
4. Exit 0 on success, non-zero with a clear error otherwise.

## Seed data

Implement exactly the dataset in `contracts/demo-dataset.md` (content, deterministic IDs, insert-missing-only idempotency, `sim.CollectorState` rows). Hash passwords with `PasswordHasher` v3 using `ADMIN_PASSWORD` and `CUSTOMER_PASSWORD`. The seed does not create Service Bus queues or enrolment tokens.

## Implementation notes

- Add foreign keys where the tables above name a parent; `ON DELETE NO ACTION` everywhere.
- Use `CHECK` constraints for every enum-like column listed above.
- The RLS predicate function must be `WITH SCHEMABINDING` and inline table-valued. Apply both FILTER and BLOCK (AFTER INSERT, AFTER UPDATE) predicates.
- `telemetry.RawEvent` uses a clustered columnstore index; do not add a primary key constraint to it.
- Grant `EXECUTE` on `sys.sp_set_session_context` is implicit; no extra grant needed.

