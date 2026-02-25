using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class ScheduleCommandHandlerTests
{
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

        Assert.Equal("10:30", stateService.GetScheduleTime());
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

        Assert.Null(stateService.NextRunSet);
    }

    [Fact]
    public async Task HandleAsync_InvalidFormat_ReturnsError()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 25:99"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("invalide", bot.SentMessages[0]);
        Assert.Equal("08:00", stateService.GetScheduleTime()); // non modifié
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

        Assert.Equal("00:00", stateService.GetScheduleTime());
        Assert.Single(bot.SentMessages);
        Assert.DoesNotContain("invalide", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_EndOfDayEdgeCase_AcceptsAndUpdates()
    {
        var (handler, stateService) = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/schedule 23:59"), CancellationToken.None);

        Assert.Equal("23:59", stateService.GetScheduleTime());
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
