-- RawEvent: clustered columnstore index; no primary key
IF OBJECT_ID('[telemetry].[RawEvent]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[RawEvent] (
        [EventId]      uniqueidentifier NOT NULL,
        [TenantId]     uniqueidentifier NOT NULL,
        [SiteId]       uniqueidentifier NOT NULL,
        [CollectorId]  uniqueidentifier NOT NULL,
        [SourceId]     varchar(100)     NOT NULL,
        [AssetId]      uniqueidentifier NOT NULL,
        [Sequence]     bigint           NOT NULL,
        [EventTime]    datetime2(3)     NOT NULL,
        [ObservedTime] datetime2(3)     NOT NULL,
        [EventType]    varchar(20)      NOT NULL,
        [PayloadJson]  nvarchar(2000)   NOT NULL,
        [Quality]      varchar(10)      NOT NULL,
        [BatchId]      uniqueidentifier NOT NULL,
        [ReceivedAt]   datetime2(3)     NOT NULL,
        [ProcessedAt]  datetime2(3)     NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'CCI_RawEvent'
               AND object_id = OBJECT_ID('[telemetry].[RawEvent]'))
    CREATE CLUSTERED COLUMNSTORE INDEX [CCI_RawEvent] ON [telemetry].[RawEvent];
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RawEvent_CollectorId_SourceId_Sequence'
               AND object_id = OBJECT_ID('[telemetry].[RawEvent]'))
    CREATE NONCLUSTERED INDEX [IX_RawEvent_CollectorId_SourceId_Sequence]
        ON [telemetry].[RawEvent] ([CollectorId], [SourceId], [Sequence]);
GO

IF OBJECT_ID('[telemetry].[ProcessedEvent]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[ProcessedEvent] (
        [EventId]     uniqueidentifier NOT NULL,
        [ProcessedAt] datetime2(3)     NOT NULL,
        CONSTRAINT [PK_ProcessedEvent] PRIMARY KEY ([EventId])
    );
END
GO

IF OBJECT_ID('[telemetry].[SourceSequence]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[SourceSequence] (
        [CollectorId]  uniqueidentifier NOT NULL,
        [SourceId]     varchar(100)     NOT NULL,
        [TenantId]     uniqueidentifier NOT NULL,
        [LastSequence] bigint           NOT NULL,
        [UpdatedAt]    datetime2(3)     NOT NULL,
        CONSTRAINT [PK_SourceSequence] PRIMARY KEY ([CollectorId], [SourceId])
    );
END
GO

IF OBJECT_ID('[telemetry].[SequenceGap]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[SequenceGap] (
        [GapId]        bigint           IDENTITY NOT NULL,
        [TenantId]     uniqueidentifier NOT NULL,
        [CollectorId]  uniqueidentifier NOT NULL,
        [SourceId]     varchar(100)     NOT NULL,
        [FromSequence] bigint           NOT NULL,
        [ToSequence]   bigint           NOT NULL,
        [DetectedAt]   datetime2(3)     NOT NULL,
        [FilledAt]     datetime2(3)     NULL,
        CONSTRAINT [PK_SequenceGap] PRIMARY KEY ([GapId])
    );
END
GO

IF OBJECT_ID('[telemetry].[RollupMinute]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[RollupMinute] (
        [TenantId]         uniqueidentifier NOT NULL,
        [SiteId]           uniqueidentifier NOT NULL,
        [LineId]           uniqueidentifier NOT NULL,
        [AssetId]          uniqueidentifier NOT NULL,
        [MinuteStart]      datetime2(0)     NOT NULL,
        [EventCount]       int              NOT NULL,
        [CounterDelta]     bigint           NOT NULL,
        [FaultEventCount]  int              NOT NULL,
        [LastState]        varchar(20)      NULL,
        [ProcessValueAvg]  float            NULL,
        [ProcessValueMin]  float            NULL,
        [ProcessValueMax]  float            NULL,
        [IsRestated]       bit              NOT NULL,
        [RestatementCount] int              NOT NULL,
        [FirstComputedAt]  datetime2(3)     NOT NULL,
        [LastComputedAt]   datetime2(3)     NOT NULL,
        CONSTRAINT [PK_RollupMinute] PRIMARY KEY ([AssetId], [MinuteStart])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RollupMinute_TenantId_MinuteStart'
               AND object_id = OBJECT_ID('[telemetry].[RollupMinute]'))
    CREATE INDEX [IX_RollupMinute_TenantId_MinuteStart]
        ON [telemetry].[RollupMinute] ([TenantId], [MinuteStart]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RollupMinute_LastComputedAt'
               AND object_id = OBJECT_ID('[telemetry].[RollupMinute]'))
    CREATE INDEX [IX_RollupMinute_LastComputedAt]
        ON [telemetry].[RollupMinute] ([LastComputedAt]);
GO

IF OBJECT_ID('[telemetry].[CollectorFreshness]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[CollectorFreshness] (
        [CollectorId]            uniqueidentifier NOT NULL,
        [TenantId]               uniqueidentifier NOT NULL,
        [SiteId]                 uniqueidentifier NOT NULL,
        [LastEventTime]          datetime2(3)     NOT NULL,
        [LastReceivedAt]         datetime2(3)     NOT NULL,
        [LastProcessedAt]        datetime2(3)     NOT NULL,
        [EventsProcessedTotal]   bigint           NOT NULL,
        [EventsDeadLetteredTotal] bigint          NOT NULL,
        CONSTRAINT [PK_CollectorFreshness] PRIMARY KEY ([CollectorId])
    );
END
GO

IF OBJECT_ID('[telemetry].[Reconciliation]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[Reconciliation] (
        [CollectorId]        uniqueidentifier NOT NULL,
        [MinuteStart]        datetime2(0)     NOT NULL,
        [TenantId]           uniqueidentifier NOT NULL,
        [ProducedCount]      int              NULL,
        [DroppedAtEdgeCount] int              NULL,
        [AcceptedCount]      int              NOT NULL DEFAULT 0,
        [DeadLetteredCount]  int              NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Reconciliation] PRIMARY KEY ([CollectorId], [MinuteStart])
    );
END
GO

IF OBJECT_ID('[telemetry].[DeadLetter]', 'U') IS NULL
BEGIN
    CREATE TABLE [telemetry].[DeadLetter] (
        [DeadLetterId]    bigint           IDENTITY NOT NULL,
        [TenantId]        uniqueidentifier NULL,
        [SiteId]          uniqueidentifier NULL,
        [CollectorId]     uniqueidentifier NOT NULL,
        [BatchId]         uniqueidentifier NOT NULL,
        [EventId]         uniqueidentifier NULL,
        [ReasonCode]      varchar(50)      NOT NULL,
        [ReasonDetail]    nvarchar(500)    NOT NULL,
        [EventJson]       nvarchar(max)    NOT NULL,
        [ReceivedAt]      datetime2(3)     NOT NULL,
        [CreatedAt]       datetime2(3)     NOT NULL,
        [Status]          varchar(20)      NOT NULL,
        [StatusChangedAt] datetime2(3)     NOT NULL,
        [StatusChangedBy] nvarchar(200)    NULL,
        [ReplayAttempts]  int              NOT NULL,
        CONSTRAINT [PK_DeadLetter] PRIMARY KEY ([DeadLetterId])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeadLetter_TenantId_Status_CreatedAt'
               AND object_id = OBJECT_ID('[telemetry].[DeadLetter]'))
    CREATE INDEX [IX_DeadLetter_TenantId_Status_CreatedAt]
        ON [telemetry].[DeadLetter] ([TenantId], [Status], [CreatedAt]);
GO
