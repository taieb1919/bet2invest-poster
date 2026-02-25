using Bet2InvestPoster.Telegram.Commands;
using global::Telegram.Bot.Types;
using global::Telegram.Bot.Types.Enums;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class HelpCommandHandlerTests
{
    private static Message MakeMessage(string text = "/help") =>
        new() { Text = text, Chat = new Chat { Id = 42 } };

    private static (HelpCommandHandler handler, FakeTelegramBotClient bot) CreateHandler()
    {
        var handler = new HelpCommandHandler();
        var bot = new FakeTelegramBotClient();
        return (handler, bot);
    }

    [Fact]
    public void CanHandle_Help_ReturnsTrue()
    {
        var (handler, _) = CreateHandler();
        Assert.True(handler.CanHandle("/help"));
    }

    [Fact]
    public void CanHandle_Run_ReturnsFalse()
    {
        var (handler, _) = CreateHandler();
        Assert.False(handler.CanHandle("/run"));
    }

    [Fact]
    public void CanHandle_OtherCommand_ReturnsFalse()
    {
        var (handler, _) = CreateHandler();
        Assert.False(handler.CanHandle("/status"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bonjour")]
    [InlineData("/run")]
    public void CanHandle_NonHelpInput_ReturnsFalse(string input)
    {
        var (handler, _) = CreateHandler();
        Assert.False(handler.CanHandle(input));
    }

    [Theory]
    [InlineData("/run")]
    [InlineData("/status")]
    [InlineData("/start")]
    [InlineData("/stop")]
    [InlineData("/history")]
    [InlineData("/schedule")]
    [InlineData("/tipsters")]
    [InlineData("/report")]
    [InlineData("/help")]
    public async Task HandleAsync_MessageContainsAllKnownCommands(string expectedCommand)
    {
        var (handler, bot) = CreateHandler();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains(expectedCommand, bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_UsesHtmlParseMode()
    {
        var (handler, bot) = CreateHandler();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentParseModes);
        Assert.Equal(ParseMode.Html, bot.SentParseModes[0]);
    }

    [Fact]
    public async Task HandleAsync_SendsMessageToCorrectChatId()
    {
        var (handler, bot) = CreateHandler();
        var message = new Message { Text = "/help", Chat = new Chat { Id = 99999 } };

        await handler.HandleAsync(bot, message, CancellationToken.None);

        Assert.Single(bot.SentChatIds);
        Assert.Equal(99999L, bot.SentChatIds[0]);
    }
}
