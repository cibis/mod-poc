# Contract: Database — schema `telemetry` (data plane)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Conventions: see `overview.md` (types, timestamps, GUIDs).

**telemetry.RawEvent** — clustered columnstore index; nonclustered index (`CollectorId`, `SourceId`, `Sequence`)
`EventId` · `TenantId` · `SiteId` · `CollectorId` · `SourceId` varchar(100) · `AssetId` · `Sequence` bigint · `EventTime` · `ObservedTime` · `EventType` varchar(20) · `PayloadJson` nvarchar(2000) · `Quality` varchar(10) · `BatchId` · `ReceivedAt` · `ProcessedAt`. Rows with `ReceivedAt` older than 7 days are deleted by processing hourly.

**telemetry.ProcessedEvent** — PK `EventId` (de-duplication store)
`EventId` · `ProcessedAt`. Purged after `DEDUPE_RETENTION_DAYS` (default 14; must exceed the longest buffered outage).

**telemetry.SourceSequence** — PK (`CollectorId`, `SourceId`)
`CollectorId` · `SourceId` · `TenantId` · `LastSequence` bigint (highest seen) · `UpdatedAt`

**telemetry.SequenceGap** — PK `GapId` bigint identity
`GapId` · `TenantId` · `CollectorId` · `SourceId` · `FromSequence` bigint · `ToSequence` bigint (inclusive) · `DetectedAt` · `FilledAt` null (set when every sequence in the range has arrived)

**telemetry.RollupMinute** — PK (`AssetId`, `MinuteStart`); index (`TenantId`, `MinuteStart`); index (`LastComputedAt`)
`TenantId` · `SiteId` · `LineId` · `AssetId` · `MinuteStart` datetime2(0) · `EventCount` int · `CounterDelta` bigint · `FaultEventCount` int · `LastState` varchar(20) null · `ProcessValueAvg` float null · `ProcessValueMin` float null · `ProcessValueMax` float null · `IsRestated` bit · `RestatementCount` int · `FirstComputedAt` · `LastComputedAt`

**telemetry.CollectorFreshness** — PK `CollectorId`
`CollectorId` · `TenantId` · `SiteId` · `LastEventTime` · `LastReceivedAt` · `LastProcessedAt` · `EventsProcessedTotal` bigint · `EventsDeadLetteredTotal` bigint

**telemetry.Reconciliation** — PK (`CollectorId`, `MinuteStart`)
`CollectorId` · `MinuteStart` datetime2(0) · `TenantId` · `ProducedCount` int null (written by management-api from health reports) · `DroppedAtEdgeCount` int null (management-api) · `AcceptedCount` int default 0 (processing) · `DeadLetteredCount` int default 0 (processing). Completeness = (Accepted + DeadLettered) / Produced. Data quality = Accepted / (Accepted + DeadLettered).

**telemetry.DeadLetter** — PK `DeadLetterId` bigint identity; index (`TenantId`, `Status`, `CreatedAt`)
`DeadLetterId` · `TenantId` null · `SiteId` null · `CollectorId` · `BatchId` · `EventId` null · `ReasonCode` varchar(50) · `ReasonDetail` nvarchar(500) · `EventJson` nvarchar(max) (the single event, or the whole batch for BatchUnreadable) · `ReceivedAt` · `CreatedAt` · `Status` varchar(20) · `StatusChangedAt` · `StatusChangedBy` nvarchar(200) null · `ReplayAttempts` int
