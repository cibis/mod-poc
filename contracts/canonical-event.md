# Contract: Canonical event (schema version 1.0)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

JSON, camelCase, UTF-8. Timestamps are ISO-8601 UTC with `Z` and millisecond precision. GUIDs are lowercase with hyphens.

| Field | Type | Required | Rules |
|---|---|---|---|
| eventId | GUID | yes | Assigned at the edge when the event is created. Idempotency key. Never reused. |
| sourceId | string, 1–100 chars, `[A-Za-z0-9._-]` | yes | Collector-local source identifier taken from the collector's config document. |
| sequence | int64 ≥ 1 | yes | Monotonic per (collectorId, sourceId), +1 per event, persisted across restarts. |
| eventTime | timestamp | yes | When the value occurred at the source. |
| observedTime | timestamp | yes | When the collector read or created it. |
| eventType | enum | yes | `StateChange`, `Counter`, `ProcessValue`, `Fault` |
| payload | object | yes | Shape depends on eventType (below). |
| quality | enum | yes | `Good`, `Uncertain`, `Bad` |
| schemaVersion | string | yes | `"1.0"` |

Payload by eventType:

| eventType | Payload fields |
|---|---|
| StateChange | `state`: `Running` \| `Idle` \| `Stopped` \| `Fault` \| `Setup` |
| Counter | `name` string; `value` int64 ≥ 0, cumulative; `reset` bool — true when the counter restarted from zero (value is then the count since reset) |
| ProcessValue | `name` string; `value` number (finite); `unit` string |
| Fault | `code` string 1–50 chars; `active` bool |

Consumers must ignore unknown additional fields (additive evolution only).
