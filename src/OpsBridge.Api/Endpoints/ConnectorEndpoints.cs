using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsBridge.Application.Connectors;
using OpsBridge.Domain.Connectors;

namespace OpsBridge.Api.Endpoints;

public static class ConnectorEndpoints
{
    public static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/connectors")
            .WithTags("Connectors");

        group.MapGet("/", async (IConnectorRepository repo, CancellationToken ct) =>
        {
            var connectors = await repo.GetAllAsync(ct);
            return Results.Ok(connectors.Select(c => new
            {
                c.Id,
                c.Name,
                Type = c.Type.ToString(),
                c.BaseUrl,
                c.IsActive,
                c.CreatedAt,
                c.UpdatedAt
            }));
        })
        .WithName("ListConnectors");

        group.MapGet("/{id:guid}", async (Guid id, IConnectorRepository repo, CancellationToken ct) =>
        {
            var connector = await repo.GetByIdAsync(id, ct);
            if (connector == null)
            {
                return Results.NotFound(new { error = "NotFound", message = $"Connector with ID {id} not found." });
            }

            return Results.Ok(new
            {
                connector.Id,
                connector.Name,
                Type = connector.Type.ToString(),
                connector.BaseUrl,
                connector.IsActive,
                connector.CreatedAt,
                connector.UpdatedAt
            });
        })
        .WithName("GetConnectorById");

        group.MapPost("/", async (SaveConnectorDto dto, IConnectorRepository repo, CancellationToken ct) =>
        {
            var connector = new ConnectorConfig
            {
                Id = dto.Id ?? Guid.NewGuid(),
                Name = dto.Name,
                Type = dto.Type,
                Secret = dto.Secret,
                BaseUrl = dto.BaseUrl,
                IsActive = dto.IsActive ?? true
            };

            await repo.SaveAsync(connector, ct);

            return Results.Created($"/api/v1/connectors/{connector.Id}", new
            {
                connector.Id,
                connector.Name,
                Type = connector.Type.ToString(),
                connector.IsActive
            });
        })
        .WithName("SaveConnector");

        return app;
    }
}

public sealed record SaveConnectorDto(
    Guid? Id,
    string Name,
    ConnectorType Type,
    string Secret,
    string? BaseUrl,
    bool? IsActive
);
