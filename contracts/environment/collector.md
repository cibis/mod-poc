# Contract: Environment variables — collector apps (edge; set by the portal simulator at power On)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

| Variable | Example / default | Notes |
|---|---|---|
| COLLECTOR_ID | GUID | Known before enrolment; used for simulator queue names only |
| MANAGEMENT_URL | `https://ca-mgmt.<env-domain>` | |
| ENROLMENT_TOKEN | secret | Single-use; ignored if an identity already exists in DATA_DIR |
| DATA_DIR | `/data` | EmptyDir volume: buffer, identity, request log |
| SIM_SB_FQDN | | Simulator namespace |
| SIM_COMMAND_QUEUE | `sim/{collectorId}` | |
| SIM_COMMAND_SAS | secret | SAS token (Listen) |
| SIM_STATUS_QUEUE | `sim-status` | |
| SIM_STATUS_SAS | secret | SAS token (Send) |
| SIM_INITIAL_RATE | 1 | Events per second per source until a SetRate arrives |
| COLLECTOR_SOFTWARE_VERSION | image tag | |
| LOG_LEVEL | `Information` | Logs go to stdout only (controller Log Analytics) |
