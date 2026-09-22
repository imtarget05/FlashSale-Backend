using FlashSale.Application.Assistant;

namespace FlashSale.UnitTests;

/// <summary>
/// The grounding guard is the safety property of spec §10 ("AI không được
/// invent productId") — parser behaviour is tested directly, not only via the
/// use case, so a regression here cannot hide behind a fake.
/// </summary>
public class AssistantOutputParserTests
{
    private static readonly ISet<int> Valid = new HashSet<int> { 1, 2 };

    [Fact]
    public void ParsesPlainJson()
    {
        var ok = AssistantOutputParser.TryParse(
            """{"answer":"Pick 1.","recommendedProducts":[{"productId":1,"reason":"best"}]}""",
            Valid, out var answer, out var products, out var dropped);

        Assert.True(ok);
        Assert.Equal("Pick 1.", answer);
        Assert.Single(products);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void StripsMarkdownCodeFence()
    {
        var ok = AssistantOutputParser.TryParse(
            "```json\n{\"answer\":\"Pick 2.\",\"recommendedProducts\":[{\"productId\":2,\"reason\":\"cheap\"}]}\n```",
            Valid, out var answer, out var products, out _);

        Assert.True(ok);
        Assert.Equal("Pick 2.", answer);
        Assert.Single(products);
    }

    [Fact]
    public void DropsUnknownAndMalformedProductIds_AndCountsThem()
    {
        var ok = AssistantOutputParser.TryParse(
            """{"answer":"a","recommendedProducts":[{"productId":1,"reason":"ok"},{"productId":77,"reason":"fake"},{"productId":null,"reason":"broken"},{"productId":1,"reason":"dup"}]}""",
            Valid, out _, out var products, out var dropped);

        Assert.True(ok);
        Assert.Single(products);          // only id 1 kept (first occurrence)
        Assert.Equal(2, dropped);         // fake id 77 + null id (duplicate is deduped, not dropped)
    }

    [Fact]
    public void RejectsGarbage_WithoutThrowing()
    {
        var ok = AssistantOutputParser.TryParse("definitely not json", Valid, out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void RejectsMissingAnswer()
    {
        var ok = AssistantOutputParser.TryParse(
            """{"recommendedProducts":[{"productId":1,"reason":"x"}]}""",
            Valid, out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void SloppyStringArrayEntry_IsDropped_NotFatal()
    {
        // Observed live with qwen3:4b: "recommendedProducts": ["great phone"]
        var ok = AssistantOutputParser.TryParse(
            """{"answer":"ok","recommendedProducts":["great phone"]}""",
            Valid, out _, out var products, out var dropped);

        Assert.True(ok);          // answer survives a malformed array
        Assert.Empty(products);
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void RejectsEmptyOrNull()
    {
        Assert.False(AssistantOutputParser.TryParse("", Valid, out _, out _, out _));
        Assert.False(AssistantOutputParser.TryParse("   ", Valid, out _, out _, out _));
    }

    [Fact]
    public void AnswerWithoutRecommendations_IsStillValid()
    {
        var ok = AssistantOutputParser.TryParse(
            """{"answer":"Nothing matches.","recommendedProducts":[]}""",
            Valid, out var answer, out var products, out var dropped);

        Assert.True(ok);
        Assert.Equal("Nothing matches.", answer);
        Assert.Empty(products);
        Assert.Equal(0, dropped);
    }
}
