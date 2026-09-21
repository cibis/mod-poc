# Contract: Environment variables — collector apps (edge; set by the portal simulator at power On)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

| Variable | Example / default | Notes |
|---|---|---|
| COLLECTOR_ID | GUID | Identifies this collector instance |
| TENANT_ID | GUID | Tenant this collector belongs to |
| SITE_ID | GUID | Site this collector is deployed at |
| COLLECTOR_CLIENT_CERT_B64 | base64 PKCS12 | **PoC only**: shared TLS client cert (no password) signed by the collector CA. All collectors use the same cert; no per-collector enrollment or renewal. **Production**: each collector generates its own key pair and receives a per-collector cert via `POST /v1/enrol`; this variable is not needed. |
| MANAGEMENT_URL | `https://ca-mgmt.<env-domain>` | |
| DATA_DIR | `/data` | EmptyDir volume: buffer, request log |
| SIM_SB_FQDN | | Simulator namespace |
| SIM_COMMAND_QUEUE | `sim/{collectorId}` | |
| SIM_SB_KEY_NAME | `sim-issuer` | SAS rule name for simulator namespace |
| SIM_SB_KEY | secret | Base64-encoded SAS key for `SIM_SB_KEY_NAME` |
| SIM_STATUS_QUEUE | `sim-status` | |
| SIM_INITIAL_RATE | 1 | Events per second per source until a SetRate arrives |
| COLLECTOR_SOFTWARE_VERSION | image tag | |
| LOG_LEVEL | `Information` | Logs go to stdout only (controller Log Analytics) |

> **Removed (PoC simplification):** `ENROLMENT_TOKEN` — no longer used. The PoC skips the enrollment HTTP ceremony; identity is loaded from `COLLECTOR_CLIENT_CERT_B64`, `COLLECTOR_ID`, `TENANT_ID`, and `SITE_ID` at startup. Production would reinstate `ENROLMENT_TOKEN` for per-collector cert issuance.
