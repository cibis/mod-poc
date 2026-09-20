-- demo scaffolding
IF OBJECT_ID('[sim].[CollectorState]', 'U') IS NULL
BEGIN
    CREATE TABLE [sim].[CollectorState] (
        [CollectorId]       uniqueidentifier NOT NULL,
        [PoweredOn]         bit              NOT NULL,
        [ContainerAppName]  varchar(32)      NULL,
        [ProvisioningState] varchar(20)      NOT NULL CONSTRAINT [CK_CollectorState_ProvisioningState] CHECK ([ProvisioningState] IN ('Off', 'Provisioning', 'Running', 'Deprovisioning', 'Failed')),
        [LastError]         nvarchar(1000)   NULL,
        [BufferOverflowEver] bit             NOT NULL,
        [LastStatusJson]    nvarchar(max)    NULL,
        [LastStatusAt]      datetime2(3)     NULL,
        [UpdatedAt]         datetime2(3)     NOT NULL,
        CONSTRAINT [PK_CollectorState] PRIMARY KEY ([CollectorId]),
        CONSTRAINT [FK_CollectorState_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[sim].[ScenarioRun]', 'U') IS NULL
BEGIN
    CREATE TABLE [sim].[ScenarioRun] (
        [ScenarioRunId]  uniqueidentifier NOT NULL,
        [ScenarioName]   varchar(50)      NOT NULL,
        [ParametersJson] nvarchar(max)    NOT NULL,
        [StartedAt]      datetime2(3)     NOT NULL,
        [EndedAt]        datetime2(3)     NULL,
        [Status]         varchar(20)      NOT NULL CONSTRAINT [CK_ScenarioRun_Status] CHECK ([Status] IN ('Running', 'Completed', 'Stopped', 'Failed')),
        [StartedBy]      nvarchar(200)    NOT NULL,
        CONSTRAINT [PK_ScenarioRun] PRIMARY KEY ([ScenarioRunId])
    );
END
GO

IF OBJECT_ID('[sim].[TimelineMarker]', 'U') IS NULL
BEGIN
    CREATE TABLE [sim].[TimelineMarker] (
        [MarkerId]      bigint           IDENTITY NOT NULL,
        [At]            datetime2(3)     NOT NULL,
        [Kind]          varchar(20)      NOT NULL CONSTRAINT [CK_TimelineMarker_Kind] CHECK ([Kind] IN ('Command', 'Power', 'ScenarioStart', 'ScenarioStep', 'ScenarioEnd')),
        [Label]         nvarchar(200)    NOT NULL,
        [TargetsJson]   nvarchar(max)    NOT NULL,
        [ScenarioRunId] uniqueidentifier NULL,
        CONSTRAINT [PK_TimelineMarker] PRIMARY KEY ([MarkerId]),
        CONSTRAINT [FK_TimelineMarker_ScenarioRun] FOREIGN KEY ([ScenarioRunId]) REFERENCES [sim].[ScenarioRun] ([ScenarioRunId]) ON DELETE NO ACTION
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TimelineMarker_At'
               AND object_id = OBJECT_ID('[sim].[TimelineMarker]'))
    CREATE INDEX [IX_TimelineMarker_At] ON [sim].[TimelineMarker] ([At]);
GO

IF OBJECT_ID('[sim].[MetricSample]', 'U') IS NULL
BEGIN
    CREATE TABLE [sim].[MetricSample] (
        [MetricKey] varchar(150) NOT NULL,
        [At]        datetime2(0) NOT NULL,
        [Value]     float        NOT NULL,
        CONSTRAINT [PK_MetricSample] PRIMARY KEY ([MetricKey], [At])
    );
END
GO
