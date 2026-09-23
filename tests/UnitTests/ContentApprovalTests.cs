using FlashSale.Application.Content;
using FlashSale.Domain.Content;

namespace FlashSale.UnitTests;

/// <summary>
/// Spec §8 + §13: the rules that make "AI must never auto-publish" true.
/// Published is reachable ONLY from Approved; Approved ONLY from ReviewRequired.
/// </summary>
public class ContentApprovalStateMachineTests
{
    [Fact]
    public void Approve_OnlyFromReviewRequired()
    {
        Assert.True(ContentApprovalStateMachine.CanApprove(ProductContentStatus.ReviewRequired));
        Assert.False(ContentApprovalStateMachine.CanApprove(ProductContentStatus.Draft));
        Assert.False(ContentApprovalStateMachine.CanApprove(ProductContentStatus.AiGenerated));
        Assert.False(ContentApprovalStateMachine.CanApprove(ProductContentStatus.Approved)); // already
        Assert.False(ContentApprovalStateMachine.CanApprove(ProductContentStatus.Rejected));
        Assert.False(ContentApprovalStateMachine.CanApprove(ProductContentStatus.Published));
    }

    [Fact]
    public void Publish_OnlyFromApproved_NeverDirectlyFromReview()
    {
        Assert.True(ContentApprovalStateMachine.CanPublish(ProductContentStatus.Approved));
        Assert.False(ContentApprovalStateMachine.CanPublish(ProductContentStatus.ReviewRequired));
        Assert.False(ContentApprovalStateMachine.CanPublish(ProductContentStatus.AiGenerated));
        Assert.False(ContentApprovalStateMachine.CanPublish(ProductContentStatus.Rejected));
        Assert.False(ContentApprovalStateMachine.CanPublish(ProductContentStatus.Published));
    }

    [Fact]
    public void Reject_FromReviewOrApproved_Only()
    {
        Assert.True(ContentApprovalStateMachine.CanReject(ProductContentStatus.ReviewRequired));
        Assert.True(ContentApprovalStateMachine.CanReject(ProductContentStatus.Approved));
        Assert.False(ContentApprovalStateMachine.CanReject(ProductContentStatus.Published));
        Assert.False(ContentApprovalStateMachine.CanReject(ProductContentStatus.Draft));
    }

    [Fact]
    public void Regenerate_AllowedUntilPublished()
    {
        Assert.True(ContentApprovalStateMachine.CanRegenerate(ProductContentStatus.Draft));
        Assert.True(ContentApprovalStateMachine.CanRegenerate(ProductContentStatus.AiGenerated));
        Assert.True(ContentApprovalStateMachine.CanRegenerate(ProductContentStatus.ReviewRequired));
        Assert.True(ContentApprovalStateMachine.CanRegenerate(ProductContentStatus.Rejected));
        Assert.False(ContentApprovalStateMachine.CanRegenerate(ProductContentStatus.Published));
    }

    [Fact]
    public void RequiresHumanReview_UntilADecisionIsMade()
    {
        Assert.True(ContentApprovalStateMachine.RequiresHumanReview(ProductContentStatus.AiGenerated));
        Assert.True(ContentApprovalStateMachine.RequiresHumanReview(ProductContentStatus.ReviewRequired));
        Assert.False(ContentApprovalStateMachine.RequiresHumanReview(ProductContentStatus.Approved));
        Assert.False(ContentApprovalStateMachine.RequiresHumanReview(ProductContentStatus.Published));
    }
}

/// <summary>Validated content shape tests: every field required, arrays bounded.</summary>
public class ProductContentParserTests
{
    private const string Valid = """
        {"shortDescription":"Great phone for daily use.","seoDescription":"Flagship smartphone with advanced camera.","keywords":["phone","smartphone","flagship"],"socialCaption":"Get the new Phone X!","faq":[{"question":"Warranty?","answer":"1 year."},{"question":"Screen size?","answer":"6.7 inch."}]}
        """;

    [Fact]
    public void ParsesValidContent()
    {
        var ok = ProductContentParser.TryParse(Valid, out var content, out var reason);

        Assert.True(ok, reason);
        Assert.Equal("Great phone for daily use.", content.ShortDescription);
        Assert.Equal(3, content.Keywords.Count);
        Assert.Equal(2, content.Faq.Count);
    }

    [Fact]
    public void StripsCodeFence()
    {
        var ok = ProductContentParser.TryParse("```json\n" + Valid + "\n```", out var content, out _);

        Assert.True(ok);
        Assert.NotEmpty(content.ShortDescription);
    }

    [Theory]
    [InlineData("""{"shortDescription":"","seoDescription":"x","keywords":["a","b","c"],"socialCaption":"y","faq":[{"question":"q","answer":"a"},{"question":"q2","answer":"a2"}]}""", "shortDescription")]
    [InlineData("""{"seoDescription":"x","keywords":["a","b","c"],"socialCaption":"y","faq":[{"question":"q","answer":"a"},{"question":"q2","answer":"a2"}]}""", "shortDescription")]
    [InlineData("""{"shortDescription":"ok","seoDescription":"x","keywords":["only-two"],"socialCaption":"y","faq":[{"question":"q","answer":"a"},{"question":"q2","answer":"a2"}]}""", "keywords")]
    [InlineData("""{"shortDescription":"ok","seoDescription":"x","keywords":["a","b","c"],"socialCaption":"y","faq":[{"question":"q","answer":"a"}]}""", "faq")]
    [InlineData("not json at all", "not JSON")]
    [InlineData("[1,2,3]", "object")]
    public void RejectsMalformedContent(string raw, string expectedReasonFragment)
    {
        var ok = ProductContentParser.TryParse(raw, out _, out var reason);

        Assert.False(ok);
        Assert.Contains(expectedReasonFragment, reason, StringComparison.OrdinalIgnoreCase);
    }
}