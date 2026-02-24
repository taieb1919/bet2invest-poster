using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class ScheduleCommandHandlerTests
{
    private class FakeExecutionStateService : IExecutionStateService
    {
        private string _scheduleTime = "08:00";
        private bool _schedulingEnabled = true;
        private DateTimeOffset? _nextRunAt;

        public string LastSetTime => _scheduleTime;
        public DateTimeOffset? LastSetNextRun => _nextRunAt;

        public ExecutionState GetState() =>
            new(null, null, null, _nextRunAt, null, _schedulingEnabled, _scheduleTime);

        public void RecordSuccess(int publishedCount) { }
        public void RecordFailure(string reason) { }
        public void SetNextRun(DateTimeOffset nextRunAt) => _nextRunAt = nextRunAt;
        public void SetApiConnectionStatus(bool connected) { }
        public bool GetSchedulingEnabled() => _schedulingEnabled;
        public void SetSchedulingEnabled(bool enabled) => _schedulingEnabled = enabled;
        public string GetScheduleTime() => _scheduleTime;
        public void SetScheduleTime(string time) => _scheduleTime = time;
    }

    private static Message MakeMessage(string text) =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    private static (ScheduleCommandHandler handler, FakeExecutionStateService stateService) CreateHandler()
    {
        var stateService = new FakeExecutionStateService();
        var handler = new ScheduleCommandHandler(
            stateService,
            NullLogger<ScheduleCommandHandler>.Instance);
        return (handler, stateService);
    }

    [Fact]
    public void CanHandle_Schedule_ReturnsTrue()
    {
        var (handler, _) = CreateHandler();
        Assert.True(handler.CanHandle("/schedule"));
    }

    [Fact]
    public void CanHandle_OtherCommand_ReturnsFalse()
    {
        var (handler, _) = CreateHandler();
        Assert.False(handler.CanHandle("/status"));
    }

    [Fact]
    public async Task HandleAsync_NoArgument_ReturnsCurrentTimeAndUsage()
    {
        var (handler, _) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("08:00", bot.SentMessages[0]);
        Assert.Contains("Usage", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_ValidTime_UpdatesScheduleAndConfirms()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 10:30"), CancellationToken.None);

        Assert.Equal("10:30", stateService.LastSetTime);
        Assert.Single(bot.SentMessages);
        Assert.Contains("10:30", bot.SentMessages[0]);
        Assert.Contains("Prochain run", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_ValidTime_DoesNotSetNextRunDirectly()
    {
        // Le calcul du NextRun est délégué au SchedulerWorker — le handler ne l'appelle plus directement
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 10:30"), CancellationToken.None);

        Assert.Null(stateService.LastSetNextRun);
    }

    [Fact]
    public async Task HandleAsync_InvalidFormat_ReturnsError()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 25:99"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("invalide", bot.SentMessages[0]);
        Assert.Equal("08:00", stateService.LastSetTime); // non modifié
    }

    [Fact]
    public async Task HandleAsync_InvalidText_ReturnsError()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule abc"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("invalide", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_MidnightEdgeCase_AcceptsAndUpdates()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 00:00"), CancellationToken.None);

        Assert.Equal("00:00", stateService.LastSetTime);
        Assert.Single(bot.SentMessages);
        Assert.DoesNotContain("invalide", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_EndOfDayEdgeCase_AcceptsAndUpdates()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 23:59"), CancellationToken.None);

        Assert.Equal("23:59", stateService.LastSetTime);
    }

    [Fact]
    public async Task HandleAsync_ConfirmationContainsClockEmoji()
    {
        var (handler, _) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 14:00"), CancellationToken.None);

        Assert.Contains("⏰", bot.SentMessages[0]);
    }
}
