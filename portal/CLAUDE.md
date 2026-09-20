# Module: portal — MOD administration portal and simulator (API + Angular)

One deployable (`ca-portal`) that serves, under one login and one authorisation model:

1. **Administration** — the control plane: tenants and hierarchy, collector registration, configuration and mappings, enrolment tokens, revocation, product command queues and the command dispatcher, dead-letter replay requests, restatements, audit.
2. **Simulator (DEMO SCAFFOLDING)** — power registered collectors on and off (provisioning container apps in controller space), send simulator commands, run unattended scenarios, compute live metrics and topology, and show them live.
3. **The Angular admin app** (admin screens and the simulator area), served as static files from the API's `wwwroot` on the same origin.

This module writes the `registry` and `sim` schemas and reads `telemetry`; it does not own the schema (the `platform` module does).

## How to work in this module

This file is deliberately short. **Read only the spec and contract files listed for the task you are doing**, and nothing else outside this module. One task per session; `/clear` between tasks.

| Task | Spec files (this module) | Shared files (under `/contracts/` unless shown otherwise) |
|---|---|---|
| API host: Program.cs, auth, JWT, static files, Dockerfile | this file | `browser-auth.md`, `environment/common.md`, `environment/portal.md`, `images.md` |
| Admin API (`Features/Admin`) | `specs/admin-services.md`, `specs/admin-api.md`, `specs/admin-http.md` | `database/overview.md`, `database/registry.md`, `database/telemetry.md`, `database/access.md`, `command-channel.md`, `dead-letter-reason-codes.md`, `demo-dataset.md` (queue reconciler) |
| Admin UI (`web/src/app/core`, `features/`) | `specs/admin-ui.md`, `specs/admin-http.md` | `browser-auth.md`, `dead-letter-reason-codes.md`, `/conventions/angular.md` |
| Simulator API (`Features/Simulation`) | `specs/admin-services.md`, `specs/simulator-api.md`, `specs/simulator-http.md` | `simulator-channel.md`, `database/overview.md`, `database/sim.md`, `database/telemetry.md` (read), `environment/portal.md`, `environment/collector.md`, `images.md` (edge row), `demo-dataset.md` (scenario defaults) |
| Simulator UI (`web/src/app/simulation`) | `specs/simulator-ui.md`, `specs/simulator-http.md` | `browser-auth.md`, `/conventions/angular.md` |

Recommended order: API host → Admin API → Admin UI → Simulator API → Simulator UI.

**Internal boundary:** Admin and Simulation are separate feature folders in both API and UI. Simulation may depend on Admin only through the Admin service interfaces (API) and the `core` folder (UI). Admin never depends on Simulation. The simulator's Service Bus clients, credentials and classes live only in `Features/Simulation` and are never shared with the product command channel code.

## Technology

**API**
- .NET 10 ASP.NET Core minimal APIs grouped per feature (`MapGroup`), JWT bearer auth, in-process SignalR hub `/hubs/sim` (single replica; no Azure SignalR Service).
- Dapper + `Microsoft.Data.SqlClient`. `Microsoft.Extensions.Identity.Core` `PasswordHasher`; `Microsoft.IdentityModel.JsonWebTokens` for tokens.
- `Azure.Messaging.ServiceBus` + `ServiceBusAdministrationClient` with managed identity, one client per namespace: product command namespace (`CMD_SB_FQDN`, Admin only) and simulator namespace (`SIM_SB_FQDN`, Simulation only).
- Simulation only: `Azure.ResourceManager.AppContainers` (collector container apps in the controller resource group, replica lists), `Azure.Messaging.EventHubs` (`GetPartitionPropertiesAsync`) + `Azure.Storage.Blobs` (checkpoint metadata).
- Static files + SPA fallback: any GET not under `/api`, `/hubs`, `/healthz`, `/readyz` returns `wwwroot/index.html`.

**Web:** follow `conventions/angular.md`.

## Layout

```
portal/
  CLAUDE.md
  specs/                            admin-services, admin-api, admin-http, admin-ui,
                                    simulator-api, simulator-http, simulator-ui
  Dockerfile                        context = portal/ (builds web, then api)
  Mod.Portal.slnx
  api/
    src/Mod.PortalApi/
      Program.cs                    composition root: AddAdmin/MapAdmin, AddSimulation/MapSimulation
      Auth/                         login endpoint, JWT issuing, AdminOnly policy
      Features/Admin/
        Tenants/ Sites/ Collectors/ Commands/ DeadLetters/ Restatements/ Audit/
        Services/                   Admin service interfaces (`specs/admin-services.md`)
      Features/Simulation/          DEMO SCAFFOLDING
        Endpoints/  Provisioning/  Channel/  Scenarios/ (Definitions/*.json)
        Telemetry/  Hubs/  Data/
      Infrastructure/               Sql/, ServiceBus/ (product namespace only), Audit/
      wwwroot/                      filled at image build from web/dist/admin-portal/browser
  web/                              Angular workspace, one application `admin-portal`
    angular.json, package.json, proxy.conf.json (→ http://localhost:5100)
    src/app/
      core/                         auth, api clients, layout shell, errors
      features/                     tenants/ collectors/ dead-letters/ restatements/ audit/
      simulation/                   DEMO SCAFFOLDING: services/ pages/ components/ simulation.routes.ts
  README.md
```

## Dockerfile

Three stages, context `portal/`: (1) `node:22` runs `npm ci` and `npx ng build admin-portal --configuration production` in `web/` → `web/dist/admin-portal/browser`; (2) .NET SDK publishes `api/src/Mod.PortalApi`; (3) ASP.NET runtime image with the publish output and the Angular build copied into `wwwroot`.

## Done when

- `dotnet build` passes on `Mod.Portal.slnx`; `npm ci` and `npx ng build` pass in `web/`.
- The API runs locally serving a placeholder `wwwroot/index.html` when the Angular build is absent; `ng serve` with the proxy works against it.
- The Dockerfile builds with `az acr build` using `portal/` as context.
- README lists departures: local accounts, in-process SignalR single replica, unsigned command requests, Pooled-only tenancy (Dedicated shown disabled), simulator channel and simulator-driven provisioning.
- `.env.example` committed at the module root, listing every variable of `contracts/environment/common.md` and `portal.md` with empty values. No secret in any tracked file.
