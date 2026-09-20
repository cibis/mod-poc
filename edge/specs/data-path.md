# Spec: collector data path — sources, buffer, forwarder

Part of the `edge` module. Module rules and layout: `edge/CLAUDE.md`.

## Simulated sources

- One `SimulatedSource` per config source, running at `eventsPerSecondPerSource` (from `SIM_INITIAL_RATE`, then `SetRate`), spread evenly with small jitter.
- A simple machine state machine per source: mostly `Running`, occasionally `Idle`, rarely `Fault`/`Setup`; emit `StateChange` on transitions and a `Fault` event (active true/false) around faults.
- Mix of emitted events (report by exception): `Counter` (cumulative `counterName`, increments only while Running; reset to 0 once per simulated 8 h shift with `reset` = true), `ProcessValue` (`processValue.name`, value in [min, max] with noise, emitted when change > 1 % deadband or at least every 10 s), plus state/fault events.
- While `SetPaused` is true, sources emit nothing.
- `InvalidDataInjector`: replaces the configured share of generated events with invalid ones per `contracts/dead-letter-reason-codes.md` (they must pass edge validation so they reach the cloud).
- Every event gets a new `eventId`, `sequence` from the per-source counter persisted in SQLite (incremented in the same transaction as the buffer append), `eventTime` = `observedTime` = now.

## Buffer

- SQLite tables: `buffer` (rowid, sourceId, sequence, eventTime, eventType, json, sizeBytes), `source_sequence` (sourceId, lastSequence), `minute_counts` (minuteStart, produced, droppedAtEdge, acknowledged flag), `rejected_batches`, `executed_requests` (last 1,000).
- Capacity in events: `buffer.capacityEvents` from config, overridden by `SetBufferCapacity`.
- **Overflow policy** when full: first delete the oldest `ProcessValue` events; if none remain, delete the oldest events of any type. Count deletions in `overflowDroppedTotal` and `droppedAtEdge` per minute; set `bufferOverflowEver` = true permanently (persist it).
- A batch is deleted from the buffer only after a 202.

## Forwarder

- One batch in flight at a time. Build a batch from the oldest events up to `maxEvents` / `maxBytes` (uncompressed). Send when the batch is full or `flushIntervalMs` has elapsed since the oldest unsent event, whichever first.
- Batch identity: record the batchId with the included rowids so a retry resends the identical batch with the same batchId.
- Throughput cap: at most `maxBatchesPerSecond` (catch-up is the same loop at its ceiling).
- Retry: exponential back-off with full jitter from `retryBaseMs` to `retryMaxMs`; honour `Retry-After`. Response handling exactly per the table in `contracts/ingest-api.md`.
- Every call goes through `LinkGate`. When the link is down, calls fail immediately with a simulated network error (no request leaves the process), and the forwarder backs off as it would for a real outage.
