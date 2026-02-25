using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Bet2InvestPoster.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Tests.Services;

public class OnboardingServiceTests
{
    // --- Fakes ---

    private class FakeHistoryManager : IHistoryManager
    {
        private readonly HashSet<string> _keys;

        public FakeHistoryManager(HashSet<string>? keys = null)
        {
            _keys = keys ?? [];
        }

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default) =>
            Task.FromResult(_keys);

        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task PurgeOldEntriesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default) =>
            Task.FromResult(new List<HistoryEntry>());
        public Task UpdateEntriesAsync(List<HistoryEntry> updatedEntries, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct = default) =>
            Task.FromResult(new List<HistoryEntry>());
    }

    private class FakeMessageFormatter : IMessageFormatter
    {
        public string LastApiConnected { get; private set; } = "";
        public int LastTipsterCount { get; private set; }
        public string[] LastScheduleTimes { get; private set; } = [];

        public string FormatOnboardingMessage(bool apiConnected, int tipsterCount, string[] scheduleTimes)
        {
            LastApiConnected = apiConnected ? "connected" : "disconnected";
            LastTipsterCount = tipsterCount;
            LastScheduleTimes = scheduleTimes;
            return $"ONBOARDING|api={apiConnected}|tipsters={tipsterCount}|schedule={string.Join(",", scheduleTimes)}";
        }

        public string FormatStatus(ExecutionState state) => "";
        public string FormatHistory(List<HistoryEntry> entries) => "";
        public string FormatTipsters(List<TipsterConfig> tipsters) => "";
        public string FormatScrapedTipsters(List<ScrapedTipster> tipsters) => "";
        public string FormatScrapedTipstersConfirmation() => "";
        public string FormatReport(List<HistoryEntry> entries, int days) => "";
        public string FormatCycleSuccess(Bet2InvestPoster.Models.CycleResult result) => "";
    }

    private class FakeTipsterService : ITipsterService
    {
        private readonly List<TipsterConfig> _tipsters;
        public bool ThrowOnLoad { get; set; }

        public FakeTipsterService(List<TipsterConfig>? tipsters = null)
        {
            _tipsters = tipsters ?? [];
        }

        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default)
        {
            if (ThrowOnLoad) throw new IOException("Erreur lecture tipsters");
            return Task.FromResult(_tipsters);
        }

        public Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default) =>
            Task.FromResult(new TipsterConfig { Url = url, Name = "slug" });

        public Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task ReplaceTipstersAsync(List<TipsterConfig> tipsters, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private class FakeExtendedBet2InvestClient : IExtendedBet2InvestClient
    {
        public bool IsAuthenticated { get; private set; }
        public bool ThrowOnLogin { get; set; }

        public Task LoginAsync(CancellationToken ct = default)
        {
            if (ThrowOnLogin) throw new Exception("Credentials invalides");
            IsAuthenticated = true;
            return Task.CompletedTask;
        }

        public Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default) => Task.CompletedTask;

        public Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(int tipsterNumericId, CancellationToken ct = default) =>
            Task.FromResult((false, new List<PendingBet>()));

        public Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
        public Task<List<ScrapedTipster>> GetFreeTipstersAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ScrapedTipster>());
        public Task<List<JTDev.Bet2InvestScraper.Models.SettledBet>> GetSettledBetsForTipsterAsync(int numericId, DateTime startDate, DateTime endDate, CancellationToken ct = default) =>
            Task.FromResult(new List<JTDev.Bet2InvestScraper.Models.SettledBet>());
    }

    // --- Helpers ---

    private static OnboardingService CreateService(
        IHistoryManager? historyManager = null,
        IExecutionStateService? executionState = null,
        INotificationService? notification = null,
        IMessageFormatter? formatter = null,
        FakeTipsterService? tipsterService = null,
        FakeExtendedBet2InvestClient? apiClient = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITipsterService>(tipsterService ?? new FakeTipsterService());
        services.AddSingleton<IExtendedBet2InvestClient>(apiClient ?? new FakeExtendedBet2InvestClient());
        var sp = services.BuildServiceProvider();

        return new OnboardingService(
            historyManager ?? new FakeHistoryManager(),
            executionState ?? new FakeExecutionStateService(),
            notification ?? new FakeNotificationService(),
            formatter ?? new FakeMessageFormatter(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OnboardingService>.Instance);
    }

    // --- Tests ---

    [Fact]
    public async Task TrySendOnboardingAsync_HistoryVide_EnvoieMessage()
    {
        var notification = new FakeNotificationService();
        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            notification: notification);

        await svc.TrySendOnboardingAsync();

        Assert.Single(notification.SentMessages);
    }

    [Fact]
    public async Task TrySendOnboardingAsync_HistoryNonVide_NAucunMessageEnvoye()
    {
        var notification = new FakeNotificationService();
        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: ["key1", "key2"]),
            notification: notification);

        await svc.TrySendOnboardingAsync();

        Assert.Empty(notification.SentMessages);
    }

    [Fact]
    public async Task TrySendOnboardingAsync_ApiConnexionEchoue_EnvoieMessageDegrades()
    {
        var notification = new FakeNotificationService();
        var formatter = new FakeMessageFormatter();
        var apiClient = new FakeExtendedBet2InvestClient { ThrowOnLogin = true };

        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            notification: notification,
            formatter: formatter,
            apiClient: apiClient);

        await svc.TrySendOnboardingAsync();

        Assert.Single(notification.SentMessages);
        Assert.Equal("disconnected", formatter.LastApiConnected);
    }

    [Fact]
    public async Task TrySendOnboardingAsync_ApiConnexionReussie_MessageAvecConnectedTrue()
    {
        var formatter = new FakeMessageFormatter();
        var tipsters = new List<TipsterConfig>
        {
            new() { Url = "http://example.com/t1", Name = "Tipster1" },
            new() { Url = "http://example.com/t2", Name = "Tipster2" },
        };

        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            formatter: formatter,
            tipsterService: new FakeTipsterService(tipsters),
            apiClient: new FakeExtendedBet2InvestClient());

        await svc.TrySendOnboardingAsync();

        Assert.Equal("connected", formatter.LastApiConnected);
        Assert.Equal(2, formatter.LastTipsterCount);
    }

    [Fact]
    public async Task TrySendOnboardingAsync_NeLevePasException_MemeEnCasErreurGlobale()
    {
        // NotificationService qui lève une exception
        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            notification: new ThrowingNotificationService());

        // Ne doit pas lever d'exception
        await svc.TrySendOnboardingAsync();
    }

    [Fact]
    public async Task TrySendOnboardingAsync_IncludeScheduleTime_DansLeMessage()
    {
        var formatter = new FakeMessageFormatter();
        var executionState = new FakeExecutionStateService(scheduleTimes: ["10:30"]);

        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            executionState: executionState,
            formatter: formatter);

        await svc.TrySendOnboardingAsync();

        Assert.Contains("10:30", formatter.LastScheduleTimes);
    }

    [Fact]
    public async Task TrySendOnboardingAsync_TipsterLoadFails_EnvoieMessageAvecZeroTipsters()
    {
        var formatter = new FakeMessageFormatter();
        var tipsterService = new FakeTipsterService { ThrowOnLoad = true };

        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            formatter: formatter,
            tipsterService: tipsterService);

        await svc.TrySendOnboardingAsync();

        Assert.Equal(0, formatter.LastTipsterCount);
    }

    // --- Fake levant exception ---

    private class ThrowingNotificationService : INotificationService
    {
        public Task NotifySuccessAsync(Bet2InvestPoster.Models.CycleResult result, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFailureAsync(string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyNoFilteredCandidatesAsync(string filterDetails, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendMessageAsync(string message, CancellationToken ct = default) =>
            throw new Exception("Erreur réseau Telegram");
    }
}
