using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class TipstersCommandHandlerTests
{
    // --- Fake ITipsterService ---

    private class FakeTipsterService : ITipsterService
    {
        private readonly List<TipsterConfig> _tipsters;

        public FakeTipsterService(List<TipsterConfig>? tipsters = null)
        {
            _tipsters = tipsters ?? [];
        }

        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default) =>
            Task.FromResult(_tipsters);
    }

    private class ThrowingFakeTipsterService : ITipsterService
    {
        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default) =>
            throw new IOException("Fichier tipsters.json corrompu");
    }

    // --- Helpers ---

    private static Message MakeMessage(string text = "/tipsters") =>
        new() { Text = text, Chat = new Chat { Id = 99 } };

    private static TipstersCommandHandler CreateHandler(List<TipsterConfig>? tipsters = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => new FakeTipsterService(tipsters));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new TipstersCommandHandler(
            scopeFactory,
            new MessageFormatter(),
            NullLogger<TipstersCommandHandler>.Instance);
    }

    private static TipstersCommandHandler CreateThrowingHandler()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => new ThrowingFakeTipsterService());
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new TipstersCommandHandler(
            scopeFactory,
            new MessageFormatter(),
            NullLogger<TipstersCommandHandler>.Instance);
    }

    private static TipsterConfig MakeTipster(string name, string url) =>
        new() { Name = name, Url = url };

    // --- Tests CanHandle ---

    [Fact]
    public void CanHandle_Tipsters_ReturnsTrue()
    {
        Assert.True(CreateHandler().CanHandle("/tipsters"));
    }

    // Note: TelegramBotService extrait uniquement "/tipsters" (premier token) avant d'appeler CanHandle.
    // CanHandle ne recevra donc jamais "/tipsters add". Ce test vérifie les cas "false" légitimes.
    [Fact]
    public void CanHandle_OtherCommand_ReturnsFalse()
    {
        Assert.False(CreateHandler().CanHandle("/status"));
        Assert.False(CreateHandler().CanHandle("/history"));
        Assert.False(CreateHandler().CanHandle("/start"));
    }

    // --- Tests HandleAsync ---

    [Fact]
    public async Task HandleAsync_EmptyList_SendsAucunTipsterMessage()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Aucun tipster", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WithTipsters_SendsFormattedList()
    {
        var tipsters = new List<TipsterConfig>
        {
            MakeTipster("NG1", "https://bet2invest.com/tipsters/performance-stats/NG1"),
            MakeTipster("Edge Analytics", "https://bet2invest.com/tipsters/performance-stats/Edge_Analytics")
        };

        var handler = CreateHandler(tipsters);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("NG1", bot.SentMessages[0]);
        Assert.Contains("Edge Analytics", bot.SentMessages[0]);
        Assert.Contains("📋 Tipsters configurés", bot.SentMessages[0]);
        Assert.Contains("Total : 2 tipsters", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WithArguments_SendsProchainementMessage()
    {
        // TelegramBotService passe le message complet — le handler détecte les arguments lui-même.
        // Les sous-commandes add/remove ne sont pas encore implémentées (story 8.2).
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters add https://example.com"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("prochainement", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_SendsToCorrectChatId()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();
        var msg = new Message { Text = "/tipsters", Chat = new Chat { Id = 5555 } };

        await handler.HandleAsync(bot, msg, CancellationToken.None);

        Assert.Single(bot.SentChatIds);
        Assert.Equal(5555, bot.SentChatIds[0]);
    }

    [Fact]
    public async Task HandleAsync_OneTipster_SendsSingularTotal()
    {
        var tipsters = new List<TipsterConfig>
        {
            MakeTipster("ProTips", "https://bet2invest.com/tipsters/performance-stats/ProTips")
        };

        var handler = CreateHandler(tipsters);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Total : 1 tipster", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WhenLoadTipstersThrows_SendsErrorMessage()
    {
        var handler = CreateThrowingHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌ Erreur", bot.SentMessages[0]);
    }
}
