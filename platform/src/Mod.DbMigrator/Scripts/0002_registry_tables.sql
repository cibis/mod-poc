IF OBJECT_ID('[registry].[Tenant]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[Tenant] (
        [TenantId]      uniqueidentifier NOT NULL,
        [Name]          nvarchar(200)    NOT NULL,
        [Slug]          varchar(50)      NOT NULL,
        [IsolationMode] varchar(20)      NOT NULL CONSTRAINT [CK_Tenant_IsolationMode] CHECK ([IsolationMode] = 'Pooled'),
        [Status]        varchar(20)      NOT NULL DEFAULT 'Active',
        [CreatedAt]     datetime2(3)     NOT NULL,
        CONSTRAINT [PK_Tenant] PRIMARY KEY ([TenantId]),
        CONSTRAINT [UQ_Tenant_Slug] UNIQUE ([Slug])
    );
END
GO

IF OBJECT_ID('[registry].[Site]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[Site] (
        [SiteId]      uniqueidentifier NOT NULL,
        [TenantId]    uniqueidentifier NOT NULL,
        [Name]        nvarchar(200)    NOT NULL,
        [RegionLabel] nvarchar(50)     NOT NULL,
        [TimeZone]    varchar(64)      NOT NULL,
        [CreatedAt]   datetime2(3)     NOT NULL,
        CONSTRAINT [PK_Site] PRIMARY KEY ([SiteId]),
        CONSTRAINT [FK_Site_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [registry].[Tenant] ([TenantId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[Line]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[Line] (
        [LineId]    uniqueidentifier NOT NULL,
        [TenantId]  uniqueidentifier NOT NULL,
        [SiteId]    uniqueidentifier NOT NULL,
        [Name]      nvarchar(200)    NOT NULL,
        [CreatedAt] datetime2(3)     NOT NULL,
        CONSTRAINT [PK_Line] PRIMARY KEY ([LineId]),
        CONSTRAINT [FK_Line_Site] FOREIGN KEY ([SiteId]) REFERENCES [registry].[Site] ([SiteId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[Asset]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[Asset] (
        [AssetId]          uniqueidentifier NOT NULL,
        [TenantId]         uniqueidentifier NOT NULL,
        [SiteId]           uniqueidentifier NOT NULL,
        [LineId]           uniqueidentifier NOT NULL,
        [Name]             nvarchar(200)    NOT NULL,
        [AssetType]        nvarchar(50)     NOT NULL,
        [CounterName]      varchar(50)      NOT NULL DEFAULT 'good_count',
        [ProcessValueName] varchar(50)      NULL,
        [ProcessValueUnit] varchar(20)      NULL,
        [ProcessValueMin]  float            NULL,
        [ProcessValueMax]  float            NULL,
        [CreatedAt]        datetime2(3)     NOT NULL,
        CONSTRAINT [PK_Asset] PRIMARY KEY ([AssetId]),
        CONSTRAINT [FK_Asset_Line] FOREIGN KEY ([LineId]) REFERENCES [registry].[Line] ([LineId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[Collector]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[Collector] (
        [CollectorId]           uniqueidentifier NOT NULL,
        [TenantId]              uniqueidentifier NOT NULL,
        [SiteId]                uniqueidentifier NOT NULL,
        [Name]                  nvarchar(200)    NOT NULL,
        [Status]                varchar(20)      NOT NULL CONSTRAINT [CK_Collector_Status] CHECK ([Status] IN ('Registered', 'Enrolled', 'Revoked', 'Retired')),
        [CommandChannelEnabled] bit              NOT NULL,
        [CreatedAt]             datetime2(3)     NOT NULL,
        [EnrolledAt]            datetime2(3)     NULL,
        [LastSeenAt]            datetime2(3)     NULL,
        [SoftwareVersion]       varchar(50)      NULL,
        [ReportedConfigVersion] int              NULL,
        [ReportedConfigHash]    char(64)         NULL,
        CONSTRAINT [PK_Collector] PRIMARY KEY ([CollectorId]),
        CONSTRAINT [FK_Collector_Site] FOREIGN KEY ([SiteId]) REFERENCES [registry].[Site] ([SiteId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[AssetSourceMapping]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[AssetSourceMapping] (
        [CollectorId] uniqueidentifier NOT NULL,
        [SourceId]    varchar(100)     NOT NULL,
        [AssetId]     uniqueidentifier NOT NULL,
        [TenantId]    uniqueidentifier NOT NULL,
        [ValidFrom]   datetime2(3)     NOT NULL,
        [ValidTo]     datetime2(3)     NULL,
        CONSTRAINT [PK_AssetSourceMapping] PRIMARY KEY ([CollectorId], [SourceId], [ValidFrom]),
        CONSTRAINT [FK_AssetSourceMapping_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AssetSourceMapping_Asset] FOREIGN KEY ([AssetId]) REFERENCES [registry].[Asset] ([AssetId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[CollectorConfig]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[CollectorConfig] (
        [CollectorId]  uniqueidentifier NOT NULL,
        [Version]      int              NOT NULL,
        [SettingsJson] nvarchar(max)    NOT NULL,
        [UpdatedAt]    datetime2(3)     NOT NULL,
        [UpdatedBy]    nvarchar(200)    NOT NULL,
        CONSTRAINT [PK_CollectorConfig] PRIMARY KEY ([CollectorId]),
        CONSTRAINT [FK_CollectorConfig_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[EnrolmentToken]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[EnrolmentToken] (
        [TokenHash]   binary(32)       NOT NULL,
        [CollectorId] uniqueidentifier NOT NULL,
        [CreatedAt]   datetime2(3)     NOT NULL,
        [ExpiresAt]   datetime2(3)     NOT NULL,
        [UsedAt]      datetime2(3)     NULL,
        [CreatedBy]   nvarchar(200)    NOT NULL,
        CONSTRAINT [PK_EnrolmentToken] PRIMARY KEY ([TokenHash]),
        CONSTRAINT [FK_EnrolmentToken_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[Certificate]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[Certificate] (
        [Thumbprint]       char(64)         NOT NULL,
        [CollectorId]      uniqueidentifier NOT NULL,
        [SerialNumber]     varchar(64)      NOT NULL,
        [IssuedAt]         datetime2(3)     NOT NULL,
        [ExpiresAt]        datetime2(3)     NOT NULL,
        [SupersededAt]     datetime2(3)     NULL,
        [RevokedAt]        datetime2(3)     NULL,
        [RevocationReason] nvarchar(200)    NULL,
        CONSTRAINT [PK_Certificate] PRIMARY KEY ([Thumbprint]),
        CONSTRAINT [FK_Certificate_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[CollectorHealth]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[CollectorHealth] (
        [CollectorId]          uniqueidentifier NOT NULL,
        [ReceivedAt]           datetime2(3)     NOT NULL,
        [BufferDepthEvents]    int              NOT NULL,
        [BufferCapacityEvents] int              NOT NULL,
        [OverflowDroppedTotal] bigint           NOT NULL,
        [SoftwareVersion]      varchar(50)      NOT NULL,
        [ConfigVersion]        int              NOT NULL,
        [ClockOffsetMs]        int              NOT NULL,
        [ReportJson]           nvarchar(max)    NOT NULL,
        CONSTRAINT [PK_CollectorHealth] PRIMARY KEY ([CollectorId]),
        CONSTRAINT [FK_CollectorHealth_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID('[registry].[CollectorHealthHistory]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[CollectorHealthHistory] (
        [Id]                   bigint           IDENTITY NOT NULL,
        [CollectorId]          uniqueidentifier NOT NULL,
        [ReceivedAt]           datetime2(3)     NOT NULL,
        [BufferDepthEvents]    int              NOT NULL,
        [BufferCapacityEvents] int              NOT NULL,
        [OverflowDroppedTotal] bigint           NOT NULL,
        CONSTRAINT [PK_CollectorHealthHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CollectorHealthHistory_Collector] FOREIGN KEY ([CollectorId]) REFERENCES [registry].[Collector] ([CollectorId]) ON DELETE NO ACTION
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CollectorHealthHistory_CollectorId_ReceivedAt'
               AND object_id = OBJECT_ID('[registry].[CollectorHealthHistory]'))
    CREATE INDEX [IX_CollectorHealthHistory_CollectorId_ReceivedAt]
        ON [registry].[CollectorHealthHistory] ([CollectorId], [ReceivedAt]);
GO

IF OBJECT_ID('[registry].[AppUser]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[AppUser] (
        [UserId]       uniqueidentifier NOT NULL,
        [UserName]     nvarchar(100)    NOT NULL,
        [PasswordHash] nvarchar(400)    NOT NULL,
        [Kind]         varchar(20)      NOT NULL CONSTRAINT [CK_AppUser_Kind] CHECK ([Kind] IN ('ModAdmin', 'Customer')),
        [TenantId]     uniqueidentifier NULL,
        [DisplayName]  nvarchar(200)    NOT NULL,
        [CreatedAt]    datetime2(3)     NOT NULL,
        CONSTRAINT [PK_AppUser] PRIMARY KEY ([UserId]),
        CONSTRAINT [UQ_AppUser_UserName] UNIQUE ([UserName])
    );
END
GO

IF OBJECT_ID('[registry].[AuditLog]', 'U') IS NULL
BEGIN
    CREATE TABLE [registry].[AuditLog] (
        [AuditId]     bigint           IDENTITY NOT NULL,
        [At]          datetime2(3)     NOT NULL,
        [ActorName]   nvarchar(200)    NOT NULL,
        [ActorKind]   varchar(20)      NOT NULL CONSTRAINT [CK_AuditLog_ActorKind] CHECK ([ActorKind] IN ('ModAdmin', 'Collector', 'System', 'Simulator')),
        [Action]      varchar(100)     NOT NULL,
        [TargetType]  varchar(50)      NOT NULL,
        [TargetId]    varchar(100)     NOT NULL,
        [TenantId]    uniqueidentifier NULL,
        [DetailsJson] nvarchar(max)    NULL,
        CONSTRAINT [PK_AuditLog] PRIMARY KEY ([AuditId])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_TenantId_At'
               AND object_id = OBJECT_ID('[registry].[AuditLog]'))
    CREATE INDEX [IX_AuditLog_TenantId_At] ON [registry].[AuditLog] ([TenantId], [At]);
GO
