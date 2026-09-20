using System.Net.Security;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Mod.Collector;
using Mod.Collector.Buffer;
using Mod.Collector.Commands;
using Mod.Collector.Config;
using Mod.Collector.Edge;
using Mod.Collector.Forwarding;
using Mod.Collector.Health;
using Mod.Collector.Identity;
using Mod.Collector.Network;
using Mod.Collector.Simulation;
using Mod.Collector.Sources;

var options = CollectorOptions.Load();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(8080));

// Capture Warning+ logs for GetDiagnostics responses.
var logCapture = new LogCapture();
builder.Logging.ClearProviders().AddConsole().AddProvider(logCapture)
    .SetMinimumLevel(options.ParsedLogLevel);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(logCapture);

// Core singletons (no hosted-service role).
builder.Services.AddSingleton<LinkGate>();
builder.Services.AddSingleton<MinuteCounter>();
builder.Services.AddSingleton<EdgeValidator>();
builder.Services.AddSingleton<InvalidDataInjector>();
builder.Services.AddSingleton<SqliteBuffer>();

// Sim state (DEMO SCAFFOLDING).
builder.Services.AddSingleton<SimState>();

// Identity.
builder.Services.AddSingleton<IdentityStore>();
builder.Services.AddSingleton<EnrolmentClient>();

// Background services registered as singletons so other services can hold references.
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConfigService>());

builder.Services.AddSingleton<Forwarder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Forwarder>());

builder.Services.AddSingleton<HealthReporter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HealthReporter>());

builder.Services.AddSingleton<ProductCommandListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProductCommandListener>());

builder.Services.AddSingleton<SimStatusPublisher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimStatusPublisher>());

builder.Services.AddSingleton<SimChannelListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimChannelListener>());

// Default named HTTP client — carries the client certificate after enrolment.
builder.Services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var identity = sp.GetRequiredService<IdentityStore>();
        var opts = sp.GetRequiredService<CollectorOptions>();
        var handler = new SocketsHttpHandler();
        var sslOpts = new SslClientAuthenticationOptions
        {
            LocalCertificateSelectionCallback = (_, _, _, _, _) =>
                identity.HasValidCertificate ? identity.GetCertificateWithKey() : null,
        };
        if (opts.LocalInsecureTls)
            sslOpts.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        handler.SslOptions = sslOpts;
        return handler;
    });

// "noClientCert" client — used only for POST /v1/enrol (no certificate yet).
builder.Services.AddHttpClient("noClientCert")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new SocketsHttpHandler();
        if (options.LocalInsecureTls)
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
        return handler;
    });

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok());

await app.RunAsync();
