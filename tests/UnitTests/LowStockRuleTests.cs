using FlashSale.Application.Automation;

namespace FlashSale.UnitTests;

/// <summary>
/// Spec §16 "low-stock rule" tests: the boundary is inclusive — stock equal to
/// the reorder point is already low. No clock, no I/O, no AI.
/// </summary>
public class LowStockRuleTests
{
    [Theory]
    [InlineData(0, 5, true)]   // empty
    [InlineData(1, 5, true)]
    [InlineData(4, 5, true)]
    [InlineData(5, 5, true)]   // boundary: at the reorder point => LOW
    [InlineData(6, 5, false)]  // one above => healthy
    [InlineData(100, 5, false)]
    public void IsLowStock_BoundaryIsInclusive(int stock, int threshold, bool expected)
    {
        Assert.Equal(expected, LowStockRule.IsLowStock(stock, threshold));
    }

    [Fact]
    public void ZeroThreshold_OnlyEmptyStockIsLow()
    {
        // ReorderThreshold=0 on the product means "platform default" upstream;
        // if a caller passes 0 deliberately, only stock 0 trips the rule.
        Assert.False(LowStockRule.IsLowStock(1, 0));
        Assert.True(LowStockRule.IsLowStock(0, 0));
    }
}