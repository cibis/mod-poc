# Spec: Simulator — API (`Features/Simulation`) — DEMO SCAFFOLDING

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

Header comment on every file saying it is demo scaffolding; response header `X-Mod-Demo-Scaffolding: true` on every simulator endpoint.

## Hard rules

- Collectors are created, configured, enrolled and trusted **only through the Admin services**. The simulator decides only whether a registered collector currently runs.
- The simulator channel is structurally separate: its own Service Bus namespace (`SIM_SB_FQDN`), its own clients and credentials, its own classes. Never reuse the product command dispatcher or product queues.
- Nothing outside this feature may read the `sim` schema or the simulator channel.

## Power On (per collector, max 5 concurrent)

1. Collector must exist and have Status `Registered` or `Enrolled` (else skipped with reason).
2. `IEnrolmentTokenService.Issue(collectorId, actor: Simulator)`.
3. Create queue `sim/{collectorId}` (TTL 60 s) if missing. Mint Listen token for it and Send token for `sim-status` (30 days).
4. Create container app `col-{first 12 hex of collectorId}` in `CONTROLLER_RESOURCE_GROUP`, environment `CONTROLLER_ENVIRONMENT_ID`, location `CONTROLLER_LOCATION`: image `COLLECTOR_IMAGE`, registry server = host part of `COLLECTOR_IMAGE`, pulled with user-assigned identity `COLLECTOR_PULL_IDENTITY_ID`, 0.25 vCPU / 0.5 GiB, min = max = 1 replica, no ingress, `EmptyDir` volume at `/data`, liveness probe `/healthz:8080`, secrets for `ENROLMENT_TOKEN`, `SIM_COMMAND_SAS`, `SIM_STATUS_SAS`, env vars per `contracts/environment/collector.md`, tags `mod-sim-collector={collectorId}`, `mod-demo=true`.
5. `sim.CollectorState`: `Provisioning` → `Running` when the ARM operation completes (poll in background), or `Failed` with `LastError`. Push `provisioning` events. Write a `Power` timeline marker.

## Power Off

Delete the container app, delete queue `sim/{collectorId}`, `ICertificateService.RevokeActive(collectorId, "SimulatorPowerOff", actor: Simulator)`, state `Off`, marker, hub event. `BufferOverflowEver` is never reset.

## Provisioning reconciler

At startup and every 60 s: list container apps in the controller resource group tagged `mod-sim-collector`, compare with `sim.CollectorState`, correct state (apps without state → delete; state Running without app → `Off`).

## Status receiver

One receiver on `sim-status`. For each message: update the in-memory latest status per collector, push `collectorStatus`, set `BufferOverflowEver` sticky in memory and in SQL when true, persist `LastStatusJson` at most every 10 s per collector.

## Commands

Resolve the target selector via `ICollectorService` list + sim state (powered on only), send one message per collector to `sim/{collectorId}`, write one `Command` marker for the whole action with a human label (e.g. "Link down — region EU-North (4 collectors)").

## Scenarios

Definitions are JSON files (embedded) with `name`, `description`, `parameters`, and `steps` of `{atSeconds, action, targets, command | power}`. The runner executes one run at a time as a background task, writes `ScenarioStart`, `ScenarioStep` and `ScenarioEnd` markers, updates `sim.ScenarioRun`, and pushes `scenarioRun`. Stop cancels the remaining steps and restores link `up` and paused `false` for affected collectors. Every scenario first powers on any target collector that is Off and waits until its status reports `enrolled` (max 2 min), so each scenario works from a freshly deployed environment. Default parameters come from `contracts/demo-dataset.md`; resolve default collectors by name, falling back to the first powered collector if the named one no longer exists.

| Name | Steps |
|---|---|
| steady-state | Power on all registered collectors (param `tenantId` optional), link up, rate 1; run 10 min |
| one-site-offline | Param `collectorId` (default: `Customer B North 1 Collector`). t+60 s link down; t+360 s link up; run 10 min |
| regional-outage | Param `regionLabel` (default `EU-North`). t+60 s link down for the region; t+660 s link up for all at once; run 20 min |
| rate-ramp | All powered: rate 1 → 5 → 10 → 20 every 120 s, then back to 1 |
| bad-data | Param `share` (0.1), all powered collectors: set invalid share at t+30 s, reset to 0 at t+330 s |
| buffer-overflow | Param `collectorId` (default: `Customer A South 1 Collector`): buffer capacity 2000, rate 10, link down at t+30 s, link up at t+330 s, capacity back to config (restart not required: send capacity 200000) |

## Metrics aggregator (every 2 s)

Compute the metric keys in `specs/simulator-http.md` (metric keys). Sources: in-memory sim statuses; Event Hubs partition properties and checkpoint blob metadata (`sequencenumber`) for lag and ingest rate; ARM replica lists of the active revisions of `INGEST_APP_NAME`, `PROCESSING_APP_NAME`, `MANAGEMENT_APP_NAME`, `PORTAL_APP_NAME` and `REPORTING_APP_NAME` (running replicas only, refreshed every 2 s); `telemetry.CollectorFreshness` and `telemetry.Reconciliation` (read-only). Push `metricsTick`; write `sim.MetricSample` every 10 s; purge samples older than 24 h hourly. Rebuild the topology and push `topologyChanged` only when it differs.
