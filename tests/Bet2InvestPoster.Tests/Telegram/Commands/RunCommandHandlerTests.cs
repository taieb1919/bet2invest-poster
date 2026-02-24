using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    // --- Helpers ---

    private static (RunCommandHandler handler, FakeTelegramBotClient bot, FakePostingCycleService cycleService, ExecutionStateService stateService)
        CreateHandler(bool shouldThrow = false)
    {
        var cycleService = new FakePostingCycleService { ShouldThrow = shouldThrow };
        var stateService = new ExecutionStateService();
        var services = new ServiceCollection();
        services.AddSingleton<IPostingCycleService>(cycleService);
        var sp = services.BuildServiceProvider();

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var handler = new RunCommandHandler(scopeFactory, stateService, NullLogger<RunCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();
        return (handler, bot, cycleService, stateService);
    }

    private static RunCommandHandler CreateMinimalHandler()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new RunCommandHandler(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new ExecutionStateService(),
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
        var (handler, bot, cycleService, stateService) = CreateHandler(shouldThrow: false);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.True(cycleService.WasCalled);
        Assert.Single(bot.SentMessages);
        Assert.Contains("✅", bot.SentMessages[0]);
        // H2 fix: state service should be updated
        var state = stateService.GetState();
        Assert.True(state.LastRunSuccess);
    }

    [Fact]
    public async Task HandleAsync_Failure_SendsErrorMessage()
    {
        var (handler, bot, _, stateService) = CreateHandler(shouldThrow: true);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌", bot.SentMessages[0]);
        // M3 fix: error message uses exception type name, not ex.Message (no credential leak)
        Assert.Contains("InvalidOperationException", bot.SentMessages[0]);
        Assert.DoesNotContain("API indisponible", bot.SentMessages[0]);
        // H2 fix: state service should record failure
        var state = stateService.GetState();
        Assert.False(state.LastRunSuccess);
    }
}
