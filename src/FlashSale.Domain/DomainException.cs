namespace FlashSale.Domain;

public sealed class StockDriftException : Exception
{
    public StockDriftException(string message) : base(message) { }
}

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
