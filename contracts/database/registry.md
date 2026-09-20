# Contract: Database — schema `registry` (control plane)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Conventions: see `overview.md` (types, timestamps, GUIDs).

**registry.Tenant** — PK `TenantId`
`TenantId` uniqueidentifier · `Name` nvarchar(200) · `Slug` varchar(50) unique · `IsolationMode` varchar(20) CHECK = `'Pooled'` (PoC supports only Pooled) · `Status` varchar(20) `'Active'` · `CreatedAt`

**registry.Site** — PK `SiteId`, FK Tenant
`SiteId` · `TenantId` · `Name` nvarchar(200) · `RegionLabel` nvarchar(50) (grouping label used for regional scenarios, e.g. `EU-North`) · `TimeZone` varchar(64) IANA · `CreatedAt`

**registry.Line** — PK `LineId`, FK Site
`LineId` · `TenantId` · `SiteId` · `Name` nvarchar(200) · `CreatedAt`

**registry.Asset** — PK `AssetId`, FK Line
`AssetId` · `TenantId` · `SiteId` · `LineId` · `Name` nvarchar(200) · `AssetType` nvarchar(50) · `CounterName` varchar(50) default `'good_count'` · `ProcessValueName` varchar(50) · `ProcessValueUnit` varchar(20) · `ProcessValueMin` float · `ProcessValueMax` float · `CreatedAt`

**registry.Collector** — PK `CollectorId`, FK Site
`CollectorId` · `TenantId` · `SiteId` · `Name` nvarchar(200) · `Status` varchar(20) CHECK in (`Registered`, `Enrolled`, `Revoked`, `Retired`) · `CommandChannelEnabled` bit · `CreatedAt` · `EnrolledAt` null · `LastSeenAt` null · `SoftwareVersion` varchar(50) null · `ReportedConfigVersion` int null · `ReportedConfigHash` char(64) null

**registry.AssetSourceMapping** — PK (`CollectorId`, `SourceId`, `ValidFrom`)
`CollectorId` · `SourceId` varchar(100) · `AssetId` · `TenantId` · `ValidFrom` · `ValidTo` null (null = active; at most one active row per (CollectorId, SourceId))

**registry.CollectorConfig** — PK `CollectorId`
`CollectorId` · `Version` int (starts at 1; incremented on every settings or mapping change) · `SettingsJson` nvarchar(max) (JSON with `batching`, `forwarder`, `buffer`, `intervals` objects as in ConfigDocument) · `UpdatedAt` · `UpdatedBy` nvarchar(200)

**registry.EnrolmentToken** — PK `TokenHash`
`TokenHash` binary(32) (SHA-256 of the token string) · `CollectorId` · `CreatedAt` · `ExpiresAt` (created + 24 h) · `UsedAt` null · `CreatedBy` nvarchar(200)

**registry.Certificate** — PK `Thumbprint`
`Thumbprint` char(64) · `CollectorId` · `SerialNumber` varchar(64) · `IssuedAt` · `ExpiresAt` · `SupersededAt` null · `RevokedAt` null · `RevocationReason` nvarchar(200) null

**registry.CollectorHealth** — PK `CollectorId` (latest report)
`CollectorId` · `ReceivedAt` · `BufferDepthEvents` int · `BufferCapacityEvents` int · `OverflowDroppedTotal` bigint · `SoftwareVersion` · `ConfigVersion` int · `ClockOffsetMs` int · `ReportJson` nvarchar(max) (full HealthReport)

**registry.CollectorHealthHistory** — PK `Id` bigint identity; index (`CollectorId`, `ReceivedAt`)
`Id` · `CollectorId` · `ReceivedAt` · `BufferDepthEvents` · `BufferCapacityEvents` · `OverflowDroppedTotal`. Rows older than 7 days are purged by management-api hourly.

**registry.AppUser** — PK `UserId`
`UserId` · `UserName` nvarchar(100) unique · `PasswordHash` nvarchar(400) (ASP.NET Core Identity `PasswordHasher` v3 format) · `Kind` varchar(20) CHECK in (`ModAdmin`, `Customer`) · `TenantId` null (required when Kind = Customer) · `DisplayName` nvarchar(200) · `CreatedAt`

**registry.AuditLog** — PK `AuditId` bigint identity; index (`TenantId`, `At`)
`AuditId` · `At` · `ActorName` nvarchar(200) · `ActorKind` varchar(20) (`ModAdmin`, `Collector`, `System`, `Simulator`) · `Action` varchar(100) · `TargetType` varchar(50) · `TargetId` varchar(100) · `TenantId` null · `DetailsJson` nvarchar(max) null
