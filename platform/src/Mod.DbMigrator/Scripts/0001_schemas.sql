IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'registry')
    EXEC('CREATE SCHEMA [registry]');
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'telemetry')
    EXEC('CREATE SCHEMA [telemetry]');
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'sim')
    EXEC('CREATE SCHEMA [sim]');
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'security')
    EXEC('CREATE SCHEMA [security]');
GO
