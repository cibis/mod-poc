# Contract: Ingest HTTP API (served by ingest-api, called by collector)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Base URL: `ingestUrl` from the collector config document (the ingest container app's public FQDN).

**POST /v1/batches**

- TLS client certificate required (see the certificate contract). The Container Apps ingress runs in client-certificate mode `require`.
- Request headers: `Content-Type: application/json`, `Content-Encoding: gzip` (both required).
- Body after decompression is the batch envelope:

| Field | Type | Rules |
|---|---|---|
| batchId | GUID | New per batch. A retry of the same batch reuses the same batchId. |
| collectorId | GUID | Informational. The server uses the certificate identity. Mismatch → 400 `CollectorMismatch`. |
| schemaVersion | string | `"1.0"` |
| sentAt | timestamp | |
| events | array | 1–500 canonical events |

- Limits: at most 500 events and at most 262,144 bytes uncompressed.
- Ingest validation is envelope-level only: gzip, JSON parse, envelope fields, event count and size, presence and JSON type of every event's top-level fields. Payload semantics are not checked here.

Responses (errors use `application/problem+json`, `type` = the code shown):

| Status | Meaning | Collector behaviour |
|---|---|---|
| 202 `{batchId, acceptedEventCount}` | Durably appended to Event Hubs | Delete batch from buffer |
| 400 `EnvelopeInvalid` / `CollectorMismatch` / `UnsupportedSchemaVersion` | Not retryable | Move batch to local rejected store, count it, continue |
| 401 | Missing, invalid, expired or untrusted certificate | Keep buffering, attempt renewal/re-check, retry with back-off |
| 403 | Certificate revoked or collector not Enrolled | Keep buffering, set `authRejected`, retry with long back-off |
| 413 | Too large | Treat as bug: split batch in half and retry |
| 415 | Wrong content type or encoding | Treat as bug: log, retry with back-off |
| 429 | Per-collector rate limit, `Retry-After` seconds | Wait, retry |
| 503 | Event Hubs unavailable, `Retry-After` | Wait, retry |

A duplicate batchId is accepted again (202). De-duplication happens downstream on eventId.
