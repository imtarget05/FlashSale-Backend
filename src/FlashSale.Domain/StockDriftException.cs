namespace FlashSale.Domain;

/// <summary>
/// Raised when the authoritative stock level is insufficient to fulfill an
/// order that was previously reserved. The message is dead-lettered and the
/// ops runbook reconciles the reservation tier (ADR-003).
/// </summary>
public sealed class StockDriftException(string message) : Exception(message);
