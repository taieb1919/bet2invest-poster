using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Tests.Helpers;
using Bet2InvestPoster.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bet2InvestPoster.Tests.Workers;

public class SchedulerWorkerTests
{
    // ──────────────────────────────── Fakes ────────────────────────────────

    private class FakePostingCycleService : IPostingCycleService
    {
        public int RunCount { get; private set; }
        public bool ShouldThrow { get; set; }
        public TaskCompletionSource CycleExecuted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Bet2InvestPoster.Models.CycleResult> RunCycleAsync(CancellationToken ct = default)
        {
            RunCount++;
            CycleExecuted.TrySetResult();
            if (ShouldThrow)
                throw new InvalidOperationException("Simulated cycle failure");
            return Task.FromResult(new Bet2InvestPoster.Models.CycleResult());
        }
    }

    // Pass-through: executes the action once, no retry (for existing tests unrelated to Polly)
    private class FakeResiliencePipelineService : IResiliencePipelineService
    {
        public async Task ExecuteCycleWithRetryAsync(Func<CancellationToken, Task> cycleAction, CancellationToken ct = default)
            => await cycleAction(ct);

        public Bet2InvestPoster.Models.CircuitBreakerState GetCircuitBreakerState()
            => Bet2InvestPoster.Models.CircuitBreakerState.Closed;

        public TimeSpan? GetCircuitBreakerRemainingDuration() => null;
    }

    // ──────────────────────────────── Helpers ────────────────────────────────

    private static (SchedulerWorker worker, FakeNotificationService notificationService) CreateWorker(
        FakeTimeProvider fakeTime,
        FakeExecutionStateService fakeState,
        FakePostingCycleService fakeCycle,
        string scheduleTime = "08:00",
        IResiliencePipelineService? resilience = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => fakeCycle);
        var sp = services.BuildServiceProvider();

        var fakeNotification = new FakeNotificationService();
        var options = Options.Create(new PosterOptions { ScheduleTime = scheduleTime, MaxRetryCount = 3 });
        var worker = new SchedulerWorker(
            sp, fakeState,
            resilience ?? new FakeResiliencePipelineService(),
            fakeNotification,
            options, fakeTime,
            NullLogger<SchedulerWorker>.Instance);
        return (worker, fakeNotification);
    }

    // ──────────────────────── CalculateNextRun tests ────────────────────────

    [Fact]
    public void CalculatesNextRun_WhenTimeNotPassedToday_SchedulesToday()
    {
        // 07:59:00 UTC — schedule is 08:00
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var (worker, _) = CreateWorker(fakeTime, new FakeExecutionStateService(), new FakePostingCycleService());

        var next = worker.CalculateNextRun();

        var expected = new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void CalculatesNextRun_WhenTimePassed_SchedulesTomorrow()
    {
        // 09:00:00 UTC — schedule is 08:00 (already passed)
        var now = new DateTimeOffset(2026, 2, 25, 9, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var (worker, _) = CreateWorker(fakeTime, new FakeExecutionStateService(), new FakePostingCycleService());

        var next = worker.CalculateNextRun();

        var expected = new DateTimeOffset(2026, 2, 26, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void CalculatesNextRun_WhenExactlyAtScheduleTime_SchedulesTomorrow()
    {
        // Exactly 08:00:00 UTC — schedule is 08:00 (equal, not greater → tomorrow)
        var now = new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var (worker, _) = CreateWorker(fakeTime, new FakeExecutionStateService(), new FakePostingCycleService());

        var next = worker.CalculateNextRun();

        var expected = new DateTimeOffset(2026, 2, 26, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void CalculateNextRun_UsesExecutionStateServiceScheduleTime_Dynamically()
    {
        // 07:00 UTC — schedule initially 08:00
        var now = new DateTimeOffset(2026, 2, 25, 7, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var dynamicState = new FakeExecutionStateService();
        // Create worker directly with dynamic state for CalculateNextRun
        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => new FakePostingCycleService());
        var sp = services.BuildServiceProvider();
        var options = Options.Create(new PosterOptions { ScheduleTime = "08:00", MaxRetryCount = 3 });
        var workerDynamic = new SchedulerWorker(
            sp, dynamicState,
            new FakeResiliencePipelineService(),
            new FakeNotificationService(),
            options, fakeTime,
            NullLogger<SchedulerWorker>.Instance);

        // Avec 08:00 initial
        var next1 = workerDynamic.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero), next1);

        // Changer l'heure → 14:30
        dynamicState.SetScheduleTime("14:30");
        var next2 = workerDynamic.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 14, 30, 0, TimeSpan.Zero), next2);
    }

    // ──────────────────────── ExecuteAsync tests ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SetsNextRun_OnStartup()
    {
        // Time before schedule — worker sets NextRun immediately at startup
        var now = new DateTimeOffset(2026, 2, 25, 7, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var fakeState = new FakeExecutionStateService();
        var fakeCycle = new FakePostingCycleService();
        var (worker, _) = CreateWorker(fakeTime, fakeState, fakeCycle);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // Short real-time wait so the background task can call SetNextRun
        await Task.Delay(100);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(fakeState.SetNextRunCallCount >= 1);
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero), fakeState.NextRunSet);
    }

    [Fact]
    public async Task ExecuteAsync_CallsRunCycleAsync_AtScheduledTime()
    {
        // 07:59 — 1 minute before schedule 08:00
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var fakeState = new FakeExecutionStateService();
        var fakeCycle = new FakePostingCycleService();
        var (worker, _) = CreateWorker(fakeTime, fakeState, fakeCycle);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // Let worker start and reach Task.Delay(1 min, fakeTime, ct)
        await Task.Delay(100);

        // Advance fake time to trigger the scheduled Task.Delay
        fakeTime.Advance(TimeSpan.FromMinutes(1));

        // Wait for cycle execution (deterministic sync via TaskCompletionSource)
        await fakeCycle.CycleExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, fakeCycle.RunCount);
    }

    [Fact]
    public async Task ExecuteAsync_SetsNextRun_AfterCycleCompletes()
    {
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var fakeState = new FakeExecutionStateService();
        var fakeCycle = new FakePostingCycleService();
        var (worker, _) = CreateWorker(fakeTime, fakeState, fakeCycle);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);

        fakeTime.Advance(TimeSpan.FromMinutes(1)); // trigger cycle
        await fakeCycle.CycleExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Let the post-cycle loop iteration call SetNextRun again
        await Task.Delay(100);

        await worker.StopAsync(CancellationToken.None);

        // SetNextRun called at least twice: once at startup and once after cycle
        Assert.True(fakeState.SetNextRunCallCount >= 2);
        // After cycle completes with time now at 08:00, next run = tomorrow 08:00
        var expectedTomorrow = new DateTimeOffset(2026, 2, 26, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(expectedTomorrow, fakeState.NextRunSet);
    }

    [Fact]
    public async Task ExecuteAsync_SetsNextRun_EvenAfterCycleFailure()
    {
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var fakeState = new FakeExecutionStateService();
        var fakeCycle = new FakePostingCycleService { ShouldThrow = true };
        var (worker, _) = CreateWorker(fakeTime, fakeState, fakeCycle);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);

        fakeTime.Advance(TimeSpan.FromMinutes(1)); // trigger failing cycle
        await fakeCycle.CycleExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Let the post-cycle error handling and loop iteration proceed
        await Task.Delay(100);

        await worker.StopAsync(CancellationToken.None);

        // Cycle ran (and failed) — but the worker continues, SetNextRun called again
        Assert.Equal(1, fakeCycle.RunCount);
        Assert.True(fakeState.SetNextRunCallCount >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        // Schedule already passed — next run is tomorrow (24h delay)
        var now = new DateTimeOffset(2026, 2, 25, 9, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var fakeState = new FakeExecutionStateService();
        var fakeCycle = new FakePostingCycleService();
        var (worker, _) = CreateWorker(fakeTime, fakeState, fakeCycle);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(50); // let worker start

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // Cycle never ran — cancelled while waiting for 24h delay
        Assert.Equal(0, fakeCycle.RunCount);
    }

    // ─── SchedulingEnabled tests ──────────────────────────────────────────────

    private class ControllableExecutionStateService : IExecutionStateService
    {
        private volatile bool _schedulingEnabled;
        public DateTimeOffset? NextRunSet { get; private set; }
        public int SetNextRunCallCount { get; private set; }

        public ControllableExecutionStateService(bool schedulingEnabled = true)
            => _schedulingEnabled = schedulingEnabled;

        public ExecutionState GetState() => new(null, null, null, NextRunSet, null);
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) { NextRunSet = nextRunAt; SetNextRunCallCount++; }
        public void SetApiConnectionStatus(bool connected) { }
        public bool GetSchedulingEnabled() => _schedulingEnabled;
        public void SetSchedulingEnabled(bool enabled) => _schedulingEnabled = enabled;
        public string[] GetScheduleTimes() => ["08:00"];
        public void SetScheduleTimes(string[] times) { }
        public string GetScheduleTime() => "08:00";
        public void SetScheduleTime(string time) { }
    }

    /// <summary>
    /// FakeTimeProvider that signals each time a timer is created (worker reached Task.Delay).
    /// Eliminates race conditions from Task.Delay(50) in tests.
    /// </summary>
    private class CountingFakeTimeProvider(DateTimeOffset startTime) : FakeTimeProvider(startTime)
    {
        private int _timerCount;
        private TaskCompletionSource _nextTimerTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timerCount);
            _nextTimerTcs.TrySetResult();
            return base.CreateTimer(callback, state, dueTime, period);
        }

        /// <summary>Wait for the next timer registration (deterministic signal).</summary>
        public async Task WaitForNextTimerAsync(TimeSpan timeout)
        {
            await _nextTimerTcs.Task.WaitAsync(timeout);
            _nextTimerTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSchedulingDisabled_DoesNotRunCycle_UntilEnabled()
    {
        // Scheduling disabled — worker enters polling loop immediately (before calculating next run)
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new CountingFakeTimeProvider(now);
        var controllableState = new ControllableExecutionStateService(schedulingEnabled: false);
        var fakeCycle = new FakePostingCycleService();

        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => fakeCycle);
        var sp = services.BuildServiceProvider();
        var options = Options.Create(new PosterOptions { ScheduleTime = "08:00", MaxRetryCount = 3 });
        var worker = new SchedulerWorker(
            sp, controllableState,
            new FakeResiliencePipelineService(),
            new FakeNotificationService(),
            options, fakeTime,
            NullLogger<SchedulerWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // Wait for worker to enter the polling loop (5s delay — scheduling disabled)
        await fakeTime.WaitForNextTimerAsync(TimeSpan.FromSeconds(5));

        // Cycle should NOT have run because scheduling is disabled
        Assert.Equal(0, fakeCycle.RunCount);

        // Enable scheduling and advance past polling delay
        controllableState.SetSchedulingEnabled(true);
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        // Wait for the main schedule delay timer (worker exited polling, now waiting for next run)
        await fakeTime.WaitForNextTimerAsync(TimeSpan.FromSeconds(5));

        // Advance past the schedule time to trigger the cycle
        fakeTime.Advance(TimeSpan.FromMinutes(1));
        await fakeCycle.CycleExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, fakeCycle.RunCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSchedulingDisabled_StopsOnCancellation()
    {
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new CountingFakeTimeProvider(now);
        var controllableState = new ControllableExecutionStateService(schedulingEnabled: false);
        var fakeCycle = new FakePostingCycleService();

        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => fakeCycle);
        var sp = services.BuildServiceProvider();
        var options = Options.Create(new PosterOptions { ScheduleTime = "08:00", MaxRetryCount = 3 });
        var worker = new SchedulerWorker(
            sp, controllableState,
            new FakeResiliencePipelineService(),
            new FakeNotificationService(),
            options, fakeTime,
            NullLogger<SchedulerWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // Wait for polling loop timer (scheduling disabled → enters polling immediately)
        await fakeTime.WaitForNextTimerAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, fakeCycle.RunCount);
    }
}
