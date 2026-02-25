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
    }

    private class FakeExecutionStateService : IExecutionStateService
    {
        private string _scheduleTime = "08:00";

        public FakeExecutionStateService(string scheduleTime = "08:00")
        {
            _scheduleTime = scheduleTime;
        }

        public ExecutionState GetState() => new(null, null, null, null, null);
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) { }
        public void SetApiConnectionStatus(bool connected) { }
        public bool GetSchedulingEnabled() => true;
        public void SetSchedulingEnabled(bool enabled) { }
        public string GetScheduleTime() => _scheduleTime;
        public void SetScheduleTime(string time) { _scheduleTime = time; }
    }

    private class FakeMessageFormatter : IMessageFormatter
    {
        public string LastApiConnected { get; private set; } = "";
        public int LastTipsterCount { get; private set; }
        public string LastScheduleTime { get; private set; } = "";

        public string FormatOnboardingMessage(bool apiConnected, int tipsterCount, string scheduleTime)
        {
            LastApiConnected = apiConnected ? "connected" : "disconnected";
            LastTipsterCount = tipsterCount;
            LastScheduleTime = scheduleTime;
            return $"ONBOARDING|api={apiConnected}|tipsters={tipsterCount}|schedule={scheduleTime}";
        }

        public string FormatStatus(ExecutionState state) => "";
        public string FormatHistory(List<HistoryEntry> entries) => "";
        public string FormatTipsters(List<TipsterConfig> tipsters) => "";
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
        var executionState = new FakeExecutionStateService("10:30");

        var svc = CreateService(
            historyManager: new FakeHistoryManager(keys: []),
            executionState: executionState,
            formatter: formatter);

        await svc.TrySendOnboardingAsync();

        Assert.Equal("10:30", formatter.LastScheduleTime);
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
        public Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFailureAsync(string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyNoFilteredCandidatesAsync(string filterDetails, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendMessageAsync(string message, CancellationToken ct = default) =>
            throw new Exception("Erreur réseau Telegram");
    }
}
