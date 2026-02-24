using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class StartCommandHandlerTests
{
    // ─── Fake ────────────────────────────────────────────────────────────────

    private class FakeExecutionStateService : IExecutionStateService
    {
        private bool _schedulingEnabled;
        private readonly DateTimeOffset? _nextRunAt;

        public FakeExecutionStateService(bool schedulingEnabled = true, DateTimeOffset? nextRunAt = null)
        {
            _schedulingEnabled = schedulingEnabled;
            _nextRunAt = nextRunAt;
        }

        public bool SetSchedulingEnabledCalled { get; private set; }
        public bool? LastSetValue { get; private set; }

        public ExecutionState GetState() => new(null, null, null, _nextRunAt, null);
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
        public string GetScheduleTime() => "08:00";
        public void SetScheduleTime(string time) { }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Message MakeMessage(string text = "/start") =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    private static (StartCommandHandler handler, FakeTelegramBotClient bot, FakeExecutionStateService state)
        CreateHandler(bool schedulingEnabled = true, DateTimeOffset? nextRunAt = null)
    {
        var state = new FakeExecutionStateService(schedulingEnabled, nextRunAt);
        var handler = new StartCommandHandler(state, NullLogger<StartCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();
        return (handler, bot, state);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_Start_ReturnsTrue()
    {
        var (handler, _, _) = CreateHandler();
        Assert.True(handler.CanHandle("/start"));
    }

    [Fact]
    public void CanHandle_Stop_ReturnsFalse()
    {
        var (handler, _, _) = CreateHandler();
        Assert.False(handler.CanHandle("/stop"));
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyActive_SendsAlreadyActiveMessage()
    {
        var (handler, bot, state) = CreateHandler(schedulingEnabled: true);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("déjà actif", bot.SentMessages[0]);
        Assert.Contains("ℹ️", bot.SentMessages[0]);
        Assert.False(state.SetSchedulingEnabledCalled);
    }

    [Fact]
    public async Task HandleAsync_WhenSuspended_EnablesSchedulingAndSendsActivatedMessage()
    {
        var (handler, bot, state) = CreateHandler(schedulingEnabled: false);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.True(state.SetSchedulingEnabledCalled);
        Assert.True(state.LastSetValue);
        Assert.Single(bot.SentMessages);
        Assert.Contains("▶️", bot.SentMessages[0]);
        Assert.Contains("activé", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenSuspended_WithNextRun_IncludesNextRunInMessage()
    {
        var nextRun = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var (handler, bot, _) = CreateHandler(schedulingEnabled: false, nextRunAt: nextRun);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Contains("2026-03-01", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenSuspended_WithoutNextRun_ShowsNonPlanifie()
    {
        var (handler, bot, _) = CreateHandler(schedulingEnabled: false, nextRunAt: null);

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Contains("non planifié", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_SendsMessageToCorrectChatId()
    {
        var state = new FakeExecutionStateService(schedulingEnabled: false);
        var handler = new StartCommandHandler(state, NullLogger<StartCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();
        var message = new Message { Text = "/start", Chat = new Chat { Id = 99999 } };

        await handler.HandleAsync(bot, message, CancellationToken.None);

        Assert.Single(bot.SentChatIds);
        Assert.Equal(99999L, bot.SentChatIds[0]);
    }
}
