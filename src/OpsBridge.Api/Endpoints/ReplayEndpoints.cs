using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsBridge.Api.Auth;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.UseCases;

namespace OpsBridge.Api.Endpoints;

public static class ReplayEndpoints
{
    public static IEndpointRouteBuilder MapReplayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events")
            .WithTags("Event Replay")
            .RequireAuthorization(OpsBridgeAuthPolicies.OperatorOrAdmin);

        group.MapPost("/replay/{eventId:guid}", async (
            Guid eventId,
            HttpContext context,
            ReplayDeadLetterEventUseCase replayUseCase,
            CancellationToken ct) =>
        {
            var actor = context.User.Identity?.Name ?? "operator";
            var result = await replayUseCase.ExecuteAsync(eventId, actor, ct);
            return result.Status switch
            {
                DeadLetterReplayStatus.NotFound => Results.NotFound(new
                {
                    error = "NotFound",
                    message = $"No dead-letter event found with ID {eventId}."
                }),
                DeadLetterReplayStatus.AlreadyReplayed => Results.Ok(new
                {
                    eventId,
                    status = "AlreadyReplayed",
                    message = "Event has already been replayed.",
                    replayedAt = result.ReplayedAt,
                    replayedBy = result.ReplayedBy
                }),
                _ => Results.Ok(new
                {
                    eventId,
                    status = "ReplayQueued",
                    message = "Dead-letter event successfully re-queued for processing.",
                    outboxId = result.OutboxId
                })
            };
        })
        .RequireAuthorization(OpsBridgeAuthPolicies.OperatorOrAdmin)
        .WithName("ReplayDeadLetterEvent")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}
