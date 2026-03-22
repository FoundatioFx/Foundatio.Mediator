namespace Foundatio.Mediator.Tests;

public class ApiVersionComparisonTests
{
    // --- Integer versions ---

    [Theory]
    [InlineData("1", "2", true)]
    [InlineData("2", "1", false)]
    [InlineData("1", "1", false)]
    [InlineData("10", "2", false)]
    [InlineData("2", "10", true)]
    public void IsBefore_IntegerVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsBefore(target));
    }

    [Theory]
    [InlineData("1", "1", true)]
    [InlineData("2", "1", true)]
    [InlineData("1", "2", false)]
    [InlineData("10", "2", true)]
    public void IsAtLeast_IntegerVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsAtLeast(target));
    }

    // --- Semantic versions (major.minor) ---

    [Theory]
    [InlineData("1.0", "1.1", true)]
    [InlineData("1.1", "1.0", false)]
    [InlineData("1.2", "1.10", true)]   // numeric comparison, not lexicographic
    [InlineData("1.10", "1.2", false)]
    [InlineData("2.0", "1.99", false)]
    [InlineData("1.0", "1.0", false)]
    public void IsBefore_SemanticVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsBefore(target));
    }

    [Theory]
    [InlineData("1.0", "1.0", true)]
    [InlineData("1.10", "1.2", true)]   // 1.10 >= 1.2
    [InlineData("1.2", "1.10", false)]  // 1.2 < 1.10
    [InlineData("2.0", "1.99", true)]
    public void IsAtLeast_SemanticVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsAtLeast(target));
    }

    // --- Three-part versions ---

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.1", "1.0.0", false)]
    [InlineData("1.0.9", "1.0.10", true)]
    public void IsBefore_ThreePartVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsBefore(target));
    }

    // --- ISO 8601 date versions ---

    [Theory]
    [InlineData("2024-01-15", "2024-06-01", true)]
    [InlineData("2024-06-01", "2024-01-15", false)]
    [InlineData("2024-01-15", "2024-01-15", false)]
    [InlineData("2023-12-31", "2024-01-01", true)]
    public void IsBefore_DateVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsBefore(target));
    }

    [Theory]
    [InlineData("2024-06-01", "2024-01-15", true)]
    [InlineData("2024-01-15", "2024-01-15", true)]
    [InlineData("2024-01-15", "2024-06-01", false)]
    public void IsAtLeast_DateVersions(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsAtLeast(target));
    }

    // --- Exact match ---

    [Theory]
    [InlineData("1", "1", true)]
    [InlineData("1", "2", false)]
    [InlineData("v1", "V1", true)]       // case-insensitive
    [InlineData("beta", "BETA", true)]
    [InlineData("1.0", "1.0", true)]
    public void Is_ExactMatch(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.Is(target));
    }

    // --- String fallback ---

    [Theory]
    [InlineData("alpha", "beta", true)]
    [InlineData("beta", "alpha", false)]
    [InlineData("alpha", "alpha", false)]
    public void IsBefore_StringFallback(string current, string target, bool expected)
    {
        var ctx = new ApiVersionContext { Current = current };
        Assert.Equal(expected, ctx.IsBefore(target));
    }

    // --- Empty/default version ---

    [Fact]
    public void DefaultCurrent_IsEmptyString()
    {
        var ctx = new ApiVersionContext();
        Assert.Equal("", ctx.Current);
    }

    [Fact]
    public void EmptyCurrent_IsBeforeAnyVersion()
    {
        var ctx = new ApiVersionContext { Current = "" };
        Assert.True(ctx.IsBefore("1"));
    }

    // --- Mixed format edge cases (both must parse the same format) ---

    [Fact]
    public void MismatchedFormats_FallsBackToString()
    {
        // "1.0" parses as Version, "2024-01-15" does not → string comparison fallback
        var ctx = new ApiVersionContext { Current = "1.0" };
        // Both can't parse as int, "1.0" parses as Version but "2024-01-15" doesn't → string compare
        Assert.True(ctx.IsBefore("2024-01-15"));
    }
}
