using DbUp;
using Mod.DbMigrator.Steps;

var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION")
    ?? throw new InvalidOperationException("SQL_CONNECTION is required.");

var seedDemoData = !string.Equals(
    Environment.GetEnvironmentVariable("SEED_DEMO_DATA"), "false",
    StringComparison.OrdinalIgnoreCase);

try
{
    Console.WriteLine("Step 1/3: running schema migrations…");
    var upgrader = DeployChanges.To
        .SqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
        .WithTransactionPerScript()
        .LogToConsole()
        .Build();

    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
    {
        Console.Error.WriteLine($"Migration failed: {result.Error.Message}");
        return 1;
    }
    Console.WriteLine("Schema migrations complete.");

    Console.WriteLine("Step 2/3: provisioning database users…");
    await new UsersStep(connectionString).RunAsync();
    Console.WriteLine("Users step complete.");

    if (seedDemoData)
    {
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? throw new InvalidOperationException("ADMIN_PASSWORD is required when SEED_DEMO_DATA=true.");
        var customerPassword = Environment.GetEnvironmentVariable("CUSTOMER_PASSWORD")
            ?? throw new InvalidOperationException("CUSTOMER_PASSWORD is required when SEED_DEMO_DATA=true.");

        Console.WriteLine("Step 3/3: seeding demo dataset…");
        await new SeedStep(connectionString, adminPassword, customerPassword).RunAsync();
        Console.WriteLine("Seed step complete.");
    }
    else
    {
        Console.WriteLine("Step 3/3: seed skipped (SEED_DEMO_DATA=false).");
    }

    Console.WriteLine("Database migration completed successfully.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
