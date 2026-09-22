namespace FlashSale.Domain.Entities;

/// <summary>Aggregate root for flash-sale inventory.</summary>
public class Product
{
    public int Id { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal FlashSalePrice { get; set; }
    public int AvailableStock { get; set; }
}
