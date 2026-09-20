using System.Security.Claims;
using System.Text;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mod.ReportingApi.Auth;
using Mod.ReportingApi.Data;
using Mod.ReportingApi.Endpoints;
using Mod.ReportingApi.Live;

var builder = WebApplication.CreateBuilder(args);

// --- Required configuration validation ---
var jwtKey = builder.Configuration["REPORTING_JWT_KEY"]
    ?? throw new InvalidOperationException("REPORTING_JWT_KEY is required");
var sqlConnection = builder.Configuration["SQL_CONNECTION"]
    ?? throw new InvalidOperationException("SQL_CONNECTION is required");

// --- Authentication & authorisation ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "mod-poc-reporting",
            ValidAudience = "mod-poc-reporting",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
        // SignalR passes the token as the access_token query parameter
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(token))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("CustomerOnly", p => p.RequireClaim("kind", "Customer"));
});

// --- Tenant context (scoped — one per request / scope) ---
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// --- Data ---
builder.Services.AddScoped<TenantSessionInterceptor>();
builder.Services.AddDbContext<ReportingDbContext>((sp, opts) =>
{
    opts.UseSqlServer(sqlConnection)
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
        .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>());
});
builder.Services.AddSingleton<LoginConnectionFactory>();

// --- SignalR ---
builder.Services.AddSignalR();

// --- Live hub support ---
builder.Services.AddSingleton<ConnectedTenantTracker>();
builder.Services.AddHostedService<RollupChangePoller>();

// --- OpenTelemetry + Azure Monitor ---
var appInsightsConn = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConn))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// --- Build app ---
var app = builder.Build();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Populate TenantContext from JWT claims for every authenticated request
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var tenantCtx = ctx.RequestServices.GetRequiredService<TenantContext>();
        var tenantIdClaim = ctx.User.FindFirstValue("tenant_id");
        if (Guid.TryParse(tenantIdClaim, out var tid))
            tenantCtx.TenantId = tid;
    }
    await next(ctx);
});

// --- Endpoints ---
app.MapPost("/api/auth/login", LoginEndpoint.Handle);

app.MapGet("/api/me", MeEndpoint.Handle)
    .RequireAuthorization("CustomerOnly");

app.MapGet("/api/hierarchy", HierarchyEndpoint.Handle)
    .RequireAuthorization("CustomerOnly");

app.MapGet("/api/sites/status", SiteStatusEndpoint.Handle)
    .RequireAuthorization("CustomerOnly");

app.MapGet("/api/sites/{siteId:guid}/summary", RollupsEndpoint.HandleSiteSummary)
    .RequireAuthorization("CustomerOnly");

app.MapGet("/api/assets/{assetId:guid}/rollups", RollupsEndpoint.HandleAssetRollups)
    .RequireAuthorization("CustomerOnly");

// --- SignalR hub ---
app.MapHub<LiveHub>("/hubs/live");

// --- Health ---
app.MapGet("/healthz", () => Results.Ok());
app.MapGet("/readyz", async (LoginConnectionFactory factory, CancellationToken ct) =>
{
    try
    {
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok();
    }
    catch
    {
        return Results.Problem("Database not reachable", statusCode: 503);
    }
});

// --- SPA fallback (must be last) ---
app.MapFallbackToFile("index.html");

app.Run();
