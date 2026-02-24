using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Tests.Unit;

public class TipsterConfigTests
{
    // ─── Happy paths ────────────────────────────────────────────────

    [Theory]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/NG1", "NG1")]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/Edge_Analytics", "Edge_Analytics")]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/Mister_Pep", "Mister_Pep")]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/Slug/", "Slug")]  // trailing slash
    public void TryExtractSlug_ValidUrl_ExtractsLastSegment(string url, string expectedSlug)
    {
        var config = new TipsterConfig { Url = url, Name = "Test" };

        var result = config.TryExtractSlug(out var slug);

        Assert.True(result);
        Assert.Equal(expectedSlug, slug);
        Assert.Equal(expectedSlug, config.Id);
    }

    // ─── Failure cases ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-url")]
    public void TryExtractSlug_InvalidUrl_ReturnsFalse(string url)
    {
        var config = new TipsterConfig { Url = url, Name = "Test" };

        var result = config.TryExtractSlug(out var slug);

        Assert.False(result);
        Assert.Null(slug);
        Assert.Equal(string.Empty, config.Id);
    }

    [Fact]
    public void TryExtractSlug_NullUrl_ReturnsFalse()
    {
        var config = new TipsterConfig { Url = null!, Name = "Test" };

        var result = config.TryExtractSlug(out var slug);

        Assert.False(result);
        Assert.Null(slug);
    }

    // ─── Id is set only on success ──────────────────────────────────

    [Fact]
    public void TryExtractSlug_SetsIdProperty_OnSuccess()
    {
        var config = new TipsterConfig
        {
            Url = "https://bet2invest.com/tipsters/performance-stats/KINGBET09",
            Name = "KINGBET09"
        };

        Assert.Equal(string.Empty, config.Id); // before extraction

        config.TryExtractSlug(out _);

        Assert.Equal("KINGBET09", config.Id);
    }

    [Fact]
    public void TryExtractSlug_CalledTwice_UpdatesId()
    {
        var config = new TipsterConfig
        {
            Url = "https://bet2invest.com/tipsters/performance-stats/First",
            Name = "Test"
        };
        config.TryExtractSlug(out _);
        Assert.Equal("First", config.Id);

        config.Url = "https://bet2invest.com/tipsters/performance-stats/Second";
        config.TryExtractSlug(out var slug);

        Assert.Equal("Second", slug);
        Assert.Equal("Second", config.Id);
    }
}
