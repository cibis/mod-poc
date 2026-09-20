# Module: customer — customer reporting (API + Angular)

One deployable (`ca-reporting`) that serves tenant-scoped reporting data to customer users, pushes live updates, and serves the Angular customer app from `wwwroot` on the same origin. It demonstrates layered tenant isolation: tenant from the token only, an application-level filter, and SQL row-level security beneath it.

Minimal by design: it exists to show isolation, freshness and restatement, not to be a product dashboard. It only reads the database (owned by the `platform` module).

## How to work in this module

This file is deliberately short. **Read only the spec and contract files listed for the task you are doing**, and nothing else outside this module. One task per session; `/clear` between tasks.

| Task | Spec files (this module) | Shared files (under `/contracts/` unless shown otherwise) |
|---|---|---|
| API: host, auth, data access with tenant isolation, endpoints, live hub, Dockerfile | `specs/reporting-api.md`, `specs/reporting-http.md` | `browser-auth.md`, `database/overview.md`, `database/registry.md`, `database/telemetry.md`, `database/access.md`, `environment/common.md`, `environment/reporting.md`, `images.md` |
| UI (`web/`) | `specs/customer-ui.md`, `specs/reporting-http.md` | `browser-auth.md`, `/conventions/angular.md` |

Seeded tenants and `*-viewer` users for local runs: `demo-dataset.md` (optional).

## Technology

**API**
- .NET 10 ASP.NET Core minimal APIs, JWT bearer auth, in-process SignalR hub `/hubs/live` (max 1 replica).
- **EF Core** (`Microsoft.EntityFrameworkCore.SqlServer`), read-only `DbContext` with `HasQueryFilter(e => e.TenantId == currentTenant)` on every entity with TenantId, `AsNoTracking` everywhere.
- A `DbConnectionInterceptor` that runs `sp_set_session_context @key=N'TenantId', @value=@t, @read_only=1` on every opened connection, using the tenant of the current request (or the explicit tenant of the background poller). If no tenant is known, throw — never open a connection without it. The only exception is the login query on `registry.AppUser` (not covered by RLS), which uses `LoginConnectionFactory`, used nowhere else.
- Static files + SPA fallback (any GET not under `/api`, `/hubs`, `/healthz`, `/readyz` → `wwwroot/index.html`).

**Web:** follow `conventions/angular.md`.

## Layout

```
customer/
  CLAUDE.md
  specs/                            reporting-api.md, reporting-http.md, customer-ui.md
  Dockerfile                        context = customer/ (builds web, then api)
  Mod.Customer.slnx
  api/
    src/Mod.ReportingApi/
      Program.cs
      Auth/                         login, JWT, CustomerOnly policy, ITenantContext
      Data/ReportingDbContext.cs, TenantSessionInterceptor.cs, LoginConnectionFactory.cs
      Endpoints/Hierarchy.cs, SiteStatus.cs, Rollups.cs
      Live/LiveHub.cs, RollupChangePoller.cs
      wwwroot/                      filled at image build from web/dist/customer-portal/browser
  web/                              Angular workspace, one application `customer-portal`
    angular.json, package.json, proxy.conf.json (→ http://localhost:5200)
    src/app/
      core/                         auth, api clients, live hub service
      pages/sites/ site/ asset/
  README.md
```

## Dockerfile

Three stages, context `customer/`: (1) `node:22` runs `npm ci` and `npx ng build customer-portal --configuration production` in `web/` → `web/dist/customer-portal/browser`; (2) .NET SDK publishes `api/src/Mod.ReportingApi`; (3) ASP.NET runtime image with the Angular build copied into `wwwroot`.

## Done when

- `dotnet build` passes on `Mod.Customer.slnx`; `npm ci` and `npx ng build` pass in `web/`.
- The Dockerfile builds with `az acr build` using `customer/` as context.
- README lists departures: local accounts instead of Entra External ID federation, no Front Door, in-process SignalR, minimal UI.
- `.env.example` committed at the module root, listing every variable of `contracts/environment/common.md` and `reporting.md` with empty values. No secret in any tracked file.
