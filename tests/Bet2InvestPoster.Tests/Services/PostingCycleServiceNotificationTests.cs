using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging.Abstractions;
using static Bet2InvestPoster.Tests.Services.PostingCycleServiceTests;

namespace Bet2InvestPoster.Tests.Services;

/// <summary>Tests covering Story 4.3 — NotificationService integration in PostingCycleService.</summary>
public class PostingCycleServiceNotificationTests
{
    // ─── Pipeline fakes (minimal versions without callOrder tracking) ──────

    private sealed class SimpleHistoryManager : IHistoryManager
    {
        public Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<int>());
        public Task RecordAsync(Models.HistoryEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task PurgeOldEntriesAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class SimpleTipsterService : ITipsterService
    {
        public Task<List<Models.TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Models.TipsterConfig>());
    }

    private sealed class SimpleUpcomingBetsFetcher : IUpcomingBetsFetcher
    {
        public Task<List<SettledBet>> FetchAllAsync(List<Models.TipsterConfig> tipsters, CancellationToken ct = default)
            => Task.FromResult(new List<SettledBet>());
    }

    private sealed class SimpleBetSelector : IBetSelector
    {
        public Task<List<SettledBet>> SelectAsync(List<SettledBet> candidates, CancellationToken ct = default)
            => Task.FromResult(new List<SettledBet>());
    }

    private sealed class ThrowingBetPublisher : IBetPublisher
    {
        public int PublishedCount { get; set; } = 5;
        public bool ShouldThrow { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default)
        {
            if (ShouldThrow)
                throw ExceptionToThrow ?? new InvalidOperationException("Simulated failure");
            return Task.FromResult(PublishedCount);
        }
    }

    // ─── Helper (uses FakeNotificationService + FakeExecutionStateService from PostingCycleServiceTests) ──

    private static (PostingCycleService service, FakeNotificationService notification, FakeExecutionStateService state, ThrowingBetPublisher publisher)
        CreateService(int publishedCount = 5, bool shouldThrow = false, Exception? exception = null)
    {
        var notification = new FakeNotificationService();
        var state = new FakeExecutionStateService();
        var publisher = new ThrowingBetPublisher { PublishedCount = publishedCount, ShouldThrow = shouldThrow, ExceptionToThrow = exception };

        var service = new PostingCycleService(
            new SimpleHistoryManager(),
            new SimpleTipsterService(),
            new SimpleUpcomingBetsFetcher(),
            new SimpleBetSelector(),
            publisher,
            notification,
            state,
            NullLogger<PostingCycleService>.Instance);

        return (service, notification, state, publisher);
    }

    // ─── Tests — succès ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunCycleAsync_Success_CallsNotifySuccessAsync()
    {
        var (service, notification, _, _) = CreateService(publishedCount: 7);

        await service.RunCycleAsync();

        Assert.Equal(1, notification.SuccessCallCount);
        Assert.Equal(0, notification.FailureCallCount);
        Assert.Equal(7, notification.LastSuccessCount);
    }

    [Fact]
    public async Task RunCycleAsync_Success_CallsRecordSuccess()
    {
        var (service, _, state, _) = CreateService(publishedCount: 3);

        await service.RunCycleAsync();

        Assert.True(state.RecordSuccessCalled);
        Assert.False(state.RecordFailureCalled);
        Assert.Equal(3, state.LastSuccessCount);
    }

    [Fact]
    public async Task RunCycleAsync_Success_RecordSuccessCalledBeforeNotify()
    {
        var callOrder = new List<string>();
        var trackingNotification = new TrackingNotificationService(callOrder, "Notify");
        var trackingState = new TrackingExecutionStateService(callOrder, "Record");

        var service = new PostingCycleService(
            new SimpleHistoryManager(),
            new SimpleTipsterService(),
            new SimpleUpcomingBetsFetcher(),
            new SimpleBetSelector(),
            new ThrowingBetPublisher { PublishedCount = 1 },
            trackingNotification,
            trackingState,
            NullLogger<PostingCycleService>.Instance);

        await service.RunCycleAsync();

        Assert.Equal(2, callOrder.Count);
        Assert.Equal("Record", callOrder[0]);
        Assert.Equal("Notify", callOrder[1]);
    }

    // ─── Tests — échec ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunCycleAsync_Failure_CallsNotifyFailureAsync()
    {
        var (service, notification, _, _) = CreateService(shouldThrow: true, exception: new InvalidOperationException("api error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunCycleAsync());

        Assert.Equal(0, notification.SuccessCallCount);
        Assert.Equal(1, notification.FailureCallCount);
    }

    [Fact]
    public async Task RunCycleAsync_Failure_SanitizesReasonWithExceptionTypeName()
    {
        var (service, notification, _, _) = CreateService(shouldThrow: true, exception: new InvalidOperationException("secret password=abc123"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunCycleAsync());

        Assert.Equal("InvalidOperationException", notification.LastFailureReason);
        Assert.DoesNotContain("secret", notification.LastFailureReason!);
    }

    [Fact]
    public async Task RunCycleAsync_Failure_CallsRecordFailure()
    {
        var (service, _, state, _) = CreateService(shouldThrow: true, exception: new HttpRequestException("network error"));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RunCycleAsync());

        Assert.True(state.RecordFailureCalled);
        Assert.False(state.RecordSuccessCalled);
        Assert.Equal("HttpRequestException", state.LastFailureReason);
    }

    [Fact]
    public async Task RunCycleAsync_Failure_RethrowsException()
    {
        var (service, _, _, _) = CreateService(shouldThrow: true, exception: new InvalidOperationException("fail"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunCycleAsync());
        Assert.Equal("fail", ex.Message);
    }

    // ─── Helper tracking fakes ────────────────────────────────────────────────

    private sealed class TrackingNotificationService : INotificationService
    {
        private readonly List<string> _order;
        private readonly string _tag;
        public TrackingNotificationService(List<string> order, string tag) { _order = order; _tag = tag; }
        public Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default) { _order.Add(_tag); return Task.CompletedTask; }
        public Task NotifyFailureAsync(string reason, CancellationToken ct = default) { _order.Add(_tag); return Task.CompletedTask; }
        public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default) { _order.Add(_tag); return Task.CompletedTask; }
    }

    private sealed class TrackingExecutionStateService : IExecutionStateService
    {
        private readonly List<string> _order;
        private readonly string _tag;
        public TrackingExecutionStateService(List<string> order, string tag) { _order = order; _tag = tag; }
        public ExecutionState GetState() => new(null, null, null, null);
        public void RecordSuccess(int publishedCount) => _order.Add(_tag);
        public void RecordFailure(string reason) => _order.Add(_tag);
        public void SetNextRun(DateTimeOffset nextRunAt) { }
    }
}
