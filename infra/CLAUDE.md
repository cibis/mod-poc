# Module: infra — Bicep and scripts

Create and destroy the entire PoC with one command each, from a machine that has only an Azure subscription, a bash shell and the Azure CLI (Azure Cloud Shell qualifies). No local Docker, no portal steps, no hand-created identities or resources.

## How to work in this module

This file is deliberately short. **Read only the spec and contract files listed for the task you are doing**, and nothing else outside this module. Do not open `edge/`, `platform/`, `portal/` or `customer/`. One task per session; `/clear` between tasks.

| Task | Spec files (this module) | Shared files (under `/contracts/` unless shown otherwise) |
|---|---|---|
| Production space Bicep (`production/`, `modules/`) | `specs/production.md` | `environment/*` (all), `images.md`, `database/access.md`, `simulator-channel.md` (`sim-status`, `sim-issuer`), `command-channel.md` (`collector-issuer`) |
| Controller space Bicep (`controller/`) | `specs/controller.md` | `environment/collector.md`, `images.md` (edge row) |
| Scripts (`scripts/`: up, down, stop, start, local-env) | `specs/scripts.md` | `images.md` (build contexts), `demo-dataset.md` (ready-to-demo summary; `SEED_DEMO_DATA`) |

Recommended order: production → controller → scripts.

## Deployments at a glance

Two independent deployments (production space and controller space), each deployable and destroyable on its own, in different regions. They communicate only over the public internet. No VNet, peering or Private Link between them. Production space has no network path into controller space (the portal manages collector apps through the ARM control plane only).

Entities created by infra: event hub `telemetry` (8 partitions, 1 day) with consumer group `processing`; Service Bus sim namespace queue `sim-status` (TTL 30 s). Product command queues (`cmd/…`, `reply/…`) and simulator command queues (`sim/{collectorId}`) are created at runtime by the portal module, not by infra.

## Technology

- Bicep (latest), resource-group-scoped deployments, Azure Verified Modules where they fit, otherwise plain resources in `modules/`.
- bash + Azure CLI (`az`), `jq`. Scripts use `set -euo pipefail` and are idempotent.
- Container images built in Azure with `az acr build` (ACR Tasks).

## Layout

```
infra/
  CLAUDE.md
  specs/              production.md, controller.md, scripts.md
  scripts/
    up.sh               create or update everything (idempotent)
    down.sh             destroy everything (idempotent)
    stop.sh, start.sh   cold stop / restart between demonstrations
    lib/common.sh       naming, logging, az helpers
  production/
    core.bicep          everything except container apps
    apps.bicep          container apps and the migrator job
  controller/
    main.bicep          controller-space environment and collector pull identity
  modules/              one file per resource type or concern
  README.md
```

## Done when

- `az bicep build` passes for all entry points; `az deployment group what-if` runs cleanly.
- `up.sh` from an empty subscription completes; `down.sh` leaves nothing behind; both are safe to run twice.
- README lists departures: public endpoints, no Front Door/WAF, no private endpoints, Key Vault purge protection off, Basic Service Bus, simulator namespace.
