# Spec: Mod.IngestApi — thin ingest API

Part of the `platform` module. Module rules and layout: `platform/CLAUDE.md`.

No business logic, no event-level semantics, stateless, horizontally scalable. One `EventHubProducerClient` per process. Registry reads (collector, certificate) cached ≤ 30 s. Rate limiter: token bucket partitioned by collectorId.

## Request pipeline for POST /v1/batches

1. Reject non-gzip or non-JSON with 415.
2. Extract and validate the client certificate (`contracts/collector-certificates.md`). Failure → 401; revoked or collector not Enrolled → 403.
3. Apply the per-collector rate limiter → 429 with `Retry-After`.
4. Read the body with a hard limit (decompressed size > `INGEST_MAX_BYTES` → 413). Decompress with a streaming gzip reader that stops at the limit (zip-bomb safe).
5. Validate the envelope (`contracts/ingest-api.md`). Collect up to 20 errors → 400 `EnvelopeInvalid` with an `errors` array extension member.
6. Look up tenant and site for the collector (cached registry read).
7. Build one `EventData`: body = gzip of the validated JSON bytes (re-use the original compressed bytes when possible), properties as in `specs/eventhub-message.md`, partition key = collectorId. Send with `SendAsync` and a 10 s timeout.
8. Event Hubs failure or timeout → 503 with `Retry-After: 5`. Success → 202 `{batchId, acceptedEventCount}`.

Log one structured line per request: collectorId, tenantId, batchId, eventCount, bytes, outcome, durationMs. Emit OpenTelemetry metrics: `mod.ingest.batches` (counter, tag outcome), `mod.ingest.events` (counter), `mod.ingest.duration` (histogram).

