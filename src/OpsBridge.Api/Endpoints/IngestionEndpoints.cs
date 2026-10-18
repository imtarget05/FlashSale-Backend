using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsBridge.Application.Ingestion;

namespace OpsBridge.Api.Endpoints;

public static class IngestionEndpoints
{
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ingestion")
            .WithTags("Ingestion");

        group.MapGet("/events", async (IIngestedEventRepository repo, int limit = 50, CancellationToken ct = default) =>
        {
            var events = await repo.GetRecentAsync(Math.Clamp(limit, 1, 100), ct);
            return Results.Ok(events.Select(e => new
            {
                e.EventId,
                e.EventType,
                e.SchemaVersion,
                e.Source,
                e.OccurredAt,
                e.CorrelationId,
                Status = e.Status.ToString(),
                e.ErrorMessage,
                e.ReceivedAt,
                e.ProcessedAt
            }));
        })
        .WithName("ListRecentIngestedEvents");

        group.MapGet("/events/{id:guid}", async (Guid id, IIngestedEventRepository repo, CancellationToken ct) =>
        {
            var ev = await repo.GetByIdAsync(id, ct);
            if (ev == null)
            {
                return Results.NotFound(new { error = "NotFound", message = $"Ingested event with ID {id} not found." });
            }

            return Results.Ok(new
            {
                ev.EventId,
                ev.EventType,
                ev.SchemaVersion,
                ev.Source,
                ev.OccurredAt,
                ev.CorrelationId,
                Status = ev.Status.ToString(),
                ev.ErrorMessage,
                ev.ReceivedAt,
                ev.ProcessedAt,
                ev.RawPayload
            });
        })
        .WithName("GetIngestedEventById");

        return app;
    }
}
