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

    private static int _nextNumericId = 1000;

    /// <summary>
    /// Creates a TipsterConfig with a properly extracted slug Id and a numeric ID.
    /// </summary>
    private static TipsterConfig MakeTipster(string slug, string? name = null, int numericId = 0)
    {
        var t = new TipsterConfig
        {
            Url = $"https://bet2invest.com/tipsters/performance-stats/{slug}",
            Name = name ?? slug
        };
        t.TryExtractSlug(out _);
        t.NumericId = numericId > 0 ? numericId : Interlocked.Increment(ref _nextNumericId);
        return t;
    }

    private static PendingBet MakeBet(int id) => new PendingBet { Id = id };

    // ─── 5.3: Happy path — multiple tipsters, aggregated bets ──────

    [Fact]
    public async Task FetchAllAsync_WithMultipleTipsters_AggregatesAllBets()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup(101, canSeeBets: true, bets: [MakeBet(1), MakeBet(2)]);
        fake.Setup(102, canSeeBets: true, bets: [MakeBet(3)]);

        var tipsters = new List<TipsterConfig> { MakeTipster("Alice", numericId: 101), MakeTipster("Bob", numericId: 102) };

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
        fake.Setup(201, canSeeBets: true,  bets: [MakeBet(1)]);
        fake.Setup(202, canSeeBets: false, bets: [MakeBet(99)]);

        var tipsters = new List<TipsterConfig> { MakeTipster("Free", numericId: 201), MakeTipster("Pro", numericId: 202) };

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
        fake.Setup(301, canSeeBets: true, bets: []);

        var tipsters = new List<TipsterConfig> { MakeTipster("Empty", numericId: 301) };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Empty(result);
        Assert.Equal(1, fake.CallCount);
    }

    // ─── 5.7: API exception caught per-tipster, returns empty results ───

    [Fact]
    public async Task FetchAllAsync_ApiException_CaughtPerTipster_ReturnsEmptyResults()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.SetupThrow(401, new Bet2InvestApiException("/v1/statistics/401", 503, "Service unavailable"));

        var tipsters = new List<TipsterConfig> { MakeTipster("Tipster", numericId: 401) };

        // Exception caught per-tipster, returns empty (no propagation)
        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Empty(result);
        Assert.Equal(1, fake.CallCount);
    }

    // ─── 5.8: CancellationToken propagates ────────────────────────

    [Fact]
    public async Task FetchAllAsync_PropagatesOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        var fake = new FakeExtendedBet2InvestClient();
        fake.SetupCancelOn(501, cts);

        var tipsters = new List<TipsterConfig> { MakeTipster("Tipster", numericId: 501) };

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
        fake.Setup(601, canSeeBets: false, bets: [MakeBet(10)]);
        fake.Setup(602, canSeeBets: false, bets: [MakeBet(20)]);

        var tipsters = new List<TipsterConfig> { MakeTipster("Pro1", numericId: 601), MakeTipster("Pro2", numericId: 602) };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        Assert.Empty(result);
        Assert.Equal(2, fake.CallCount);
    }

    // ─── Partial failure — exception on 2nd tipster, 1st tipster results preserved ─────────────

    [Fact]
    public async Task FetchAllAsync_SecondTipsterFails_FirstTipsterResultsPreserved()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup(701, canSeeBets: true, bets: [MakeBet(1), MakeBet(2)]);
        fake.SetupThrow(702, new Bet2InvestApiException("/v1/statistics/702", 500, "Server error"));

        var tipsters = new List<TipsterConfig> { MakeTipster("OK", numericId: 701), MakeTipster("Fail", numericId: 702) };

        var result = await CreateFetcher(fake).FetchAllAsync(tipsters);

        // Le tipster OK a réussi, ses bets sont conservés malgré l'échec du 2e
        Assert.Equal(2, result.Count);
        Assert.Contains(result, b => b.Id == 1);
        Assert.Contains(result, b => b.Id == 2);
        Assert.Equal(2, fake.CallCount);
    }

    // ─── Story 11.2 : propagation des stats tipster vers PendingBets ──────

    [Fact]
    public async Task FetchAllAsync_EnrichesBetsWithTipsterStats_FromTipsterConfig()
    {
        // Issue #4 : vérifier que TipsterRoi, TipsterWinRate, TipsterSport, TipsterUsername
        // sont correctement propagés depuis la config du tipster vers les PendingBets
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup(801, canSeeBets: true, bets: [MakeBet(42)]);

        var tipster = MakeTipster("cristiano", name: "Cristiano", numericId: 801);
        tipster.Roi = 0.15m;
        tipster.BetsNumber = 42;
        tipster.MostBetSport = "Football";

        var result = await CreateFetcher(fake).FetchAllAsync([tipster]);

        Assert.Single(result);
        var resultBet = result[0];
        Assert.Equal(0.15m, resultBet.TipsterRoi);
        Assert.Equal(42m, resultBet.TipsterWinRate);   // BetsNumber converti en decimal
        Assert.Equal("Football", resultBet.TipsterSport);
        Assert.Equal("Cristiano", resultBet.TipsterUsername); // Name, pas le slug
    }

    // ─── Stub ──────────────────────────────────────────────────────

    private class FakeExtendedBet2InvestClient : IExtendedBet2InvestClient
    {
        private readonly Dictionary<int, (bool canSeeBets, List<PendingBet> bets)> _responses = new();
        private readonly Dictionary<int, Exception> _throws = new();
        private readonly Dictionary<int, CancellationTokenSource> _cancelSources = new();

        public bool IsAuthenticated => true;
        public int CallCount { get; private set; }

        public void Setup(int numericId, bool canSeeBets, List<PendingBet> bets)
            => _responses[numericId] = (canSeeBets, bets);

        public void SetupThrow(int numericId, Exception ex)
            => _throws[numericId] = ex;

        public void SetupCancelOn(int numericId, CancellationTokenSource cts)
            => _cancelSources[numericId] = cts;

        public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(
            int tipsterNumericId, CancellationToken ct = default)
        {
            CallCount++;

            if (_cancelSources.TryGetValue(tipsterNumericId, out var cts))
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            if (_throws.TryGetValue(tipsterNumericId, out var ex))
                throw ex;

            if (_responses.TryGetValue(tipsterNumericId, out var response))
                return Task.FromResult(response);

            throw new InvalidOperationException(
                $"FakeExtendedBet2InvestClient: no setup for numericId={tipsterNumericId}. Call Setup() first.");
        }

        public Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<List<Models.ScrapedTipster>> GetFreeTipstersAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Models.ScrapedTipster>());
    }
}
