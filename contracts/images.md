# Contract: Images, apps and endpoints

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

| Module | Image | Dockerfile (build context) | Container app | Space | Ingress | Port / health |
|---|---|---|---|---|---|---|
| platform | `mod/ingest-api` | `platform/docker/ingest-api.Dockerfile` (`platform/`) | `ca-ingest` | production | external, client cert `require` | 8080, `/healthz`, `/readyz` |
| platform | `mod/management-api` | `platform/docker/management-api.Dockerfile` (`platform/`) | `ca-mgmt` | production | external, client cert `accept` | 8080, `/healthz`, `/readyz` |
| platform | `mod/processing` | `platform/docker/processing.Dockerfile` (`platform/`) | `ca-processing` | production | none | 8080, `/healthz` |
| platform | `mod/db-migrator` | `platform/docker/db-migrator.Dockerfile` (`platform/`) | job `job-dbmigrate` | production | none | exit code |
| portal | `mod/portal` | `portal/Dockerfile` (`portal/`) — builds the Angular admin app and the .NET API | `ca-portal` | production | external | 8080, `/healthz`, `/readyz` |
| customer | `mod/customer` | `customer/Dockerfile` (`customer/`) — builds the Angular customer app and the .NET API | `ca-reporting` | production | external | 8080, `/healthz`, `/readyz` |
| edge | `mod/collector` | `edge/Dockerfile` (`edge/`) | `col-{first 12 hex chars of collectorId}`, created by portal | controller | none | 8080, `/healthz` |

Every image builds from its own module folder with `az acr build`; no local Docker, no repository-root build context.
