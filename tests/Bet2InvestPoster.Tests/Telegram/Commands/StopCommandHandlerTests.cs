using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class StopCommandHandlerTests
{
    // ─── Fake ────────────────────────────────────────────────────────────────

    private class FakeExecutionStateService : IExecutionStateService
    {
        private bool _schedulingEnabled = true;

        public bool SetSchedulingEnabledCalled { get; set; }
        public bool? LastSetValue { get; private set; }

        public ExecutionState GetState() => new(null, null, null, null, null);
        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) { }
        public void SetApiConnectionStatus(bool connected) { }
        public bool GetSchedulingEnabled() => _schedulingEnabled;
        public void SetSchedulingEnabled(bool enabled)
        {
            _schedulingEnabled = enabled;
            SetSchedulingEnabledCalled = true;
            LastSetValue = enabled;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Message MakeMessage(string text = "/stop") =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    private static (StopCommandHandler handler, FakeTelegramBotClient bot, FakeExecutionStateService state)
        CreateHandler()
    {
        var state = new FakeExecutionStateService();
        var handler = new StopCommandHandler(state, NullLogger<StopCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();
        return (handler, bot, state);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_Stop_ReturnsTrue()
    {
        var (handler, _, _) = CreateHandler();
        Assert.True(handler.CanHandle("/stop"));
    }

    [Fact]
    public void CanHandle_Start_ReturnsFalse()
    {
        var (handler, _, _) = CreateHandler();
        Assert.False(handler.CanHandle("/start"));
    }

    [Fact]
    public async Task HandleAsync_DisablesSchedulingAndSendsSuspendedMessage()
    {
        var (handler, bot, state) = CreateHandler();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.True(state.SetSchedulingEnabledCalled);
        Assert.False(state.LastSetValue);
        Assert.Single(bot.SentMessages);
        Assert.Contains("⏸", bot.SentMessages[0]);
        Assert.Contains("suspendu", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_MessageMentionsStartCommand()
    {
        var (handler, bot, _) = CreateHandler();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Contains("/start", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadySuspended_SendsIdempotentMessageWithoutDisabling()
    {
        // /stop appelé deux fois — doit répondre message idempotent sans rappeler SetSchedulingEnabled
        var state = new FakeExecutionStateService();
        state.SetSchedulingEnabled(false); // pré-suspendu
        state.SetSchedulingEnabledCalled = false; // reset après pré-configuration
        var handler = new StopCommandHandler(state, NullLogger<StopCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.False(state.SetSchedulingEnabledCalled);
        Assert.Single(bot.SentMessages);
        Assert.Contains("déjà suspendu", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_SendsMessageToCorrectChatId()
    {
        var (handler, bot, _) = CreateHandler();
        var message = new Message { Text = "/stop", Chat = new Chat { Id = 77777 } };

        await handler.HandleAsync(bot, message, CancellationToken.None);

        Assert.Single(bot.SentChatIds);
        Assert.Equal(77777L, bot.SentChatIds[0]);
    }
}
