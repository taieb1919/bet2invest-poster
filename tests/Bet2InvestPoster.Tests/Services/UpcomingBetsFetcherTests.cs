using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Exceptions;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Tests.Services;

public class UpcomingBetsFetcherTests
{
    // ─── Helpers ───────────────────────────────────────────────────

    private static UpcomingBetsFetcher CreateFetcher(FakeExtendedBet2InvestClient fake) =>
        new(fake, NullLogger<UpcomingBetsFetcher>.Instance);

    /// <summary>
    /// Creates a TipsterConfig with a properly extracted slug Id.
    /// TipsterConfig.Id is private-set via TryExtractSlug — must be called explicitly.
    /// </summary>
    private static TipsterConfig MakeTipster(string slug, string? name = null)
    {
        var t = new TipsterConfig
        {
            Url = $"https://bet2invest.com/tipsters/performance-stats/{slug}",
            Name = name ?? slug
        };
        t.TryExtractSlug(out _);
        return t;
    }

    private static SettledBet MakeBet(int id) => new() { Id = id };

    // ─── 5.3: Happy path — multiple tipsters, aggregated bets ──────

    [Fact]
    public async Task FetchAllAsync_WithMultipleTipsters_AggregatesAllBets()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup("Alice", canSeeBets: true, bets: [MakeBet(1), MakeBet(2)]);
        fake.Setup("Bob", canSeeBets: true, bets: [MakeBet(3)]);

        var tipsters = new List<TipsterConfig> { MakeTipster("Alice"), MakeTipster("Bob") };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, b => b.Id == 1);
        Assert.Contains(result, b => b.Id == 2);
        Assert.Contains(result, b => b.Id == 3);
    }

    // ─── 5.4: canSeeBets=false — pro tipster ignored ───────────────

    [Fact]
    public async Task FetchAllAsync_TipsterWithCanSeeBetsFalse_IsIgnored()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup("Free", canSeeBets: true,  bets: [MakeBet(1)]);
        fake.Setup("Pro", canSeeBets: false, bets: [MakeBet(99)]);

        var tipsters = new List<TipsterConfig> { MakeTipster("Free"), MakeTipster("Pro") };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    // ─── 5.5: Empty tipster list ───────────────────────────────────

    [Fact]
    public async Task FetchAllAsync_EmptyTipsterList_ReturnsEmptyList()
    {
        var fake = new FakeExtendedBet2InvestClient();

        var result = await CreateFetcher(fake).FetchAllAsync([]);

        Assert.Empty(result);
        Assert.Equal(0, fake.CallCount);
    }

    // ─── 5.6: canSeeBets=true but zero bets ───────────────────────

    [Fact]
    public async Task FetchAllAsync_TipsterWithZeroBets_ReturnsEmptyContribution()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup("Empty", canSeeBets: true, bets: []);

        var tipsters = new List<TipsterConfig> { MakeTipster("Empty") };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Empty(result);
        Assert.Equal(1, fake.CallCount);
    }

    // ─── 5.7: API exception propagates ────────────────────────────

    [Fact]
    public async Task FetchAllAsync_PropagatesBet2InvestApiException()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.SetupThrow("Tipster", new Bet2InvestApiException("/v1/statistics/Tipster", 503, "Service unavailable"));

        var tipsters = new List<TipsterConfig> { MakeTipster("Tipster") };

        var ex = await Assert.ThrowsAsync<Bet2InvestApiException>(
            () => CreateFetcher(fake).FetchAllAsync(tipsters));

        Assert.Equal(503, ex.HttpStatusCode);
        Assert.Equal("/v1/statistics/Tipster", ex.Endpoint);
    }

    // ─── 5.8: CancellationToken propagates ────────────────────────

    [Fact]
    public async Task FetchAllAsync_PropagatesOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        var fake = new FakeExtendedBet2InvestClient();
        fake.SetupCancelOn("Tipster", cts);

        var tipsters = new List<TipsterConfig> { MakeTipster("Tipster") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateFetcher(fake).FetchAllAsync(tipsters, cts.Token));
    }

    // ─── 5.9: DI registration — Scoped ────────────────────────────

    [Fact]
    public void UpcomingBetsFetcher_RegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IExtendedBet2InvestClient, FakeExtendedBet2InvestClient>();
        services.AddScoped<IUpcomingBetsFetcher, UpcomingBetsFetcher>();

        var sp = services.BuildServiceProvider();

        IUpcomingBetsFetcher instance1, instance2;
        using (var scope1 = sp.CreateScope())
            instance1 = scope1.ServiceProvider.GetRequiredService<IUpcomingBetsFetcher>();
        using (var scope2 = sp.CreateScope())
            instance2 = scope2.ServiceProvider.GetRequiredService<IUpcomingBetsFetcher>();

        Assert.NotSame(instance1, instance2);
    }

    // ─── Additional: all tipsters pro — empty result ───────────────

    [Fact]
    public async Task FetchAllAsync_AllTipstersProOrRestricted_ReturnsEmptyList()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup("Pro1", canSeeBets: false, bets: [MakeBet(10)]);
        fake.Setup("Pro2", canSeeBets: false, bets: [MakeBet(20)]);

        var tipsters = new List<TipsterConfig> { MakeTipster("Pro1"), MakeTipster("Pro2") };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Empty(result);
        Assert.Equal(2, fake.CallCount);
    }

    // ─── M2: Partial failure — exception on 2nd tipster ─────────────

    [Fact]
    public async Task FetchAllAsync_SecondTipsterFails_ExceptionPropagatesPartialResultsLost()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup("OK", canSeeBets: true, bets: [MakeBet(1), MakeBet(2)]);
        fake.SetupThrow("Fail", new Bet2InvestApiException("/v1/statistics/Fail", 500, "Server error"));

        var tipsters = new List<TipsterConfig> { MakeTipster("OK"), MakeTipster("Fail") };

        var ex = await Assert.ThrowsAsync<Bet2InvestApiException>(
            () => CreateFetcher(fake).FetchAllAsync(tipsters));

        Assert.Equal(500, ex.HttpStatusCode);
        Assert.Equal(2, fake.CallCount);
    }

    // ─── Stub ──────────────────────────────────────────────────────

    private class FakeExtendedBet2InvestClient : IExtendedBet2InvestClient
    {
        private readonly Dictionary<string, (bool canSeeBets, List<SettledBet> bets)> _responses = new();
        private readonly Dictionary<string, Exception> _throws = new();
        private readonly Dictionary<string, CancellationTokenSource> _cancelSources = new();

        public bool IsAuthenticated => true;
        public int CallCount { get; private set; }

        public void Setup(string tipsterId, bool canSeeBets, List<SettledBet> bets)
            => _responses[tipsterId] = (canSeeBets, bets);

        public void SetupThrow(string tipsterId, Exception ex)
            => _throws[tipsterId] = ex;

        public void SetupCancelOn(string tipsterId, CancellationTokenSource cts)
            => _cancelSources[tipsterId] = cts;

        public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<(bool CanSeeBets, List<SettledBet> Bets)> GetUpcomingBetsAsync(
            string tipsterId, CancellationToken ct = default)
        {
            CallCount++;

            if (_cancelSources.TryGetValue(tipsterId, out var cts))
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            if (_throws.TryGetValue(tipsterId, out var ex))
                throw ex;

            if (_responses.TryGetValue(tipsterId, out var response))
                return Task.FromResult(response);

            throw new InvalidOperationException(
                $"FakeExtendedBet2InvestClient: no setup for tipsterId={tipsterId}. Call Setup() first.");
        }

        public Task<string?> PublishBetAsync(BetOrderRequest bet, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
