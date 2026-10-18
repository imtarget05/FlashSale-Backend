using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpsBridge.Api.Endpoints;
using OpsBridge.Infrastructure;
using OpsBridge.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

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
var signingKey = builder.Configuration["Jwt:SigningKey"] ?? OpsBridgeJwtTokenIssuer.DefaultSigningKey;
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
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("OpsBridgeWebPolicy");
app.UseAuthentication();
app.UseAuthorization();

// Liveness & readiness probes
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy", service = "OpsBridge.Api", timestamp = DateTimeOffset.UtcNow }))
   .WithTags("Health");

// Map Domain & Ingestion Endpoints
app.MapAuthEndpoints();
app.MapWebhookEndpoints();
app.MapConnectorEndpoints();
app.MapIngestionEndpoints();
app.MapReplayEndpoints();
app.MapReconciliationEndpoints();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
