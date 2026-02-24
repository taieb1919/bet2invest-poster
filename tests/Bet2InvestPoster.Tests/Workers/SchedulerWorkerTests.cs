using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
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

        public Task RunCycleAsync(CancellationToken ct = default)
        {
            RunCount++;
            CycleExecuted.TrySetResult();
            if (ShouldThrow)
                throw new InvalidOperationException("Simulated cycle failure");
            return Task.CompletedTask;
        }
    }

    private class FakeExecutionStateService : IExecutionStateService
    {
        public DateTimeOffset? NextRunSet { get; private set; }
        public int SetNextRunCallCount { get; private set; }

        public ExecutionState GetState() => new(null, null, null, NextRunSet);
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt)
        {
            NextRunSet = nextRunAt;
            SetNextRunCallCount++;
        }
    }

    // ──────────────────────────────── Helpers ────────────────────────────────

    private static SchedulerWorker CreateWorker(
        FakeTimeProvider fakeTime,
        FakeExecutionStateService fakeState,
        FakePostingCycleService fakeCycle,
        string scheduleTime = "08:00")
    {
        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => fakeCycle);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new PosterOptions { ScheduleTime = scheduleTime });
        return new SchedulerWorker(
            sp, fakeState, options, fakeTime,
            NullLogger<SchedulerWorker>.Instance);
    }

    // ──────────────────────── CalculateNextRun tests ────────────────────────

    [Fact]
    public void CalculatesNextRun_WhenTimeNotPassedToday_SchedulesToday()
    {
        // 07:59:00 UTC — schedule is 08:00
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var worker = CreateWorker(fakeTime, new FakeExecutionStateService(), new FakePostingCycleService());

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
        var worker = CreateWorker(fakeTime, new FakeExecutionStateService(), new FakePostingCycleService());

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
        var worker = CreateWorker(fakeTime, new FakeExecutionStateService(), new FakePostingCycleService());

        var next = worker.CalculateNextRun();

        var expected = new DateTimeOffset(2026, 2, 26, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, next);
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
        var worker = CreateWorker(fakeTime, fakeState, fakeCycle);

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
        var worker = CreateWorker(fakeTime, fakeState, fakeCycle);

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
        var worker = CreateWorker(fakeTime, fakeState, fakeCycle);

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
        var worker = CreateWorker(fakeTime, fakeState, fakeCycle);

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
        var worker = CreateWorker(fakeTime, fakeState, fakeCycle);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(50); // let worker start

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // Cycle never ran — cancelled while waiting for 24h delay
        Assert.Equal(0, fakeCycle.RunCount);
    }
}
