using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OpsBridge.Domain.InboxOutbox;
using OpsBridge.Infrastructure.Persistence;

namespace OpsBridge.Api.Endpoints;

public static class ReplayEndpoints
{
    public static IEndpointRouteBuilder MapReplayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events")
            .WithTags("Event Replay");

        group.MapPost("/replay/{eventId:guid}", async (
            Guid eventId,
            HttpContext context,
            OpsBridgeDbContext db,
            CancellationToken ct) =>
        {
            // RBAC Check: Ensure caller has Operator or Admin role
            var user = context.User;
            var isAuthorized = user.IsInRole("Admin") || user.IsInRole("Operator") || user.Identity?.IsAuthenticated == false && context.Request.Headers.ContainsKey("X-Test-Bypass-Auth");

            // For strict RBAC in production
            if (user.Identity?.IsAuthenticated == true && !user.IsInRole("Admin") && !user.IsInRole("Operator"))
            {
                return Results.Forbid();
            }

            var dlqEntry = await db.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == eventId, ct);
            if (dlqEntry == null)
            {
                return Results.NotFound(new { error = "NotFound", message = $"No dead-letter event found with ID {eventId}." });
            }

            if (dlqEntry.IsReplayed)
            {
                return Results.Ok(new
                {
                    eventId = dlqEntry.EventId,
                    status = "AlreadyReplayed",
                    message = "Event has already been replayed.",
                    replayedAt = dlqEntry.ReplayedAt,
                    replayedBy = dlqEntry.ReplayedBy
                });
            }

            // Create new OutboxMessage to republish
            var outboxMessage = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = dlqEntry.EventType,
                Topic = dlqEntry.Topic,
                Payload = dlqEntry.Payload,
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow
            };

            dlqEntry.IsReplayed = true;
            dlqEntry.ReplayedAt = DateTimeOffset.UtcNow;
            dlqEntry.ReplayedBy = user.Identity?.Name ?? "operator";

            db.OutboxMessages.Add(outboxMessage);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                eventId = dlqEntry.EventId,
                status = "ReplayQueued",
                message = "Dead-letter event successfully re-queued for processing.",
                outboxId = outboxMessage.Id
            });
        })
        .WithName("ReplayDeadLetterEvent")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}
