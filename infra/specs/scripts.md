# Spec: scripts (`scripts/*.sh`)

Part of the `infra` module. Module rules and layout: `infra/CLAUDE.md`.

## Inputs

Only `--prefix` (default `modpoc`, 3–8 lowercase letters/digits). Optional flags with defaults: `--production-location westeurope`, `--controller-location northeurope` (must differ), `--yes` (skip confirmation), `--developer <upn>` (repeatable; default: the signed-in user; `--no-developer` disables). A unique suffix = first 5 chars of `uniqueString(subscriptionId, prefix)`.

## up.sh sequence

1. Check `az` login, Bicep, `jq`; resolve subscription; register providers `Microsoft.App`, `Microsoft.EventHub`, `Microsoft.ServiceBus`, `Microsoft.Sql`, `Microsoft.KeyVault`, `Microsoft.ContainerRegistry`, `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.Storage`.
2. Print what will be created (both resource groups, regions, SKUs); ask for confirmation unless `--yes`.
3. Create both resource groups (idempotent).
4. Deploy `production/core.bicep`.
5. Create Key Vault secrets only if absent: `portal-jwt-key`, `reporting-jwt-key` (32 random bytes, base64), `admin-password`, `customer-password` (20 random chars). The deploying user gets Key Vault Secrets Officer on the vault for this step.
6. Create Key Vault certificate `collector-ca` if absent: self-signed, EC P-256, subject `CN=MOD PoC Collector CA`, validity 24 months, exportable, key usage `keyCertSign, cRLSign, digitalSignature`, basic constraints CA true. Read its public PEM for `COLLECTOR_CA_CERT_PEM_BASE64`.
7. Build images with `az acr build`, tag = `git rev-parse --short HEAD` if available, else a UTC timestamp. Every build uses its module folder as context:
   - `platform/` with `--file docker/ingest-api.Dockerfile` → `mod/ingest-api`; `docker/management-api.Dockerfile` → `mod/management-api`; `docker/processing.Dockerfile` → `mod/processing`; `docker/db-migrator.Dockerfile` → `mod/db-migrator`
   - `edge/` → `mod/collector`
   - `portal/` → `mod/portal`
   - `customer/` → `mod/customer`
   - Run the builds in parallel and wait for all.
8. Deploy `controller/main.bicep`.
9. Deploy `production/apps.bicep`.
10. Start `job-dbmigrate`, wait for completion, fail loudly with its logs on failure.
11. Write non-secret outputs (resource names, URLs, FQDNs, identity client ids) to `infra/.state/{prefix}.json` (git-ignored) for `local-env.sh` and later runs.
12. Print the ready-to-demo summary from `contracts/demo-dataset.md`: admin portal URL, customer portal URL, the `az keyvault secret show` commands for `admin-password` and `customer-password`, the seeded user names, the seeded tenants with their collector counts per region, a one-line "next step: Admin portal → Simulator → Fleet → Power on all" and the down command.

Re-running `up.sh` updates in place and rebuilds images with the new tag.

## Developer access (`--developer`)

For each developer UPN, `up.sh` resolves the object id (`az ad user show`) and grants:
- Azure roles: Key Vault Secrets User; Azure Event Hubs Data Sender and Data Receiver; Azure Service Bus Data Owner on both namespaces; Storage Blob Data Contributor; Reader on `rg-{prefix}-prod`; Contributor on `rg-{prefix}-ctrl` (so a locally run portal can provision simulated collectors).
- Database access: passes `name:objectId` pairs to the migrator as `DEVELOPER_PRINCIPALS`.
No keys, client secrets or service principals are created. Role assignments are idempotent.

## local-env.sh

`local-env.sh --prefix <p>` prepares local runs against a deployed environment:
- Reads `infra/.state/{prefix}.json` (or queries Azure if missing).
- Adds a SQL firewall rule `dev-{upn-hash}` for the caller's current public IP (replaced on each run).
- Writes git-ignored files with every variable from the matching `contracts/environment/` file, using local-development rules (no `AZURE_CLIENT_ID`; `SQL_CONNECTION` with `Authentication=Active Directory Default`): `platform/.env.processing.local`, `portal/.env.local`, `customer/.env.local`. Secret values are fetched from Key Vault with the caller's identity and written only to these files, never printed.
- Refuses to write if the target path is not ignored by git (`git check-ignore`).
- Prints the run commands from `SETUP.md`.

## down.sh

Confirm unless `--yes`. Delete `rg-{prefix}-ctrl` (this removes all simulator-created collector apps) and `rg-{prefix}-prod`, wait, then purge the soft-deleted Key Vault. Safe to run twice (missing groups are not an error). Optional `--controller-only` / `--production-only`.

## stop.sh / start.sh (cold stop)

- stop: set min replicas to 0 on every production app, delete all `col-*` apps in the controller group (the simulator reconciler marks them Off on next start), leave SQL to auto-pause. Report what remains billable (Event Hubs TU, registry, Key Vault, storage).
- start: restore min replicas from `apps.bicep` by redeploying it with the current tag.
- Record measured restart time in the README (PoC measurement 1).

## Secrets in scripts

No script writes a secret to a tracked file or to standard output. Generated secrets go straight to Key Vault; local copies only to git-ignored `.env*.local` files. `set -x` is never enabled around commands that handle secrets.
