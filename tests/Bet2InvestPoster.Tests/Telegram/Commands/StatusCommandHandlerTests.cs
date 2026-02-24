using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class StatusCommandHandlerTests
{
    // --- Fake state service ---

    private class FakeExecutionStateService : IExecutionStateService
    {
        private readonly ExecutionState _state;

        public FakeExecutionStateService(ExecutionState? state = null)
        {
            _state = state ?? new ExecutionState(null, null, null, null, null);
        }

        public ExecutionState GetState() => _state;
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) { }
        public void SetApiConnectionStatus(bool connected) { }
    }

    private static Message MakeMessage(string text = "/status") =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    private static StatusCommandHandler CreateHandler(ExecutionState? state = null)
    {
        return new StatusCommandHandler(
            new FakeExecutionStateService(state),
            new MessageFormatter(),
            NullLogger<StatusCommandHandler>.Instance);
    }

    // --- Tests ---

    [Fact]
    public void CanHandle_Status_ReturnsTrue()
    {
        Assert.True(CreateHandler().CanHandle("/status"));
    }

    [Fact]
    public void CanHandle_Run_ReturnsFalse()
    {
        Assert.False(CreateHandler().CanHandle("/run"));
    }

    [Fact]
    public async Task HandleAsync_NoHistory_SendsNoRunMessage()
    {
        var handler = CreateHandler(new ExecutionState(null, null, null, null, null));
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Aucune", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WithSuccessHistory_SendsSuccessMessage()
    {
        var handler = CreateHandler(
            new ExecutionState(DateTimeOffset.UtcNow, true, "5 pronostic(s) publiés", null, true));
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("✅ Succès", bot.SentMessages[0]);
    }
}
