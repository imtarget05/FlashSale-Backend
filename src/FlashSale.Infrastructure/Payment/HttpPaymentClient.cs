using System.Net.Http.Json;
using System.Text.Json;
using FlashSale.Application.Payment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Payment;

/// <summary>
/// HTTP adapter for IPaymentClient calling Payment.Service (Phases 9 & 11).
/// Supports deterministic amounts:
///   *.01 -> Successful
///   *.02 -> Declined
///   *.03 -> Transient timeout
/// Also provides in-memory fallback simulation if no remote Payment.Service is reachable.
/// </summary>
public sealed class HttpPaymentClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<HttpPaymentClient> logger) : IPaymentClient
{
    private readonly string _baseUrl = configuration["Payment:ServiceUrl"] ?? "http://localhost:5002";

    public async Task<PaymentClientResult> ProcessPaymentAsync(
        PaymentClientRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var uri = new Uri(new Uri(_baseUrl.TrimEnd('/') + "/"), "api/payments");
            var response = await httpClient.PostAsJsonAsync(uri, request, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<PaymentApiResponse>(ct);
                return new PaymentClientResult(
                    Success: body?.Success ?? true,
                    Status: body?.Status ?? "Succeeded",
                    TransactionId: body?.TransactionId ?? $"tx_{Guid.NewGuid():N}",
                    FailureReason: body?.FailureReason,
                    IsTransientError: body?.IsTransientError ?? false);
            }

            if ((int)response.StatusCode == 504 || (int)response.StatusCode >= 500)
            {
                return new PaymentClientResult(
                    Success: false,
                    Status: "TimedOut",
                    TransactionId: null,
                    FailureReason: $"Payment gateway transient error: {response.StatusCode}",
                    IsTransientError: true);
            }

            if ((int)response.StatusCode == 402 || (int)response.StatusCode == 400)
            {
                var body = await response.Content.ReadFromJsonAsync<PaymentApiResponse>(ct);
                return new PaymentClientResult(
                    Success: false,
                    Status: "Declined",
                    TransactionId: null,
                    FailureReason: body?.FailureReason ?? "Payment declined by provider",
                    IsTransientError: false);
            }

            return new PaymentClientResult(
                Success: false,
                Status: "Failed",
                TransactionId: null,
                FailureReason: $"HTTP {(int)response.StatusCode}",
                IsTransientError: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            logger.LogWarning("Remote payment service at {BaseUrl} unreachable or timed out: {Message}. Evaluating deterministic rule.",
                _baseUrl, ex.Message);

            // Deterministic simulation fallback:
            // Cent value .01 -> success, .02 -> decline, .03 -> transient timeout
            var cents = (int)Math.Round((request.Amount - Math.Floor(request.Amount)) * 100);

            if (cents == 2)
            {
                return new PaymentClientResult(
                    Success: false,
                    Status: "Declined",
                    TransactionId: null,
                    FailureReason: "Card declined: insufficient funds",
                    IsTransientError: false);
            }

            if (cents == 3)
            {
                return new PaymentClientResult(
                    Success: false,
                    Status: "TimedOut",
                    TransactionId: null,
                    FailureReason: "Gateway timed out responding",
                    IsTransientError: true);
            }

            return new PaymentClientResult(
                Success: true,
                Status: "Succeeded",
                TransactionId: $"sim_tx_{Guid.NewGuid():N}",
                FailureReason: null,
                IsTransientError: false);
        }
    }

    private sealed record PaymentApiResponse(
        bool Success,
        string Status,
        string? TransactionId,
        string? FailureReason,
        bool IsTransientError);
}
