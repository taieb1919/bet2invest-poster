using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Exceptions;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class BetPublisherTests
{
    // ─── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeExtendedClient : IExtendedBet2InvestClient
    {
        public bool IsAuthenticated => true;
        public bool ShouldFail { get; set; }
        public int PublishCallCount { get; private set; }
        public List<BetOrderRequest> PublishedRequests { get; } = [];
        public List<int> PublishedBankrollIds { get; } = [];

        public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(
            int tipsterNumericId, CancellationToken ct = default)
            => Task.FromResult((true, new List<PendingBet>()));

        public Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default)
        {
            PublishCallCount++;
            PublishedRequests.Add(bet);
            PublishedBankrollIds.Add(bankrollId);
            if (ShouldFail) throw new PublishException(0, 500, "Simulated failure");
            return Task.FromResult<string?>("order-id-123");
        }
        public Task<List<Models.ScrapedTipster>> GetFreeTipstersAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Models.ScrapedTipster>());
        public Task<List<SettledBet>> GetSettledBetsForTipsterAsync(int numericId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
            => Task.FromResult(new List<SettledBet>());
    }

    private sealed class FakeHistoryManager : IHistoryManager
    {
        public List<HistoryEntry> Recorded { get; } = [];
        public int PurgeCallCount { get; private set; }

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<string>());

        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
        {
            Recorded.Add(entry);
            return Task.CompletedTask;
        }

        public Task PurgeOldEntriesAsync(CancellationToken ct = default)
        {
            PurgeCallCount++;
            return Task.CompletedTask;
        }

        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default)
            => Task.FromResult(new List<HistoryEntry>());
        public Task UpdateEntriesAsync(List<HistoryEntry> updatedEntries, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct = default)
            => Task.FromResult(new List<HistoryEntry>());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static BetPublisher CreatePublisher(
        FakeExtendedClient? client = null,
        FakeHistoryManager? history = null,
        string bankrollId = "99999")
    {
        var opts = Options.Create(new PosterOptions { BankrollId = bankrollId, DataPath = Path.GetTempPath() });
        return new BetPublisher(
            client ?? new FakeExtendedClient(),
            history ?? new FakeHistoryManager(),
            opts,
            NullLogger<BetPublisher>.Instance);
    }

    private static PendingBet MakeBet(int id, string? homeTeam = null, string? awayTeam = null)
        => new PendingBet
        {
            Id    = id,
            Type  = "MONEYLINE",
            Team  = "TEAM1",
            Price = 1.85m,
            Units = 2m,
            Sport = new BetSport { Id = 1, Name = "Football" },
            Market = new PendingBetMarket
            {
                MatchupId = $"{id}",
                Key = $"s;0;ml;{id}",
                Prices = [new MarketPrice { Designation = "home", Price = 185 }]
            },
            Event = homeTeam != null && awayTeam != null
                ? new BetEvent { Home = homeTeam, Away = awayTeam, Starts = DateTime.UtcNow }
                : null
        };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAllAsync_WithEmptyList_ReturnsZeroAndNoPublish()
    {
        var client = new FakeExtendedClient();
        var publisher = CreatePublisher(client: client);

        var result = await publisher.PublishAllAsync([]);

        Assert.Equal(0, result);
        Assert.Equal(0, client.PublishCallCount);
    }

    [Fact]
    public async Task PublishAllAsync_PublishesEachBetAndRecordsInHistory()
    {
        var client  = new FakeExtendedClient();
        var history = new FakeHistoryManager();
        var publisher = CreatePublisher(client: client, history: history);
        var bets = new List<PendingBet>
        {
            MakeBet(1, "PSG", "OM"),
            MakeBet(2, "Real", "Barça"),
            MakeBet(3, "Lyon", "Nice")
        };

        var result = await publisher.PublishAllAsync(bets);

        Assert.Equal(3, result);
        Assert.Equal(3, client.PublishCallCount);
        Assert.Equal(3, history.Recorded.Count);
        // Vérifier les betIds enregistrés
        Assert.Contains(history.Recorded, e => e.BetId == 1);
        Assert.Contains(history.Recorded, e => e.BetId == 2);
        Assert.Contains(history.Recorded, e => e.BetId == 3);
    }

    [Fact]
    public async Task PublishAllAsync_RecordsMatchDescription_FromEvent()
    {
        var history = new FakeHistoryManager();
        var publisher = CreatePublisher(history: history);
        var bet = MakeBet(42, "Arsenal", "Chelsea");

        await publisher.PublishAllAsync([bet]);

        Assert.Equal("Arsenal vs Chelsea", history.Recorded[0].MatchDescription);
    }

    [Fact]
    public async Task PublishAllAsync_RecordsMatchDescription_WhenNoEvent()
    {
        var history = new FakeHistoryManager();
        var publisher = CreatePublisher(history: history);
        var bet = MakeBet(99); // pas d'event

        await publisher.PublishAllAsync([bet]);

        Assert.Equal("Bet#99", history.Recorded[0].MatchDescription);
    }

    [Fact]
    public async Task PublishAllAsync_WhenPublishFails_SkipsAndReturnsZero()
    {
        var client = new FakeExtendedClient { ShouldFail = true };
        var publisher = CreatePublisher(client: client);

        var result = await publisher.PublishAllAsync([MakeBet(1)]);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task PublishAllAsync_UsesBankrollIdFromOptions()
    {
        var client = new FakeExtendedClient();
        var publisher = CreatePublisher(client: client, bankrollId: "12345");

        await publisher.PublishAllAsync([MakeBet(1)]);

        Assert.Single(client.PublishedRequests);
        Assert.Equal(12345, client.PublishedBankrollIds[0]);
    }

    [Fact]
    public async Task PublishAllAsync_WithInvalidMatchupId_SkipsBet()
    {
        var client = new FakeExtendedClient();
        var history = new FakeHistoryManager();
        var publisher = CreatePublisher(client: client, history: history);
        var bet = MakeBet(1, "PSG", "OM");
        bet.Market!.MatchupId = "not-a-number";

        var result = await publisher.PublishAllAsync([bet]);

        Assert.Equal(0, result);
        Assert.Equal(0, client.PublishCallCount);
    }

    [Fact]
    public async Task PublishAllAsync_WithNullSport_SkipsBet()
    {
        var client = new FakeExtendedClient();
        var history = new FakeHistoryManager();
        var publisher = CreatePublisher(client: client, history: history);
        var bet = MakeBet(1, "PSG", "OM");
        bet.Sport = null;

        var result = await publisher.PublishAllAsync([bet]);

        Assert.Equal(0, result);
        Assert.Equal(0, client.PublishCallCount);
    }

    [Fact]
    public void BetPublisher_RegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<PosterOptions>(o =>
        {
            o.DataPath   = Path.GetTempPath();
            o.BankrollId = "test";
        });
        services.Configure<Bet2InvestOptions>(o => o.ApiBase = "https://example.com");
        services.Configure<TelegramOptions>(_ => { });
        services.AddSingleton<JTDev.Bet2InvestScraper.Api.Bet2InvestClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JTDev.Bet2InvestScraper.Api.Bet2InvestClient>>();
            return new JTDev.Bet2InvestScraper.Api.Bet2InvestClient(
                "https://example.com", 0, new SerilogConsoleLoggerAdapter(logger));
        });
        services.AddScoped<IExtendedBet2InvestClient, ExtendedBet2InvestClient>();
        services.AddScoped<IHistoryManager, HistoryManager>();
        services.AddScoped<IBetPublisher, BetPublisher>();

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IBetPublisher>();
        Assert.NotNull(publisher);
    }
}
