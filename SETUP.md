# SETUP — compile and run the MOD PoC

For developers who have the source code and want to build it and see it running. If you are building the PoC from the specifications with Claude Code, use `BUILD.md` instead.

Claude Code does not need to read this file.

---

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| git | any recent | `git --version` |
| .NET SDK | 10.x | `dotnet --version` |
| Node.js + npm | 22 LTS | `node --version` |
| Azure CLI with Bicep | 2.60 or later | `az version`, `az bicep version` |
| bash, jq, curl | — | Linux, macOS, WSL or Azure Cloud Shell |

Docker is **not** needed: container images are built in Azure.

You also need an Azure subscription where you are **Owner**, or **Contributor + User Access Administrator**.

## 2. Get the code

```
git clone <repository-url> mod-poc
cd mod-poc
```

## 3. Compile everything

```
dotnet build platform/Mod.Platform.slnx
dotnet build edge/Mod.Edge.slnx
dotnet build portal/Mod.Portal.slnx
dotnet build customer/Mod.Customer.slnx
(cd portal/web   && npm ci && npx ng build)
(cd customer/web && npm ci && npx ng build)
(cd infra && for f in production/core.bicep production/apps.bicep controller/main.bicep; do az bicep build --file "$f"; done)
```

A successful build of all seven commands is the "it compiles" check. Everything below needs Azure.

## 4. Set up access to Azure

No keys, client secrets or service principals are used. Your `az login` session is the only credential on your machine; the deployed services use managed identities; platform secrets are generated into Key Vault.

```
az login
az account set --subscription "<subscription name or id>"
az account show --query "{name:name, user:user.name}" --output table
```

## 5. Run it — full environment in Azure (recommended)

```
infra/scripts/up.sh --prefix <3-8 lowercase letters, e.g. modjd>
```

- Takes roughly 20–30 minutes the first time (images are built in Azure).
- Creates `rg-<prefix>-prod` and `rg-<prefix>-ctrl`, deploys everything, seeds the demo data and grants **you** developer access (`--developer`, default: the signed-in user).
- Ends with a summary: the admin portal URL, the customer portal URL, the user names, and the commands to read the passwords, e.g.

```
az keyvault secret show --vault-name <vault> --name admin-password    --query value -o tsv
az keyvault secret show --vault-name <vault> --name customer-password --query value -o tsv
```

Then:

1. Open the **admin portal** URL and sign in as `admin`.
2. Go to **Simulator → Fleet → Power on all**. Collectors enrol and start sending within about a minute.
3. Open the **customer portal** URL and sign in as `customer-a-viewer`, `customer-b-viewer` or `customer-c-viewer`.

Stop and restart between sessions, or remove everything:

```
infra/scripts/stop.sh  --prefix <prefix>
infra/scripts/start.sh --prefix <prefix>
infra/scripts/down.sh  --prefix <prefix>
```

## 6. Run a module locally (against your Azure environment)

Requires step 5 first. The processing worker, the portal and the customer app can run on your machine; they use the deployed Event Hubs, Service Bus, SQL and Key Vault through your `az login` session.

**Generate the local environment files** (git-ignored; re-run when your public IP changes, because it also updates the SQL firewall rule for your IP):

```
infra/scripts/local-env.sh --prefix <prefix>
```

This writes `platform/.env.processing.local`, `portal/.env.local` and `customer/.env.local`. They contain secrets fetched from Key Vault: never commit them, copy them or paste them into chats.

**Processing worker**

```
cd platform
set -a && . ./.env.processing.local && set +a
dotnet run --project src/Mod.Processing
```

**Admin portal and simulator** (two terminals)

```
cd portal
set -a && . ./.env.local && set +a
dotnet run --project api/src/Mod.PortalApi --urls http://localhost:5100
```
```
cd portal/web
npx ng serve --proxy-config proxy.conf.json      # open http://localhost:4200
```

**Customer portal** (two terminals)

```
cd customer
set -a && . ./.env.local && set +a
dotnet run --project api/src/Mod.ReportingApi --urls http://localhost:5200
```
```
cd customer/web
npx ng serve --proxy-config proxy.conf.json --port 4201   # open http://localhost:4201
```

**Not runnable locally:** the ingest API, the management API and the collectors. They depend on Azure Container Apps forwarding client certificates, so they run only in Azure. A locally run portal can still power collectors on and off in Azure.

Notes:
- A locally running processing worker competes with the deployed one for Event Hubs partitions; stop the deployed one first if you want all traffic locally: `az containerapp update -n ca-processing -g rg-<prefix>-prod --min-replicas 0 --max-replicas 0`, and restore it with `infra/scripts/start.sh`.
- Your developer database login is not subject to row-level security, so tenant isolation is only demonstrated by the deployed customer app.

## 7. Secrets and git hygiene

- `.gitignore` excludes `.env*` (except `*.example`), `*.pfx`, `*.pem`, `*.key`, `secrets/` and `infra/.state/`. Keep it that way.
- The committed `.env.example` files list variable names only.
- Run `git status` before every commit; never force-add ignored files.
- If a secret is ever committed: rotate it in Key Vault (delete the secret and re-run `up.sh`, which regenerates missing secrets), then remove it from git history.

## 8. Troubleshooting

| Symptom | Fix |
|---|---|
| `up.sh` fails creating role assignments | You need Owner, or Contributor + User Access Administrator, on the subscription |
| Local app cannot reach SQL | Your IP changed: re-run `local-env.sh` |
| `DefaultAzureCredential` errors locally | Run `az login` again; check `az account show` points at the right subscription |
| Key Vault name conflict after `down.sh` | `down.sh` purges the vault; if it was interrupted, run it again |
| Collectors stay "Provisioning" | Check the controller resource group exists and `up.sh` finished; the simulator reconciler retries every 60 s |
