using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpsBridge.Api.Auth;
using OpsBridge.Api.Endpoints;
using OpsBridge.Infrastructure;
using OpsBridge.Infrastructure.Security;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();

// ------------------------------------------------------------------
// Clean Architecture Composition Root: OpsBridge API
// Infrastructure adapters are registered via AddOpsBridgeInfrastructure
// ------------------------------------------------------------------
builder.Services.AddOpsBridgeInfrastructure(builder.Configuration);

// CORS for Vite frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpsBridgeWebPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Authentication & Authorization
var configuredSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(configuredSigningKey))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Jwt:SigningKey must be configured in Production.");
    }

    configuredSigningKey = OpsBridgeJwtTokenIssuer.DefaultSigningKey;
}

if (builder.Environment.IsProduction() &&
    string.Equals(configuredSigningKey, OpsBridgeJwtTokenIssuer.DefaultSigningKey, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Jwt:SigningKey must not use the built-in development key in Production.");
}

if (Encoding.UTF8.GetByteCount(configuredSigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
}

var signingKey = configuredSigningKey;
var issuer = builder.Configuration["Jwt:Issuer"] ?? OpsBridgeJwtTokenIssuer.DefaultIssuer;
var audience = builder.Configuration["Jwt:Audience"] ?? OpsBridgeJwtTokenIssuer.DefaultAudience;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization(options => options.AddOpsBridgePolicies());

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "opsbridge-api",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("OpsBridgeWebPolicy");
app.UseHttpMetrics(options => options.ReduceStatusCodeCardinality());
app.UseAuthentication();
app.UseAuthorization();

// Liveness & readiness probes
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy", service = "OpsBridge.Api", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

// Map Domain & Ingestion Endpoints
app.MapAuthEndpoints(app.Environment);
app.MapWebhookEndpoints();
app.MapConnectorEndpoints();
app.MapIngestionEndpoints();
app.MapReplayEndpoints();
app.MapReconciliationEndpoints();
app.MapMetrics("/metrics").AllowAnonymous().WithTags("Metrics");

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
