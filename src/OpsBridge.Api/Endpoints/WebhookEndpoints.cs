using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsBridge.Application.UseCases;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.Exceptions;

namespace OpsBridge.Api.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks")
            .WithTags("Webhooks");

        group.MapPost("/factoryflow", async (
            HttpContext context,
            ReceiveWebhookUseCase useCase,
            CancellationToken ct) =>
        {
            // Support both Svix standards and OpsBridge custom headers
            var signatureHeader = context.Request.Headers["svix-signature"].FirstOrDefault()
                ?? context.Request.Headers["X-OpsBridge-Signature"].FirstOrDefault();

            var timestampHeader = context.Request.Headers["svix-timestamp"].FirstOrDefault()
                ?? context.Request.Headers["X-OpsBridge-Timestamp"].FirstOrDefault();

            var messageIdHeader = context.Request.Headers["svix-id"].FirstOrDefault()
                ?? context.Request.Headers["X-OpsBridge-Event-Id"].FirstOrDefault();

            // Read raw body stream
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync(ct);

            try
            {
                var result = await useCase.ExecuteAsync(
                    rawBody,
                    signatureHeader,
                    timestampHeader,
                    messageIdHeader,
                    ConnectorType.FactoryFlowWebhook,
                    ct);

                if (result.IsDuplicate)
                {
                    return Results.Ok(new
                    {
                        eventId = result.EventId,
                        status = "Duplicate",
                        message = result.Message
                    });
                }

                return Results.Accepted($"/api/v1/ingestion/events/{result.EventId}", new
                {
                    eventId = result.EventId,
                    status = "Accepted",
                    message = result.Message
                });
            }
            catch (EventPayloadConflictException ex)
            {
                return Results.Json(new { error = "EventPayloadConflict", message = ex.Message, eventId = ex.EventId }, statusCode: StatusCodes.Status409Conflict);
            }
            catch (SignatureValidationException ex)
            {
                return Results.Json(new { error = "SignatureVerificationFailed", message = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (TimestampExpiredException ex)
            {
                return Results.Json(new { error = "TimestampExpired", message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidEventContractException ex)
            {
                return Results.Json(new { error = "InvalidContract", message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (DomainException ex)
            {
                return Results.Json(new { error = "DomainError", message = ex.Message }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .WithName("ReceiveFactoryFlowWebhook")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
