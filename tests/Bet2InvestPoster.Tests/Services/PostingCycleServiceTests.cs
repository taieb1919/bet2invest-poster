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

    private sealed class FakeExtendedClient : IExtendedBet2InvestClient
    {
        public bool IsAuthenticated => true;
        public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(int tipsterNumericId, CancellationToken ct = default)
            => Task.FromResult((true, new List<PendingBet>()));
        public Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeHistoryManager : IHistoryManager
    {
        private readonly List<string>? _callOrder;
        public int PurgeCallCount { get; private set; }

        public FakeHistoryManager(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<string>());
        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task PurgeOldEntriesAsync(CancellationToken ct = default)
        {
            PurgeCallCount++;
            _callOrder?.Add("Purge");
            return Task.CompletedTask;
        }

        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default)
            => Task.FromResult(new List<HistoryEntry>());
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

        public Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeUpcomingBetsFetcher : IUpcomingBetsFetcher
    {
        private readonly List<string>? _callOrder;
        public int CallCount { get; private set; }
        public List<PendingBet> BetsToReturn { get; set; } = [];

        public FakeUpcomingBetsFetcher(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<List<PendingBet>> FetchAllAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
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
        public List<PendingBet> SelectionToReturn { get; set; } = [];

        public FakeBetSelector(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<List<PendingBet>> SelectAsync(List<PendingBet> candidates, CancellationToken ct = default)
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
        public List<PendingBet> LastSelected { get; private set; } = [];

        public FakeBetPublisher(List<string>? callOrder = null) => _callOrder = callOrder;

        public Task<int> PublishAllAsync(List<PendingBet> selected, CancellationToken ct = default)
        {
            CallCount++;
            LastSelected = selected;
            _callOrder?.Add("PublishAll");
            return Task.FromResult(selected.Count);
        }
    }

    // ─── Fakes (notification + state) ────────────────────────────────────────

    internal sealed class FakeNotificationService : INotificationService
    {
        public int SuccessCallCount { get; private set; }
        public int FailureCallCount { get; private set; }
        public int NoFilteredCandidatesCallCount { get; private set; }
        public int? LastSuccessCount { get; private set; }
        public string? LastFailureReason { get; private set; }
        public string? LastFilterDetails { get; private set; }

        public Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default)
        {
            SuccessCallCount++;
            LastSuccessCount = publishedCount;
            return Task.CompletedTask;
        }

        public Task NotifyFailureAsync(string reason, CancellationToken ct = default)
        {
            FailureCallCount++;
            LastFailureReason = reason;
            return Task.CompletedTask;
        }

        public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyNoFilteredCandidatesAsync(string filterDetails, CancellationToken ct = default)
        {
            NoFilteredCandidatesCallCount++;
            LastFilterDetails = filterDetails;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeExecutionStateService : IExecutionStateService
    {
        public int? LastSuccessCount { get; private set; }
        public string? LastFailureReason { get; private set; }
        public bool RecordSuccessCalled { get; private set; }
        public bool RecordFailureCalled { get; private set; }

        public ExecutionState GetState() => new(null, null, null, null, null);
        public void RecordSuccess(int publishedCount) { RecordSuccessCalled = true; LastSuccessCount = publishedCount; }
        public void RecordFailure(string reason) { RecordFailureCalled = true; LastFailureReason = reason; }
        public void SetNextRun(DateTimeOffset nextRunAt) { }
        public void SetApiConnectionStatus(bool connected) { }
        public bool GetSchedulingEnabled() => true;
        public void SetSchedulingEnabled(bool enabled) { }
        public string GetScheduleTime() => "08:00";
        public void SetScheduleTime(string time) { }
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static PostingCycleService CreateService(
        FakeHistoryManager?      history      = null,
        FakeTipsterService?      tipsters     = null,
        FakeUpcomingBetsFetcher? fetcher      = null,
        FakeBetSelector?         selector     = null,
        FakeBetPublisher?        publisher    = null,
        FakeNotificationService? notification = null,
        FakeExecutionStateService? state      = null,
        PosterOptions?           options      = null)
        => new PostingCycleService(
            new FakeExtendedClient(),
            history      ?? new FakeHistoryManager(),
            tipsters     ?? new FakeTipsterService(),
            fetcher      ?? new FakeUpcomingBetsFetcher(),
            selector     ?? new FakeBetSelector(),
            publisher    ?? new FakeBetPublisher(),
            notification ?? new FakeNotificationService(),
            state        ?? new FakeExecutionStateService(),
            Options.Create(options ?? new PosterOptions()),
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
        var selectedBets = new List<PendingBet>
        {
            new PendingBet { Id = 10 },
            new PendingBet { Id = 20 }
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
    public async Task RunCycleAsync_WhenZeroCandidatesWithActiveFilters_NotifiesAndReturns()
    {
        // Arrange : BetSelector retourne liste vide + filtre actif (MinOdds configuré)
        var selector     = new FakeBetSelector { SelectionToReturn = [] };
        var fetcher      = new FakeUpcomingBetsFetcher { BetsToReturn = [new PendingBet { Id = 1 }] };
        var publisher    = new FakeBetPublisher();
        var notification = new FakeNotificationService();
        var state        = new FakeExecutionStateService();
        var options      = new PosterOptions { MinOdds = 5.00m };

        var service = CreateService(
            selector: selector,
            fetcher: fetcher,
            publisher: publisher,
            notification: notification,
            state: state,
            options: options);

        // Act
        await service.RunCycleAsync();

        // Assert : notification "aucun candidat filtré" envoyée
        Assert.Equal(1, notification.NoFilteredCandidatesCallCount);
        Assert.NotNull(notification.LastFilterDetails);
        Assert.Contains("5", notification.LastFilterDetails); // contient la valeur MinOdds

        // Assert : PublishAllAsync NON appelé
        Assert.Equal(0, publisher.CallCount);

        // Assert : RecordSuccess NON appelé (cycle s'est arrêté avant)
        Assert.False(state.RecordSuccessCalled);
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
        services.AddSingleton<INotificationService>(_ => new FakeNotificationService());
        services.AddSingleton<IExecutionStateService, ExecutionStateService>();
        services.AddScoped<IPostingCycleService, PostingCycleService>();

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();
        var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
        Assert.NotNull(cycleService);
    }
}
