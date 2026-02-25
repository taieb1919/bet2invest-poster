using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Tests.Services;

public class ResultTrackerTests
{
    // ─── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeHistoryManager : IHistoryManager
    {
        public List<HistoryEntry> Entries { get; set; } = [];
        public List<HistoryEntry>? LastUpdated { get; private set; }
        public int UpdateCallCount { get; private set; }

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<string>());

        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PurgeOldEntriesAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default)
            => Task.FromResult(Entries.Take(count).ToList());

        public Task UpdateEntriesAsync(List<HistoryEntry> updatedEntries, CancellationToken ct = default)
        {
            LastUpdated = updatedEntries;
            UpdateCallCount++;
            return Task.CompletedTask;
        }
        public Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct = default)
            => Task.FromResult(new List<HistoryEntry>());
    }

    private sealed class FakeTipsterService : ITipsterService
    {
        public List<TipsterConfig> Tipsters { get; set; } = [];

        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default)
            => Task.FromResult(Tipsters);

        public Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task ReplaceTipstersAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeExtendedClient : IExtendedBet2InvestClient
    {
        public bool IsAuthenticated => true;
        public List<SettledBet> SettledBetsToReturn { get; set; } = [];
        public int GetSettledCallCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(int tipsterNumericId, CancellationToken ct = default)
            => Task.FromResult((true, new List<PendingBet>()));

        public Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<List<ScrapedTipster>> GetFreeTipstersAsync(CancellationToken ct = default)
            => Task.FromResult(new List<ScrapedTipster>());

        public Task<List<SettledBet>> GetSettledBetsForTipsterAsync(
            int numericId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            GetSettledCallCount++;
            if (ShouldThrow) throw new HttpRequestException("Simulated API failure");
            return Task.FromResult(SettledBetsToReturn);
        }
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow = new(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static TipsterConfig MakeTipsterConfig(string slug, int numericId)
    {
        var config = new TipsterConfig
        {
            Url = $"https://bet2invest.com/tipsters/performance-stats/{slug}",
            NumericId = numericId
        };
        config.TryExtractSlug(out _);
        return config;
    }

    private static (ResultTracker tracker, FakeHistoryManager history, FakeTipsterService tipsters, FakeExtendedClient client)
        CreateTracker(TimeProvider? timeProvider = null)
    {
        var history = new FakeHistoryManager();
        var tipsters = new FakeTipsterService();
        var client = new FakeExtendedClient();
        var tracker = new ResultTracker(
            history,
            tipsters,
            client,
            NullLogger<ResultTracker>.Instance,
            timeProvider ?? new FixedTimeProvider(FixedNow));
        return (tracker, history, tipsters, client);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrackResultsAsync_WhenSettledBetFound_UpdatesEntryResult()
    {
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 42, TipsterName = "tipster1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime }
        ];
        tipsters.Tipsters = [MakeTipsterConfig("tipster1", 100)];
        client.SettledBetsToReturn = [new SettledBet { Id = 42, State = "WON" }];

        await tracker.TrackResultsAsync();

        Assert.NotNull(history.LastUpdated);
        Assert.Single(history.LastUpdated);
        Assert.Equal("won", history.LastUpdated[0].Result);
    }

    [Fact]
    public async Task TrackResultsAsync_WhenSettledBetNotFound_EntrySetToPending()
    {
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 99, TipsterName = "tipster1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime }
        ];
        tipsters.Tipsters = [MakeTipsterConfig("tipster1", 100)];
        client.SettledBetsToReturn = []; // aucun résultat disponible

        await tracker.TrackResultsAsync();

        Assert.NotNull(history.LastUpdated);
        Assert.Single(history.LastUpdated);
        Assert.Equal("pending", history.LastUpdated[0].Result);
    }

    [Fact]
    public async Task TrackResultsAsync_WhenResultAlreadyResolved_DoesNotCallApi()
    {
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 1, TipsterName = "tipster1", Result = "won", PublishedAt = FixedNow.AddDays(-1).UtcDateTime }
        ];
        tipsters.Tipsters = [MakeTipsterConfig("tipster1", 100)];

        await tracker.TrackResultsAsync();

        // Entrée déjà résolue → pas d'appel API, pas de mise à jour
        Assert.Equal(0, client.GetSettledCallCount);
        Assert.Equal(0, history.UpdateCallCount);
    }

    [Fact]
    public async Task TrackResultsAsync_WhenEntryOlderThan7Days_IsIgnored()
    {
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 5, TipsterName = "tipster1", Result = null, PublishedAt = FixedNow.AddDays(-8).UtcDateTime }
        ];
        tipsters.Tipsters = [MakeTipsterConfig("tipster1", 100)];

        await tracker.TrackResultsAsync();

        // Ignorée car > 7 jours
        Assert.Equal(0, client.GetSettledCallCount);
        Assert.Equal(0, history.UpdateCallCount);
    }

    [Fact]
    public async Task TrackResultsAsync_WhenResultUpdated_CallsUpdateEntriesAsync()
    {
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 7, TipsterName = "tipster1", Result = "pending", PublishedAt = FixedNow.AddDays(-2).UtcDateTime }
        ];
        tipsters.Tipsters = [MakeTipsterConfig("tipster1", 100)];
        client.SettledBetsToReturn = [new SettledBet { Id = 7, State = "LOST" }];

        await tracker.TrackResultsAsync();

        // UpdateEntriesAsync doit être appelé via HistoryManager
        Assert.Equal(1, history.UpdateCallCount);
        Assert.NotNull(history.LastUpdated);
        Assert.Equal("lost", history.LastUpdated[0].Result);
    }

    [Fact]
    public async Task TrackResultsAsync_MapsAllStatesCorrectly()
    {
        // WON → "won", LOST → "lost", HALF_WON → "won", HALF_LOST → "lost"
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 1, TipsterName = "t1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
            new HistoryEntry { BetId = 2, TipsterName = "t1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
            new HistoryEntry { BetId = 3, TipsterName = "t1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
            new HistoryEntry { BetId = 4, TipsterName = "t1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
        ];
        tipsters.Tipsters = [MakeTipsterConfig("t1", 99)];
        client.SettledBetsToReturn =
        [
            new SettledBet { Id = 1, State = "WON" },
            new SettledBet { Id = 2, State = "LOST" },
            new SettledBet { Id = 3, State = "HALF_WON" },
            new SettledBet { Id = 4, State = "HALF_LOST" },
        ];

        await tracker.TrackResultsAsync();

        var updated = history.LastUpdated!.ToDictionary(e => e.BetId, e => e.Result);
        Assert.Equal("won", updated[1]);
        Assert.Equal("lost", updated[2]);
        Assert.Equal("won", updated[3]);
        Assert.Equal("lost", updated[4]);
    }

    [Fact]
    public async Task TrackResultsAsync_MapsRefundedAndCancelledToVoid()
    {
        // REFUNDED → "void", CANCELLED → "void" — ne faussent pas les stats
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 10, TipsterName = "t1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
            new HistoryEntry { BetId = 11, TipsterName = "t1", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
        ];
        tipsters.Tipsters = [MakeTipsterConfig("t1", 99)];
        client.SettledBetsToReturn =
        [
            new SettledBet { Id = 10, State = "REFUNDED" },
            new SettledBet { Id = 11, State = "CANCELLED" },
        ];

        await tracker.TrackResultsAsync();

        var updated = history.LastUpdated!.ToDictionary(e => e.BetId, e => e.Result);
        Assert.Equal("void", updated[10]);
        Assert.Equal("void", updated[11]);
    }

    [Fact]
    public async Task TrackResultsAsync_WhenApiThrows_ContinuesWithOtherTipsters()
    {
        // Un échec API pour un tipster ne doit pas bloquer le traitement des autres
        var (tracker, history, tipsters, client) = CreateTracker();

        history.Entries =
        [
            new HistoryEntry { BetId = 20, TipsterName = "failing", Result = null, PublishedAt = FixedNow.AddDays(-1).UtcDateTime },
        ];
        tipsters.Tipsters = [MakeTipsterConfig("failing", 200)];
        client.ShouldThrow = true;

        // Ne doit pas propager l'exception
        await tracker.TrackResultsAsync();

        // L'entrée doit être marquée pending (pas perdue)
        Assert.NotNull(history.LastUpdated);
        Assert.Equal("pending", history.LastUpdated[0].Result);
    }
}
