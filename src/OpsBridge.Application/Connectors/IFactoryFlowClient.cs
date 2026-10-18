using OpsBridge.Domain.Contracts;

namespace OpsBridge.Application.Connectors;

public sealed record WebhookDeliveryResult(
    bool Success,
    int StatusCode,
    string ResponseBody
);

public interface IFactoryFlowClient
{
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<GoodsReceiptConfirmedV1?> GetGoodsReceiptAsync(string receiptNumber, CancellationToken ct = default);
    Task<WebhookDeliveryResult> SendGoodsReceiptWebhookAsync(string targetWebhookUrl, GoodsReceiptConfirmedV1 payload, string secret, CancellationToken ct = default);
}
