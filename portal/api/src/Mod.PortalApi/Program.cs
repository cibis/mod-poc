using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Mod.PortalApi.Auth;
using Mod.PortalApi.Features.Admin;
using Mod.PortalApi.Features.Simulation;

var jwtKey = GetRequired("PORTAL_JWT_KEY");
var sqlConnection = GetRequired("SQL_CONNECTION");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new JwtConfig(jwtKey));
builder.Services.AddSingleton(new SqlConfig(sqlConnection));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var signingKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtKey));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "mod-poc-portal",
            ValidateAudience = true,
            ValidAudience = "mod-poc-portal",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero,
        };
        // SignalR passes the token as ?access_token= query param
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("AdminOnly", p => p.RequireClaim("kind", "ModAdmin")));

builder.Services.AddSignalR();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddAdmin();
builder.Services.AddSimulation();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok()).AllowAnonymous();

app.MapGet("/readyz", async () =>
{
    try
    {
        await using var conn = new SqlConnection(sqlConnection);
        await conn.OpenAsync();
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Problem(type: "DependencyUnavailable", statusCode: 503, title: ex.Message);
    }
}).AllowAnonymous();

app.MapLoginEndpoint();
app.MapAdmin();
app.MapSimulation();

app.MapFallbackToFile("index.html");

app.Run();

static string GetRequired(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

internal sealed record SqlConfig(string ConnectionString);
internal sealed record JwtConfig(string Key);
