-- RLS predicate: true when caller is not role_reporting, OR when TenantId matches session context
IF OBJECT_ID('[security].[fn_TenantPredicate]', 'IF') IS NOT NULL
    DROP FUNCTION [security].[fn_TenantPredicate];
GO

CREATE FUNCTION [security].[fn_TenantPredicate] (@TenantId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
(
    SELECT 1 AS [Result]
    WHERE
        IS_ROLEMEMBER('role_reporting') = 0
        OR CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) = @TenantId
);
GO

-- Drop and recreate policy so it reflects current table set
IF EXISTS (SELECT 1 FROM sys.security_policies
           WHERE name = 'TenantPolicy' AND schema_id = SCHEMA_ID('security'))
    DROP SECURITY POLICY [security].[TenantPolicy];
GO

CREATE SECURITY POLICY [security].[TenantPolicy]
    -- registry tables with TenantId (except AppUser)
    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Tenant],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Tenant]  AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Tenant]  AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Site],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Site]    AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Site]    AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Line],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Line]    AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Line]    AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Asset],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Asset]   AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Asset]   AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Collector],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Collector] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[Collector] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[AssetSourceMapping],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[AssetSourceMapping] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[AssetSourceMapping] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[AuditLog],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[AuditLog] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [registry].[AuditLog] AFTER UPDATE,

    -- telemetry tables with TenantId
    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[RawEvent],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[RawEvent]  AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[RawEvent]  AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[SourceSequence],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[SourceSequence] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[SourceSequence] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[SequenceGap],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[SequenceGap] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[SequenceGap] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[RollupMinute],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[RollupMinute] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[RollupMinute] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[CollectorFreshness],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[CollectorFreshness] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[CollectorFreshness] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[Reconciliation],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[Reconciliation] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[Reconciliation] AFTER UPDATE,

    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[DeadLetter],
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[DeadLetter] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [telemetry].[DeadLetter] AFTER UPDATE

WITH (STATE = ON);
GO
