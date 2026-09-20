-- Create application roles
IF DATABASE_PRINCIPAL_ID('role_ingest') IS NULL
    CREATE ROLE [role_ingest];
GO
IF DATABASE_PRINCIPAL_ID('role_processing') IS NULL
    CREATE ROLE [role_processing];
GO
IF DATABASE_PRINCIPAL_ID('role_management') IS NULL
    CREATE ROLE [role_management];
GO
IF DATABASE_PRINCIPAL_ID('role_portal') IS NULL
    CREATE ROLE [role_portal];
GO
IF DATABASE_PRINCIPAL_ID('role_reporting') IS NULL
    CREATE ROLE [role_reporting];
GO

-- role_ingest: SELECT on Collector and Certificate only
GRANT SELECT ON [registry].[Collector]   TO [role_ingest];
GRANT SELECT ON [registry].[Certificate] TO [role_ingest];
DENY  SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[sim] TO [role_ingest];
GO

-- role_processing: SELECT registry subset; full DML telemetry
GRANT SELECT ON [registry].[Collector]          TO [role_processing];
GRANT SELECT ON [registry].[Site]               TO [role_processing];
GRANT SELECT ON [registry].[Asset]              TO [role_processing];
GRANT SELECT ON [registry].[AssetSourceMapping] TO [role_processing];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[telemetry] TO [role_processing];
DENY  SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[sim] TO [role_processing];
GO

-- role_management: SELECT all registry; targeted writes
GRANT SELECT ON SCHEMA::[registry] TO [role_management];
GRANT INSERT ON [registry].[Certificate]             TO [role_management];
GRANT UPDATE ON [registry].[Certificate]             TO [role_management];
GRANT INSERT ON [registry].[CollectorHealth]         TO [role_management];
GRANT UPDATE ON [registry].[CollectorHealth]         TO [role_management];
GRANT INSERT ON [registry].[CollectorHealthHistory]  TO [role_management];
GRANT UPDATE ON [registry].[CollectorHealthHistory]  TO [role_management];
GRANT DELETE ON [registry].[CollectorHealthHistory]  TO [role_management];
GRANT UPDATE ON [registry].[Collector]               TO [role_management];
GRANT UPDATE ON [registry].[EnrolmentToken]          TO [role_management];
GRANT INSERT ON [registry].[AuditLog]                TO [role_management];
GRANT SELECT, INSERT, UPDATE ON [telemetry].[Reconciliation] TO [role_management];
DENY  SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[sim] TO [role_management];
GO

-- role_portal: full DML on registry and sim; SELECT telemetry + UPDATE DeadLetter
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[registry]  TO [role_portal];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[sim]       TO [role_portal];
GRANT SELECT ON SCHEMA::[telemetry]                         TO [role_portal];
GRANT UPDATE ON [telemetry].[DeadLetter]                    TO [role_portal];
GO

-- role_reporting: targeted SELECT; DENY sim
GRANT SELECT ON [registry].[Tenant]                   TO [role_reporting];
GRANT SELECT ON [registry].[Site]                     TO [role_reporting];
GRANT SELECT ON [registry].[Line]                     TO [role_reporting];
GRANT SELECT ON [registry].[Asset]                    TO [role_reporting];
GRANT SELECT ON [registry].[AppUser]                  TO [role_reporting];
GRANT SELECT ON [telemetry].[RollupMinute]            TO [role_reporting];
GRANT SELECT ON [telemetry].[CollectorFreshness]      TO [role_reporting];
GRANT SELECT ON [telemetry].[Reconciliation]          TO [role_reporting];
GRANT SELECT ON [telemetry].[SequenceGap]             TO [role_reporting];
DENY  SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[sim] TO [role_reporting];
GO
