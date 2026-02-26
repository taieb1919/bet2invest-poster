using System.Globalization;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Bet2InvestPoster.Tests.Helpers;
using Bet2InvestPoster.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using global::Telegram.Bot.Types;
using Bet2InvestPoster.Tests.Telegram.Commands;

namespace Bet2InvestPoster.Tests;

// ════════════════════════════════════════════════════════════════════════════
// Story 14.1 — Scheduling multi-horaires configurable
// ════════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────────────
// 7.5 — PosterOptions.GetEffectiveScheduleTimes()
// ─────────────────────────────────────────────────────────────────────────────
public class PosterOptionsScheduleTimesTests
{
    [Fact]
    public void GetEffectiveScheduleTimes_ScheduleTimesSet_ReturnsScheduleTimes()
    {
        var opts = new PosterOptions
        {
            ScheduleTime = "08:00",
            ScheduleTimes = ["10:00", "15:00"]
        };
        var result = opts.GetEffectiveScheduleTimes();
        Assert.Equal(["10:00", "15:00"], result);
    }

    [Fact]
    public void GetEffectiveScheduleTimes_ScheduleTimesNull_FallsBackToScheduleTime()
    {
        var opts = new PosterOptions
        {
            ScheduleTime = "14:00",
            ScheduleTimes = null
        };
        var result = opts.GetEffectiveScheduleTimes();
        Assert.Equal(["14:00"], result);
    }

    [Fact]
    public void GetEffectiveScheduleTimes_ScheduleTimesEmpty_FallsBackToScheduleTime()
    {
        var opts = new PosterOptions
        {
            ScheduleTime = "09:00",
            ScheduleTimes = []
        };
        var result = opts.GetEffectiveScheduleTimes();
        Assert.Equal(["09:00"], result);
    }

    [Fact]
    public void GetEffectiveScheduleTimes_BothNull_ReturnsDefault3Times()
    {
        var opts = new PosterOptions
        {
            ScheduleTime = "",
            ScheduleTimes = null
        };
        var result = opts.GetEffectiveScheduleTimes();
        Assert.Equal(3, result.Length);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7.4 — ExecutionStateService multi-horaires (persistence, migration)
// ─────────────────────────────────────────────────────────────────────────────
public class ExecutionStateServiceMultiScheduleTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetScheduleTimes_DefaultsToSingleTime()
    {
        var svc = new ExecutionStateService(defaultScheduleTime: "08:00");
        Assert.Equal(["08:00"], svc.GetScheduleTimes());
    }

    [Fact]
    public void GetScheduleTimes_WithDefaultMultipleTimes_ReturnsThem()
    {
        var svc = new ExecutionStateService(defaultScheduleTimes: ["08:00", "13:00", "19:00"]);
        Assert.Equal(["08:00", "13:00", "19:00"], svc.GetScheduleTimes());
    }

    [Fact]
    public void SetScheduleTimes_UpdatesAndPersists()
    {
        Directory.CreateDirectory(_tempDir);
        var svc = new ExecutionStateService(_tempDir, defaultScheduleTimes: ["08:00"]);
        svc.SetScheduleTimes(["07:00", "14:00", "21:00"]);

        Assert.Equal(["07:00", "14:00", "21:00"], svc.GetScheduleTimes());

        // Vérifie que le fichier est écrit
        var file = Path.Combine(_tempDir, "scheduling-state.json");
        Assert.True(File.Exists(file));
        var content = File.ReadAllText(file);
        Assert.Contains("07:00", content);
        Assert.Contains("scheduleTimes", content);
    }

    [Fact]
    public void LoadSchedulingState_NewFormat_LoadsArray()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(file, "{\"schedulingEnabled\":true,\"scheduleTimes\":[\"08:00\",\"13:00\",\"19:00\"]}");

        var svc = new ExecutionStateService(_tempDir);
        Assert.Equal(["08:00", "13:00", "19:00"], svc.GetScheduleTimes());
    }

    [Fact]
    public void LoadSchedulingState_OldFormat_MigratesStringToArray()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "scheduling-state.json");
        File.WriteAllText(file, "{\"schedulingEnabled\":true,\"scheduleTime\":\"15:30\"}");

        var svc = new ExecutionStateService(_tempDir);
        // Migration: string → array
        Assert.Equal(["15:30"], svc.GetScheduleTimes());
    }

    [Fact]
    public void SetScheduleTime_CompatibilityMethod_UpdatesFirstTime()
    {
        var svc = new ExecutionStateService(defaultScheduleTimes: ["08:00", "13:00"]);
        svc.SetScheduleTime("10:00");
        Assert.Equal("10:00", svc.GetScheduleTime());
        Assert.Equal(["10:00"], svc.GetScheduleTimes());
    }

    [Fact]
    public void PersistSchedulingState_NewFormat_WritesScheduleTimesArray()
    {
        Directory.CreateDirectory(_tempDir);
        var svc = new ExecutionStateService(_tempDir, defaultScheduleTimes: ["08:00"]);
        svc.SetScheduleTimes(["09:00", "18:00"]);

        var file = Path.Combine(_tempDir, "scheduling-state.json");
        var content = File.ReadAllText(file);
        // Doit écrire scheduleTimes (array) pas scheduleTime (string)
        Assert.Contains("scheduleTimes", content);
        Assert.DoesNotContain("\"scheduleTime\":", content);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7.2 — CalculateNextRun multi-horaires
// ─────────────────────────────────────────────────────────────────────────────
public class SchedulerWorkerMultiScheduleTests
{
    private static SchedulerWorker CreateWorker(FakeTimeProvider fakeTime, FakeExecutionStateService state)
    {
        var sp = new ServiceCollection()
            .AddScoped<IPostingCycleService>(_ => new FakePostingCycle())
            .BuildServiceProvider();

        var opts = Options.Create(new PosterOptions { MaxRetryCount = 3 });
        return new SchedulerWorker(sp, state,
            new FakeResiliencePipeline(),
            new FakeNotificationService(),
            opts, fakeTime,
            NullLogger<SchedulerWorker>.Instance);
    }

    private class FakePostingCycle : IPostingCycleService
    {
        public Task<Bet2InvestPoster.Models.CycleResult> RunCycleAsync(CancellationToken ct = default)
            => Task.FromResult(new Bet2InvestPoster.Models.CycleResult());
        public Task<(IReadOnlyList<Bet2InvestPoster.Models.PendingBet> Bets, Bet2InvestPoster.Models.CycleResult PartialResult)> PrepareCycleAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private class FakeResiliencePipeline : IResiliencePipelineService
    {
        public async Task ExecuteCycleWithRetryAsync(Func<CancellationToken, Task> action, CancellationToken ct)
            => await action(ct);
        public Bet2InvestPoster.Models.CircuitBreakerState GetCircuitBreakerState()
            => Bet2InvestPoster.Models.CircuitBreakerState.Closed;
        public TimeSpan? GetCircuitBreakerRemainingDuration() => null;
    }

    [Fact]
    public void CalculateNextRun_ThreeTimes_ReturnsEarliestFuture_AllInFuture()
    {
        // Now = 07:00 — times: 08:00, 13:00, 19:00 — all in future → nearest = 08:00
        var now = new DateTimeOffset(2026, 2, 25, 7, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var state = new FakeExecutionStateService(["08:00", "13:00", "19:00"]);
        var worker = CreateWorker(fakeTime, state);

        var next = worker.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void CalculateNextRun_ThreeTimes_ReturnsEarliestFuture_FirstPassed()
    {
        // Now = 10:00 — times: 08:00 (passed), 13:00, 19:00 → nearest = 13:00 today
        var now = new DateTimeOffset(2026, 2, 25, 10, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var state = new FakeExecutionStateService(["08:00", "13:00", "19:00"]);
        var worker = CreateWorker(fakeTime, state);

        var next = worker.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 13, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void CalculateNextRun_ThreeTimes_ReturnsEarliestFuture_TwoPassed()
    {
        // Now = 15:00 — times: 08:00, 13:00 (passed), 19:00 → nearest = 19:00 today
        var now = new DateTimeOffset(2026, 2, 25, 15, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var state = new FakeExecutionStateService(["08:00", "13:00", "19:00"]);
        var worker = CreateWorker(fakeTime, state);

        var next = worker.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 19, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void CalculateNextRun_ThreeTimes_AllPassed_WrapsToTomorrow()
    {
        // Now = 20:00 — all passed → earliest = 08:00 tomorrow
        var now = new DateTimeOffset(2026, 2, 25, 20, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var state = new FakeExecutionStateService(["08:00", "13:00", "19:00"]);
        var worker = CreateWorker(fakeTime, state);

        var next = worker.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 26, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void CalculateNextRun_SingleTime_BehavesLikeBeforeRefactoring()
    {
        // Now = 07:59 — single time 08:00 → today 08:00
        var now = new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);
        var state = new FakeExecutionStateService(["08:00"]);
        var worker = CreateWorker(fakeTime, state);

        var next = worker.CalculateNextRun();
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void CalculateNextRun_SequentialExecution_08_13_19_Demain08()
    {
        // Simule la séquence complète des 3 runs de la journée + wrap to demain
        var state = new FakeExecutionStateService(["08:00", "13:00", "19:00"]);

        // Avant 08:00
        var t1 = new FakeTimeProvider(new DateTimeOffset(2026, 2, 25, 7, 59, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero), CreateWorker(t1, state).CalculateNextRun());

        // Après 08:00, avant 13:00
        var t2 = new FakeTimeProvider(new DateTimeOffset(2026, 2, 25, 8, 1, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 13, 0, 0, TimeSpan.Zero), CreateWorker(t2, state).CalculateNextRun());

        // Après 13:00, avant 19:00
        var t3 = new FakeTimeProvider(new DateTimeOffset(2026, 2, 25, 13, 1, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 2, 25, 19, 0, 0, TimeSpan.Zero), CreateWorker(t3, state).CalculateNextRun());

        // Après 19:00 → demain 08:00
        var t4 = new FakeTimeProvider(new DateTimeOffset(2026, 2, 25, 19, 1, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 2, 26, 8, 0, 0, TimeSpan.Zero), CreateWorker(t4, state).CalculateNextRun());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7.3 — ScheduleCommandHandler multi-horaires
// ─────────────────────────────────────────────────────────────────────────────
public class ScheduleCommandHandlerMultiTests
{
    private static Message MakeMessage(string text) =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    [Fact]
    public async Task HandleAsync_MultipleValidTimes_UpdatesScheduleTimes()
    {
        var state = new FakeExecutionStateService();
        var handler = new ScheduleCommandHandler(state, NullLogger<ScheduleCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 08:00,13:00,19:00"), CancellationToken.None);

        Assert.Equal(["08:00", "13:00", "19:00"], state.GetScheduleTimes());
        Assert.Contains("08:00", bot.SentMessages[0]);
        Assert.Contains("13:00", bot.SentMessages[0]);
        Assert.Contains("19:00", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_InvalidTimeInList_RejectsEntireCommand()
    {
        var state = new FakeExecutionStateService();
        var handler = new ScheduleCommandHandler(state, NullLogger<ScheduleCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 08:00,25:00,19:00"), CancellationToken.None);

        Assert.Contains("invalide", bot.SentMessages[0]);
        Assert.Contains("25:00", bot.SentMessages[0]);
        // État non modifié
        Assert.Equal(["08:00"], state.GetScheduleTimes());
    }

    [Fact]
    public async Task HandleAsync_NoArgument_ShowsAllCurrentTimes()
    {
        var state = new FakeExecutionStateService();
        state.SetScheduleTimes(["08:00", "13:00", "19:00"]);
        var handler = new ScheduleCommandHandler(state, NullLogger<ScheduleCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule"), CancellationToken.None);

        Assert.Contains("08:00", bot.SentMessages[0]);
        Assert.Contains("13:00", bot.SentMessages[0]);
        Assert.Contains("19:00", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_DuplicateTimes_Deduplicates()
    {
        var state = new FakeExecutionStateService();
        var handler = new ScheduleCommandHandler(state, NullLogger<ScheduleCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 08:00,08:00,13:00"), CancellationToken.None);

        Assert.Equal(2, state.GetScheduleTimes().Length);
        Assert.Contains("08:00", state.GetScheduleTimes());
        Assert.Contains("13:00", state.GetScheduleTimes());
    }

    [Fact]
    public async Task HandleAsync_UnsortedTimes_SortsResult()
    {
        var state = new FakeExecutionStateService();
        var handler = new ScheduleCommandHandler(state, NullLogger<ScheduleCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 19:00,08:00,13:00"), CancellationToken.None);

        Assert.Equal(["08:00", "13:00", "19:00"], state.GetScheduleTimes());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FormatStatus + FormatOnboardingMessage multi-horaires
// ─────────────────────────────────────────────────────────────────────────────
public class MessageFormatterMultiScheduleTests
{
    private readonly MessageFormatter _formatter = new();

    [Fact]
    public void FormatStatus_ShowsAllConfiguredTimes()
    {
        var state = new Bet2InvestPoster.Services.ExecutionState(null, null, null, null, null,
            ScheduleTimes: ["08:00", "13:00", "19:00"]);

        var result = _formatter.FormatStatus(state);

        Assert.Contains("08:00", result);
        Assert.Contains("13:00", result);
        Assert.Contains("19:00", result);
        Assert.Contains("Horaires", result);
    }

    [Fact]
    public void FormatOnboardingMessage_ShowsAllScheduleTimes()
    {
        var result = _formatter.FormatOnboardingMessage(true, 2, ["08:00", "13:00", "19:00"]);

        Assert.Contains("08:00", result);
        Assert.Contains("13:00", result);
        Assert.Contains("19:00", result);
    }
}
