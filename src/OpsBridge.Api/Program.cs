using OpsBridge.Api.Endpoints;
using OpsBridge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Clean Architecture Composition Root: OpsBridge API
// Infrastructure adapters are registered via AddOpsBridgeInfrastructure
// ------------------------------------------------------------------
builder.Services.AddOpsBridgeInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Liveness & readiness probes
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy", service = "OpsBridge.Api", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

// Map Domain & Ingestion Endpoints
app.MapWebhookEndpoints();
app.MapConnectorEndpoints();
app.MapIngestionEndpoints();
app.MapReplayEndpoints();
app.MapReconciliationEndpoints();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
