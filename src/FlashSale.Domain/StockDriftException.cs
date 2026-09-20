namespace FlashSale.Domain;

/// <summary>
/// Raised when the authoritative PostgreSQL stock disagrees with the
/// reservation tier (Redis/DB drift). Never retried blindly — the message is
/// dead-lettered and the resync runbook reconciles (ADR-003).
/// </summary>
public sealed class StockDriftException(string message) : Exception(message);
