# Spec: collector control — lifecycle, identity, config, health, channels

Part of the `edge` module. Module rules and layout: `edge/CLAUDE.md`.

## Lifecycle

1. **Start:** open `DATA_DIR`. If `{DATA_DIR}/identity` holds a valid certificate, use it; otherwise enrol with `ENROLMENT_TOKEN` (generate EC P-256 key, CSR, `POST /v1/enrol`). On enrolment failure, retry every 30 s and report `enrolled: false` on the simulator channel. Persist key, certificate, CA certificate and the first config.
2. **Config:** apply the config from enrolment, then poll `GET /v1/config` every `configPollSeconds` with `If-None-Match`. Apply a new config atomically: validate it, rebuild sources, then swap. If validation or the post-apply check (sources start, buffer opens) fails, keep the previous config (last-known-good) and log it.
3. **Operate:** sources generate events → mapper → edge validation → buffer. Forwarder drains the buffer. Health reporter posts every `healthReportSeconds`. Certificate renewal runs when < 48 h remain.
4. **Command channel** (if `commandChannelEnabled`): fetch tokens from `GET /v1/command-channel`, receive on the request queue, reply on the reply queue.
5. **Simulator channel** (always): receive on `SIM_COMMAND_QUEUE`, publish status to `SIM_STATUS_QUEUE` every 2 s.

## Health reporting

Every `healthReportSeconds` (when the link is up): HealthReport per `contracts/management-api.md`. `minuteCounts` includes every completed minute not yet acknowledged; mark them acknowledged on 204. On failure, keep them for the next report.

## Container app shape (created by the portal simulator, relevant to the implementation)

0.25 vCPU / 0.5 GiB, exactly 1 replica, no ingress, `EmptyDir` volume mounted at `/data`, liveness probe HTTP `/healthz` on 8080. The container restarts on exit; the buffer survives a process restart but not replica replacement.
