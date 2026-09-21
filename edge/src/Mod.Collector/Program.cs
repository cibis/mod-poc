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

var logCapture = new LogCapture();
builder.Logging.ClearProviders().AddConsole().AddProvider(logCapture)
    .SetMinimumLevel(options.ParsedLogLevel);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(logCapture);

builder.Services.AddSingleton<LinkGate>();
builder.Services.AddSingleton<MinuteCounter>();
builder.Services.AddSingleton<EdgeValidator>();
builder.Services.AddSingleton<InvalidDataInjector>();
builder.Services.AddSingleton<SqliteBuffer>();

// Sim state (DEMO SCAFFOLDING).
builder.Services.AddSingleton<SimState>();

// Identity — shared cert loaded from env var at startup (no enrollment HTTP call).
builder.Services.AddSingleton<IdentityStore>();

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

// HTTP client: carries the shared client cert (ClientCertificates for Linux/OpenSSL compat)
// and injects X-Collector-Id on every outbound request so the platform APIs know which
// collector is calling without reading the cert CN.
// PoC: one cert for all collectors; no per-collector enrollment or renewal.
// Production: two named clients — default (with per-collector cert) and "noClientCert"
// (used only for POST /v1/enrol before a cert exists).
builder.Services.AddTransient<CollectorIdHandler>();
builder.Services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var identity = sp.GetRequiredService<IdentityStore>();
        var opts = sp.GetRequiredService<CollectorOptions>();
        var handler = new SocketsHttpHandler();
        var sslOpts = new SslClientAuthenticationOptions
        {
            ClientCertificates = identity.TlsCertCollection,
            LocalCertificateSelectionCallback = (_, _, localCerts, _, _) =>
                localCerts.Count > 0
                    ? (System.Security.Cryptography.X509Certificates.X509Certificate2)localCerts[0]
                    : null,
        };
        if (opts.LocalInsecureTls)
            sslOpts.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        handler.SslOptions = sslOpts;
        return handler;
    })
    .AddHttpMessageHandler<CollectorIdHandler>();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok());

await app.RunAsync();
