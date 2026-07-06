namespace Foundatio.Mediator.Tests;

public class HandlerResultTests
{
    public record TestMessage(string Value);

    [Fact]
    public void ContinueWith_SetsReplacementMessageAndContinues()
    {
        var replacement = new TestMessage("enriched");

        var result = HandlerResult.ContinueWith(replacement);

        Assert.False(result.IsShortCircuited);
        Assert.Same(replacement, result.ReplacementMessage);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ContinueWith_NullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HandlerResult.ContinueWith(null!));
        Assert.Throws<ArgumentNullException>(() => HandlerResult<string>.ContinueWith(null!));
    }

    [Fact]
    public void Continue_HasNoReplacementMessage()
    {
        Assert.Null(HandlerResult.Continue().ReplacementMessage);
        Assert.Null(HandlerResult.Continue("state").ReplacementMessage);
        Assert.Null(HandlerResult.ShortCircuit("value").ReplacementMessage);
    }

    [Fact]
    public void GenericContinueWith_ToNonGeneric_PreservesReplacementMessage()
    {
        var replacement = new TestMessage("enriched");

        HandlerResult result = HandlerResult<string>.ContinueWith(replacement);

        Assert.False(result.IsShortCircuited);
        Assert.Same(replacement, result.ReplacementMessage);
    }

    [Fact]
    public void GenericShortCircuit_ToNonGeneric_HasNoReplacementMessage()
    {
        HandlerResult result = HandlerResult<string>.ShortCircuit("value");

        Assert.True(result.IsShortCircuited);
        Assert.Null(result.ReplacementMessage);
        Assert.Equal("value", result.Value);
    }
}
