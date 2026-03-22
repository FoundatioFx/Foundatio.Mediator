namespace Foundatio.Mediator.Tests;

public class ApiVersionMatcherTests
{
    [Fact]
    public void ExactMatch_ReturnsVersionedCandidate()
    {
        var candidates = new string[]?[]
        {
            new[] { "1" },
            new[] { "2" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "1", ["1", "2"]);

        Assert.True(hasVersioned);
        Assert.Equal(0, winner);
    }

    [Fact]
    public void ExactMatch_SecondCandidate()
    {
        var candidates = new string[]?[]
        {
            new[] { "1" },
            new[] { "2" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "2", ["1", "2"]);

        Assert.True(hasVersioned);
        Assert.Equal(1, winner);
    }

    [Fact]
    public void NoMatch_FallsBackToUnversioned()
    {
        var candidates = new string[]?[]
        {
            new[] { "1" },
            Array.Empty<string>(), // unversioned fallback
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "2", ["1", "2"]);

        Assert.True(hasVersioned);
        Assert.Equal(1, winner);
    }

    [Fact]
    public void NoMatch_NoFallback_FallsBackToLatestAvailable()
    {
        // Versions 1 and 2 exist, but default is "3" (no handler for v3 on this route)
        var candidates = new string[]?[]
        {
            new[] { "1" },
            new[] { "2" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "3", ["1", "2", "3"]);

        Assert.True(hasVersioned);
        Assert.Equal(1, winner); // Falls back to v2 (latest available)
    }

    [Fact]
    public void NoMatch_NoFallback_FallsBackSkippingGaps()
    {
        // Only v1 exists, default is "3", v2 also missing
        var candidates = new string[]?[]
        {
            new[] { "1" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "3", ["1", "2", "3"]);

        Assert.True(hasVersioned);
        Assert.Equal(0, winner); // Falls back to v1 (only available)
    }

    [Fact]
    public void CaseInsensitive_Matches()
    {
        var candidates = new string[]?[]
        {
            new[] { "V1" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "v1", ["v1"]);

        Assert.True(hasVersioned);
        Assert.Equal(0, winner);
    }

    [Fact]
    public void AllUnversioned_HasVersionedIsFalse()
    {
        var candidates = new string[]?[]
        {
            Array.Empty<string>(),
            Array.Empty<string>(),
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "1", ["1"]);

        Assert.False(hasVersioned);
        Assert.Equal(-1, winner);
    }

    [Fact]
    public void NullCandidates_AreSkipped()
    {
        var candidates = new string[]?[]
        {
            null, // invalid/non-versioned candidate
            new[] { "1" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "1", ["1"]);

        Assert.True(hasVersioned);
        Assert.Equal(1, winner);
    }

    [Fact]
    public void EmptyDeclaredVersions_NoFallbackSearch()
    {
        // No declared versions means no fallback search
        var candidates = new string[]?[]
        {
            new[] { "1" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "2", Array.Empty<string>());

        Assert.True(hasVersioned);
        Assert.Equal(-1, winner);
    }

    [Fact]
    public void FallbackPreferredOverVersionSearch()
    {
        // When there's an unversioned fallback, it should win over the version search
        var candidates = new string[]?[]
        {
            new[] { "1" },
            Array.Empty<string>(), // unversioned fallback
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "3", ["1", "2", "3"]);

        Assert.True(hasVersioned);
        Assert.Equal(1, winner); // Fallback wins, not v1 from version search
    }

    [Fact]
    public void MultiVersionCandidate_MatchesAnyVersion()
    {
        var candidates = new string[]?[]
        {
            new[] { "1", "2" }, // serves both versions
        };

        var (winner1, _) = ApiVersionMetadata.ResolveWinner(candidates, "1", ["1", "2"]);
        var (winner2, _) = ApiVersionMetadata.ResolveWinner(candidates, "2", ["1", "2"]);

        Assert.Equal(0, winner1);
        Assert.Equal(0, winner2);
    }

    [Fact]
    public void SingleCandidate_NoMatchNoDeclared_ReturnsNegativeOne()
    {
        var candidates = new string[]?[]
        {
            new[] { "1" },
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "999", Array.Empty<string>());

        Assert.True(hasVersioned);
        Assert.Equal(-1, winner);
    }

    [Fact]
    public void DateVersions_ExactMatch()
    {
        var candidates = new string[]?[]
        {
            new[] { "2024-01-15" },
            new[] { "2024-06-01" },
        };

        var (winner, _) = ApiVersionMetadata.ResolveWinner(candidates, "2024-06-01", ["2024-01-15", "2024-06-01"]);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void DateVersions_FallbackToLatest()
    {
        var candidates = new string[]?[]
        {
            new[] { "2024-01-15" },
            new[] { "2024-06-01" },
        };

        // Default is "2025-01-01" but no handler exists for it
        var (winner, _) = ApiVersionMetadata.ResolveWinner(
            candidates, "2025-01-01", ["2024-01-15", "2024-06-01", "2025-01-01"]);

        Assert.Equal(1, winner); // Falls back to 2024-06-01 (latest available)
    }

    [Fact]
    public void AllNullCandidates_NoVersioned()
    {
        var candidates = new string[]?[]
        {
            null,
            null,
        };

        var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidates, "1", ["1"]);

        Assert.False(hasVersioned);
        Assert.Equal(-1, winner);
    }

    [Fact]
    public void MixedNullAndVersioned_FindsMatch()
    {
        var candidates = new string[]?[]
        {
            null,
            new[] { "1" },
            null,
            new[] { "2" },
        };

        var (winner, _) = ApiVersionMetadata.ResolveWinner(candidates, "2", ["1", "2"]);

        Assert.Equal(3, winner);
    }
}
