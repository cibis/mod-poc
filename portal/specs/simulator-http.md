# Internal contract: Simulator HTTP API, hub and metric keys (API ↔ simulator UI) — DEMO SCAFFOLDING

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

## Contract: Simulator API (served by portal-api `Features/Simulation`, called by admin-portal `simulation` area) — DEMO SCAFFOLDING

Same origin and same ModAdmin JWT as the administration API. Every response carries header `X-Mod-Demo-Scaffolding: true`.

**Target selector** (used by commands and power operations): `{collectorIds?: GUID[], siteIds?: GUID[], tenantId?: GUID, regionLabel?: string, all?: bool}`. Resolved to the union of matching registered collectors; for commands, only powered-on collectors receive messages.

| Method and route | Purpose / body → response |
|---|---|
| GET /api/sim/collectors | `[{collectorId, name, tenantId, tenantName, siteId, siteName, regionLabel, status, poweredOn, provisioningState, lastError, bufferOverflowEver, simStatus (latest status message or null), lastStatusAt}]` |
| POST /api/sim/power | `{targets, on: bool}` → `{accepted:[collectorId], skipped:[{collectorId, reason}]}`. Asynchronous; progress via hub |
| POST /api/sim/commands | `{targets, command:{type, args}}` → `{commandId, sentTo:[collectorId]}`; writes a timeline marker |
| POST /api/sim/fleet/generate | `{tenantId, sites, linesPerSite, assetsPerLine, regionLabel, commandChannelEnabled}` → created collector ids. Uses the ordinary admin services (same code path as manual creation) |
| GET /api/sim/scenarios | `[{name, description, parameters:[{name, type, default}], durationSeconds}]` |
| POST /api/sim/scenarios/{name}/run | `{parameters}` → `{scenarioRunId}`; 409 if a run is active |
| POST /api/sim/scenario-runs/{id}/stop | → 204 |
| GET /api/sim/scenario-runs?limit | Recent runs |
| GET /api/sim/topology | Topology snapshot (below) |
| GET /api/sim/metrics?keys&from&to | `{series:[{key, points:[[epochMs, value]]}]}` from `sim.MetricSample` |
| GET /api/sim/timeline?from&to | `[{markerId, at, kind, label, targets, scenarioRunId}]` |

**Topology snapshot**: `{nodes:[{id, kind, label, space, state, replicas?, minReplicas?, maxReplicas?, metrics:{}}], edges:[{id, from, to, kind, active, label}]}`
- `kind` (node): `collector`, `ingest`, `eventHub`, `processing`, `sql`, `management`, `reporting`, `portal`, `cmdBus`, `simBus`.
- `space`: `controller` (collectors) or `production` (everything else). `simBus` is marked `space: "outside"` and rendered outside both.
- `state`: `ok`, `degraded`, `down`, `off`, `scaling`.
- Edge `kind`: `data`, `management`, `command`, `simulation`.

**SignalR hub `/hubs/sim`** (server → client):
- `metricsTick` every 2 s: `{at, values:{metricKey: number}}` (metric-key contract below).
- `collectorStatus` on every received status message: `{collectorId, status}` (simulator status body).
- `provisioning` `{collectorId, provisioningState, lastError}`.
- `topologyChanged` full topology snapshot, when nodes, states or replica counts change.
- `timelineMarker` marker object, as written.
- `scenarioRun` `{scenarioRunId, name, status, currentStep, startedAt, endedAt}`.

## Contract: Simulator metric keys

| Key | Unit | Source |
|---|---|---|
| `ingest.batchesPerSec` | batches/s | Δ of Event Hubs `LastEnqueuedSequenceNumber` summed over partitions / Δt |
| `ingest.replicas` | count | ARM replica list of the ingest container app |
| `eventhub.lagBatches` | batches | Σ partitions (last enqueued sequence − checkpoint sequence) |
| `processing.replicas` | count | ARM replica list of the processing container app |
| `management.replicas` | count | ARM replica list of the management container app |
| `portal.replicas` | count | ARM replica list of the portal container app |
| `reporting.replicas` | count | ARM replica list of the reporting container app |
| `processing.eventsPerSec` | events/s | Δ Σ `telemetry.CollectorFreshness.EventsProcessedTotal` / Δt |
| `processing.deadLettersPerMin` | events/min | Δ Σ `EventsDeadLetteredTotal` |
| `rollups.restatedLastHour` | minutes | Count of `RollupMinute` with IsRestated and LastComputedAt in last hour |
| `collectors.poweredOn` | count | sim state |
| `collectors.linkDown` | count | sim status |
| `collector.{collectorId}.bufferDepth` | events | sim status |
| `collector.{collectorId}.bufferCapacity` | events | sim status |
| `collector.{collectorId}.freshnessAgeSec` | seconds | now − `CollectorFreshness.LastEventTime` |
| `tenant.{tenantId}.completenessLastHour` | ratio 0–1 | `telemetry.Reconciliation` |
