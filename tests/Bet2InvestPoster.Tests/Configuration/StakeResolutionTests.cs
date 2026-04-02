using Bet2InvestPoster.Configuration;

namespace Bet2InvestPoster.Tests.Configuration;

public class StakeResolutionTests
{
    private static PosterOptions CreateOptions(params StakeRule[] rules)
        => new() { StakeRules = [.. rules] };

    private static readonly PosterOptions DefaultOptions = CreateOptions(
        new StakeRule { MaxOdds = 1.7m, Units = 2m },
        new StakeRule { MaxOdds = 2.5m, Units = 1m },
        new StakeRule { MaxOdds = 4.0m, Units = 0.75m },
        new StakeRule { MaxOdds = null,  Units = 0.5m }
    );

    [Fact]
    public void ResolveStake_Odds_1_50_Returns_2u()
        => Assert.Equal(2m, DefaultOptions.ResolveStake(1.50m));

    [Fact]
    public void ResolveStake_Odds_1_70_Returns_1u()
        => Assert.Equal(1m, DefaultOptions.ResolveStake(1.70m));

    [Fact]
    public void ResolveStake_Odds_2_00_Returns_1u()
        => Assert.Equal(1m, DefaultOptions.ResolveStake(2.00m));

    [Fact]
    public void ResolveStake_Odds_2_50_Returns_0_75u()
        => Assert.Equal(0.75m, DefaultOptions.ResolveStake(2.50m));

    [Fact]
    public void ResolveStake_Odds_3_50_Returns_0_75u()
        => Assert.Equal(0.75m, DefaultOptions.ResolveStake(3.50m));

    [Fact]
    public void ResolveStake_Odds_5_00_Returns_0_5u()
        => Assert.Equal(0.5m, DefaultOptions.ResolveStake(5.00m));

    [Fact]
    public void ResolveStake_EmptyRules_Returns_1u()
    {
        var options = CreateOptions();
        Assert.Equal(1m, options.ResolveStake(2.00m));
    }

    [Fact]
    public void ResolveStake_Odds_Exactly_On_Boundary()
    {
        Assert.Equal(0.5m, DefaultOptions.ResolveStake(4.00m));
    }

    [Fact]
    public void ResolveStake_Odds_Very_High_Fallback()
        => Assert.Equal(0.5m, DefaultOptions.ResolveStake(100m));

    [Fact]
    public void ResolveStake_Odds_Very_Low_FirstRule()
        => Assert.Equal(2m, DefaultOptions.ResolveStake(1.01m));
}
