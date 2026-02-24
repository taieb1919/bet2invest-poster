using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class PostingCycleServiceTests
{
    // ─── Shared call order tracker ───────────────────────────────────────────

    private readonly List<string> _callOrder = [];

    // ─── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeHistoryManager : IHistoryManager
    {
        private readonly List<string>? _callOrder;
        public int PurgeCallCount { get; private set; }

        public FakeHistoryManager(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<int>());
        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task PurgeOldEntriesAsync(CancellationToken ct = default)
        {
            PurgeCallCount++;
            _callOrder?.Add("Purge");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTipsterService : ITipsterService
    {
        private readonly List<string>? _callOrder;
        public int CallCount { get; private set; }
        public List<TipsterConfig> TipstersToReturn { get; set; } = [];

        public FakeTipsterService(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default)
        {
            CallCount++;
            _callOrder?.Add("LoadTipsters");
            return Task.FromResult(TipstersToReturn);
        }
    }

    private sealed class FakeUpcomingBetsFetcher : IUpcomingBetsFetcher
    {
        private readonly List<string>? _callOrder;
        public int CallCount { get; private set; }
        public List<SettledBet> BetsToReturn { get; set; } = [];

        public FakeUpcomingBetsFetcher(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<List<SettledBet>> FetchAllAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
        {
            CallCount++;
            _callOrder?.Add("FetchAll");
            return Task.FromResult(BetsToReturn);
        }
    }

    private sealed class FakeBetSelector : IBetSelector
    {
        private readonly List<string>? _callOrder;
        public int CallCount { get; private set; }
        public List<SettledBet> SelectionToReturn { get; set; } = [];

        public FakeBetSelector(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<List<SettledBet>> SelectAsync(List<SettledBet> candidates, CancellationToken ct = default)
        {
            CallCount++;
            _callOrder?.Add("Select");
            return Task.FromResult(SelectionToReturn);
        }
    }

    private sealed class FakeBetPublisher : IBetPublisher
    {
        private readonly List<string>? _callOrder;
        public int CallCount { get; private set; }
        public List<SettledBet> LastSelected { get; private set; } = [];

        public FakeBetPublisher(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default)
        {
            CallCount++;
            LastSelected = selected;
            _callOrder?.Add("PublishAll");
            return Task.FromResult(selected.Count);
        }
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static PostingCycleService CreateService(
        FakeHistoryManager?      history  = null,
        FakeTipsterService?      tipsters = null,
        FakeUpcomingBetsFetcher? fetcher  = null,
        FakeBetSelector?         selector = null,
        FakeBetPublisher?        publisher = null)
        => new PostingCycleService(
            history   ?? new FakeHistoryManager(),
            tipsters  ?? new FakeTipsterService(),
            fetcher   ?? new FakeUpcomingBetsFetcher(),
            selector  ?? new FakeBetSelector(),
            publisher ?? new FakeBetPublisher(),
            NullLogger<PostingCycleService>.Instance);

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunCycleAsync_CallsPurgeFirst()
    {
        var history   = new FakeHistoryManager(_callOrder);
        var tipsters  = new FakeTipsterService(_callOrder);
        var fetcher   = new FakeUpcomingBetsFetcher(_callOrder);
        var selector  = new FakeBetSelector(_callOrder);
        var publisher = new FakeBetPublisher(_callOrder);
        var service   = CreateService(history, tipsters, fetcher, selector, publisher);

        await service.RunCycleAsync();

        Assert.Equal(1, history.PurgeCallCount);
        Assert.True(_callOrder.Count >= 2, "Expected at least 2 calls");
        Assert.Equal("Purge", _callOrder[0]);
    }

    [Fact]
    public async Task RunCycleAsync_CallsAllPipelineStages()
    {
        var history   = new FakeHistoryManager();
        var tipsters  = new FakeTipsterService();
        var fetcher   = new FakeUpcomingBetsFetcher();
        var selector  = new FakeBetSelector();
        var publisher = new FakeBetPublisher();
        var service   = CreateService(history, tipsters, fetcher, selector, publisher);

        await service.RunCycleAsync();

        Assert.Equal(1, history.PurgeCallCount);
        Assert.Equal(1, tipsters.CallCount);
        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(1, selector.CallCount);
        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task RunCycleAsync_PassesSelectedBetsToPublisher()
    {
        var selectedBets = new List<SettledBet>
        {
            new SettledBet { Id = 10 },
            new SettledBet { Id = 20 }
        };
        var selector  = new FakeBetSelector { SelectionToReturn = selectedBets };
        var publisher = new FakeBetPublisher();
        var service   = CreateService(selector: selector, publisher: publisher);

        await service.RunCycleAsync();

        Assert.Equal(2, publisher.LastSelected.Count);
        Assert.Contains(publisher.LastSelected, b => b.Id == 10);
        Assert.Contains(publisher.LastSelected, b => b.Id == 20);
    }

    [Fact]
    public void PostingCycleService_RegisteredAsScoped()
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
        services.AddScoped<ITipsterService, TipsterService>();
        services.AddScoped<IUpcomingBetsFetcher, UpcomingBetsFetcher>();
        services.AddScoped<IBetSelector, BetSelector>();
        services.AddScoped<IBetPublisher, BetPublisher>();
        services.AddScoped<IPostingCycleService, PostingCycleService>();

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();
        var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
        Assert.NotNull(cycleService);
    }
}
