# Spec: Mod.Processing — stream processing worker

Part of the `platform` module. Module rules and layout: `platform/CLAUDE.md`.

Every step is idempotent so reprocessing from any checkpoint is safe. Scales on consumer lag via KEDA (infra): `azure-eventhub` scaler, consumer group `processing`, `checkpointStrategy: blobMetadata`, `unprocessedEventThreshold` 64, min 0, max 8 replicas (= partitions). Use the standard blob checkpoint store and tolerate partitions moving between replicas at any time.

## Processing one Event Hubs event (one batch)

1. Read properties (`specs/eventhub-message.md`). Decompress and parse the body. Failure → one DeadLetter row `BatchUnreadable` containing the whole body (base64 if not text), continue.
2. For each event, validate against `contracts/canonical-event.md` and the reason-code rules, in this order: SchemaInvalid, UnsupportedSchemaVersion, UnknownEventType, TimestampOutOfRange, ValueOutOfRange. (Counter and mapping checks come later.)
3. **De-duplicate:** look up all eventIds of the batch in `telemetry.ProcessedEvent` in one query; drop events already processed (count them in a metric, do nothing else).
4. **Attribute:** map (collectorId, sourceId) → AssetId, LineId via the active `AssetSourceMapping` (cache for `MAPPING_CACHE_SECONDS`, refresh on miss once). No mapping → DeadLetter `UnmappedSource`. TenantId and SiteId come only from message properties; if the mapped asset's TenantId differs from the property, dead-letter with `UnmappedSource` and detail `TenantMismatch`.
5. **Counter check:** for Counter events, compare to the previous value for the same (collectorId, sourceId, name) — previous from earlier events in this batch, else the latest RawEvent. Lower value with `reset` = false → DeadLetter `CounterDecreaseWithoutReset`.
6. **Write** in one SQL transaction per batch:
   - `SqlBulkCopy` valid events into `telemetry.RawEvent`.
   - Insert all valid and dead-lettered eventIds into `telemetry.ProcessedEvent` (so a replayed EH event is a no-op).
   - DeadLetter rows (status `New`).
   - Gap detection (below).
   - Rollup recompute for every touched (AssetId, MinuteStart) (below).
   - Upsert `telemetry.CollectorFreshness`: LastEventTime = max(existing, max eventTime of valid events), LastReceivedAt, LastProcessedAt = now, totals incremented.
   - Upsert `telemetry.Reconciliation` per (collectorId, eventTime minute): add AcceptedCount and DeadLetteredCount.
7. If `PROCESSING_DELAY_MS` > 0, wait that long (demo knob, labelled as such in code).
8. Checkpoint after the transaction commits: every 50 events per partition or every 10 s, whichever first. On shutdown checkpoint what has committed.

On a transient SQL failure: retry the batch with exponential back-off (Polly or `Microsoft.Extensions.Resilience`), up to 5 attempts; after that, open a circuit for 30 s and stop pulling (do not checkpoint), so backlog builds in Event Hubs deliberately.

### Gap detection

- Per (collectorId, sourceId) keep `telemetry.SourceSequence.LastSequence`.
- Sort a batch's events by sequence per source. If `sequence > LastSequence + 1` → insert `SequenceGap(FromSequence = LastSequence + 1, ToSequence = sequence − 1)`. Update LastSequence to the max.
- If `sequence ≤ LastSequence` (late or out-of-order, not a duplicate): if it falls inside an open gap, check whether every sequence in that gap now exists in `RawEvent`; if so set `FilledAt`.

### Rollup projection

- Minute = floor(eventTime to the minute), UTC.
- For each touched (AssetId, MinuteStart), recompute the row from `telemetry.RawEvent` for that asset and minute (this makes the projection idempotent and order-independent):
  - `EventCount` = count of events.
  - `CounterDelta` = sum over counters of positive steps: for each Counter event ordered by eventTime, delta = value − previous value (previous may be the last Counter value before the minute); if `reset` is true, delta = value.
  - `FaultEventCount` = count of Fault events with `active` = true.
  - `LastState` = state of the latest StateChange in the minute, else null.
  - `ProcessValueAvg/Min/Max` over ProcessValue events.
- A window is **closed** when now ≥ MinuteStart + 60 s + `WINDOW_GRACE_SECONDS`. If the row is written while its window is closed (whether the row existed or not), set `IsRestated` = 1 and increment `RestatementCount`. Set `FirstComputedAt` on insert, `LastComputedAt` on every write.

### Dead-letter replay

- Every 10 s, select up to 500 rows with Status `ReplayRequested` (ordered by DeadLetterId). For each, re-run validation, attribution, counter check and write steps using the stored EventJson and the stored TenantId/SiteId/CollectorId/ReceivedAt. Remove the eventId from `ProcessedEvent` for the replayed event first.
- Success → Status `Replayed`, ReplayAttempts + 1. Failure → Status `New` with the new ReasonCode/ReasonDetail, ReplayAttempts + 1.
- `BatchUnreadable` rows cannot be replayed: set Status `Discarded` with detail.
- Run only on the replica that owns partition 0 (simple leader election) to avoid double work.

### Housekeeping (hourly, same leader)

Delete `RawEvent` older than `RAW_RETENTION_DAYS`, `ProcessedEvent` older than `DEDUPE_RETENTION_DAYS`, in batches of 10,000.

