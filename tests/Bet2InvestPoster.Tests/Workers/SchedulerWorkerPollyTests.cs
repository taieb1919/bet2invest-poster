using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bet2InvestPoster.Tests.Workers;

/// <summary>
/// Tests for SchedulerWorker + Polly integration (Story 5.2).
/// Verifies that NotifyFinalFailureAsync is called only when all retries are exhausted.
/// </summary>
public class SchedulerWorkerPollyTests
{
    // ──────────────────────────────── Fakes ────────────────────────────────

    private class FailingPostingCycleService : IPostingCycleService
    {
        public int CallCount { get; private set; }
        public int FailCount { get; set; } = int.MaxValue; // Default: always fail
        public TaskCompletionSource FirstCallExecuted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SuccessExecuted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunCycleAsync(CancellationToken ct = default)
        {
            CallCount++;
            FirstCallExecuted.TrySetResult();
            if (CallCount <= FailCount)
                throw new InvalidOperationException($"Simulated failure #{CallCount}");
            SuccessExecuted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private class FakeNotificationService : INotificationService
    {
        public int FinalFailureCount { get; private set; }
        public string? LastFinalFailureReason { get; private set; }
        public int LastFinalFailureAttempts { get; private set; }
        public TaskCompletionSource FinalFailureCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFailureAsync(string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default)
        {
            FinalFailureCount++;
            LastFinalFailureAttempts = attempts;
            LastFinalFailureReason = reason;
            FinalFailureCalled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private class FakeExecutionStateService : IExecutionStateService
    {
        public ExecutionState GetState() => new(null, null, null, null);
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) { }
    }

    // ──────────────────────────────── Helpers ────────────────────────────────

    private static (SchedulerWorker worker, FakeNotificationService notification) CreateWorker(
        FakeTimeProvider fakeTime,
        FailingPostingCycleService cycleService,
        int maxRetryCount = 3)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => cycleService);
        var sp = services.BuildServiceProvider();

        var notification = new FakeNotificationService();
        var options = Options.Create(new PosterOptions
        {
            ScheduleTime = "08:00",
            MaxRetryCount = maxRetryCount,
            RetryDelayMs = 0  // No delay in tests
        });

        // Use the real ResiliencePipelineService with RetryDelayMs=0
        var resilience = new ResiliencePipelineService(options, NullLogger<ResiliencePipelineService>.Instance);

        var worker = new SchedulerWorker(
            sp,
            new FakeExecutionStateService(),
            resilience,
            notification,
            options,
            fakeTime,
            NullLogger<SchedulerWorker>.Instance);

        return (worker, notification);
    }

    // ──────────────────────────────── Tests ────────────────────────────────

    [Fact]
    public async Task SchedulerWorker_AllRetriesExhausted_CallsNotifyFinalFailure()
    {
        // 07:59 — schedule at 08:00 (1 minute away)
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        // MaxRetryCount=3 → cycle called 3 times total (1 initial + 2 retries), all fail
        var cycleService = new FailingPostingCycleService { FailCount = int.MaxValue };
        var (worker, notification) = CreateWorker(fakeTime, cycleService, maxRetryCount: 3);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(50); // let worker reach Task.Delay

        fakeTime.Advance(TimeSpan.FromMinutes(1)); // trigger cycle

        // Wait for NotifyFinalFailureAsync to be called (deterministic signal)
        await notification.FinalFailureCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        // All 3 attempts exhausted → NotifyFinalFailureAsync called exactly once
        Assert.Equal(1, notification.FinalFailureCount);
        Assert.Equal(3, notification.LastFinalFailureAttempts);
        Assert.Equal("InvalidOperationException", notification.LastFinalFailureReason);
    }

    [Fact]
    public async Task SchedulerWorker_SuccessAfterRetry_NoFinalFailureNotification()
    {
        // 07:59 — cycle fails once then succeeds on retry
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        // FailCount=1 → fails 1st attempt, succeeds on 2nd
        var cycleService = new FailingPostingCycleService { FailCount = 1 };
        var (worker, notification) = CreateWorker(fakeTime, cycleService, maxRetryCount: 3);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);

        fakeTime.Advance(TimeSpan.FromMinutes(1));

        // Wait for the successful attempt (deterministic signal)
        await cycleService.SuccessExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        // Succeeded after 1 retry → no final failure notification
        Assert.Equal(0, notification.FinalFailureCount);
        Assert.True(cycleService.CallCount >= 2); // at least 1 fail + 1 success
    }
}
