namespace FlashSale.Domain;

/// <summary>Order lifecycle status (spec §4/§5/§7/§9 automation).</summary>
public enum OrderStatus
{
    /// <summary>Order accepted into the queue, processing not started.</summary>
    Pending,

    /// <summary>Order processing in progress (stock reserved, worker running).</summary>
    Processing,

    /// <summary>Payment required; order awaits payment before confirmation.</summary>
    PendingPayment,

    /// <summary>Payment completed, order confirmed.</summary>
    Confirmed,

    /// <summary>Order cancelled (payment timeout, user request, fraud rule, etc.).</summary>
    Cancelled,

    /// <summary>Order completed and fulfilled.</summary>
    Completed,

    /// <summary>Order failed permanently (drift, payment failure after retry, etc.).</summary>
    Failed
}