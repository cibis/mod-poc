# Portal

Admin portal and simulator for the MOD Cloud Platform PoC (`ca-portal`).

## PoC departures

| Area | Departure |
|---|---|
| Authentication | Local accounts in `registry.AppUser` (PasswordHasher v3) instead of Entra External ID. Passwords are provisioned at deployment and stored in Key Vault. |
| SignalR | In-process hub at `/hubs/sim` — no Azure SignalR Service. Single replica only. |
| Command requests | Unsigned HTTP requests forwarded to collectors; no message signing or HMAC verification on the collector side. |
| Tenancy | Pooled tenancy only. The Dedicated option is visible in the UI but shown as disabled. |
| Simulator | Service Bus channel (`SIM_SB_FQDN`) and collector container app provisioning in the controller resource group are **demo scaffolding** — they exist only to drive the PoC demonstration. |

## Local development

```sh
# From portal/
set -a; . ./.env.local; set +a
dotnet run --project api/src/Mod.PortalApi

# Angular dev server (separate terminal)
cd web && npx ng serve
```

The Angular dev server proxies `/api` and `/hubs` to `http://localhost:5100` via `proxy.conf.json`.

## Build

```sh
# Solution
dotnet build Mod.Portal.slnx

# Container image (from repo root)
az acr build --registry <acr> --image mod/portal:latest --file portal/Dockerfile portal/
```
