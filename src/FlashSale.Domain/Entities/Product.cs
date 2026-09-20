namespace FlashSale.Domain.Entities;

/// <summary>Aggregate root for flash-sale inventory.</summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AvailableStock { get; set; }
}
