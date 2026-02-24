using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class RunCommandHandlerTests
{
    // --- Fake cycle service ---

    private class FakePostingCycleService : IPostingCycleService
    {
        public bool WasCalled { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task RunCycleAsync(CancellationToken ct = default)
        {
            WasCalled = true;
            if (ShouldThrow)
                throw new InvalidOperationException("API indisponible");
            return Task.CompletedTask;
        }
    }

    // Pass-through: executes once, no retry (for existing tests unrelated to Polly behavior)
    private class FakeResiliencePipelineService : IResiliencePipelineService
    {
        public async Task ExecuteCycleWithRetryAsync(Func<CancellationToken, Task> cycleAction, CancellationToken ct = default)
            => await cycleAction(ct);
    }

    // --- Helpers ---

    private static (RunCommandHandler handler, FakeTelegramBotClient bot, FakePostingCycleService cycleService)
        CreateHandler(bool shouldThrow = false)
    {
        var cycleService = new FakePostingCycleService { ShouldThrow = shouldThrow };
        var stateService = new ExecutionStateService();
        var services = new ServiceCollection();
        services.AddSingleton<IPostingCycleService>(cycleService);
        var sp = services.BuildServiceProvider();

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new PosterOptions { MaxRetryCount = 3 });
        var handler = new RunCommandHandler(
            scopeFactory,
            new FakeResiliencePipelineService(),
            stateService,
            options,
            NullLogger<RunCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();
        return (handler, bot, cycleService);
    }

    private static RunCommandHandler CreateMinimalHandler()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new PosterOptions { MaxRetryCount = 3 });
        return new RunCommandHandler(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new FakeResiliencePipelineService(),
            new ExecutionStateService(),
            options,
            NullLogger<RunCommandHandler>.Instance);
    }

    private static Message MakeMessage(string text = "/run") =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    // --- Tests ---

    [Fact]
    public void CanHandle_Run_ReturnsTrue()
    {
        Assert.True(CreateMinimalHandler().CanHandle("/run"));
    }

    [Fact]
    public void CanHandle_Status_ReturnsFalse()
    {
        Assert.False(CreateMinimalHandler().CanHandle("/status"));
    }

    [Fact]
    public async Task HandleAsync_Success_CallsCycleServiceAndSendsSuccessMessage()
    {
        var (handler, bot, cycleService) = CreateHandler(shouldThrow: false);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.True(cycleService.WasCalled);
        Assert.Single(bot.SentMessages);
        Assert.Contains("✅", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_Failure_SendsErrorMessage()
    {
        var (handler, bot, _) = CreateHandler(shouldThrow: true);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌", bot.SentMessages[0]);
        // Error message uses exception type name (no credential leak)
        Assert.Contains("InvalidOperationException", bot.SentMessages[0]);
        Assert.DoesNotContain("API indisponible", bot.SentMessages[0]);
    }

    // --- Polly integration tests (M3 — code review 5.2) ---

    private class RetryCountingCycleService : IPostingCycleService
    {
        public int CallCount { get; private set; }
        public int FailCount { get; set; } = int.MaxValue;

        public Task RunCycleAsync(CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount <= FailCount)
                throw new InvalidOperationException($"Simulated failure #{CallCount}");
            return Task.CompletedTask;
        }
    }

    private static (RunCommandHandler handler, FakeTelegramBotClient bot, RetryCountingCycleService cycleService)
        CreateHandlerWithRealPolly(int failCount, int maxRetryCount = 3)
    {
        var cycleService = new RetryCountingCycleService { FailCount = failCount };
        var stateService = new ExecutionStateService();
        var services = new ServiceCollection();
        services.AddScoped<IPostingCycleService>(_ => cycleService);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new PosterOptions { MaxRetryCount = maxRetryCount, RetryDelayMs = 0 });
        var resilience = new ResiliencePipelineService(options, NullLogger<ResiliencePipelineService>.Instance);
        var handler = new RunCommandHandler(
            sp.GetRequiredService<IServiceScopeFactory>(),
            resilience,
            stateService,
            options,
            NullLogger<RunCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();
        return (handler, bot, cycleService);
    }

    [Fact]
    public async Task HandleAsync_FailsThenSucceeds_RetriesAndSendsSuccess()
    {
        // Fails once, succeeds on 2nd attempt
        var (handler, bot, cycleService) = CreateHandlerWithRealPolly(failCount: 1, maxRetryCount: 3);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Equal(2, cycleService.CallCount);
        Assert.Single(bot.SentMessages);
        Assert.Contains("✅", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_AllRetriesExhausted_SendsErrorWithRetryCount()
    {
        // All 3 attempts fail
        var (handler, bot, cycleService) = CreateHandlerWithRealPolly(failCount: int.MaxValue, maxRetryCount: 3);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Equal(3, cycleService.CallCount);
        Assert.Single(bot.SentMessages);
        Assert.Contains("❌", bot.SentMessages[0]);
        Assert.Contains("3 tentatives", bot.SentMessages[0]);
    }
}
