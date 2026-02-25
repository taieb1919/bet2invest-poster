using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class TipstersCommandHandlerTests
{
    // --- Fake ITipsterService ---

    private class FakeTipsterService : ITipsterService
    {
        private readonly List<TipsterConfig> _tipsters;

        public bool AddThrowsDuplicate { get; set; }
        public bool AddThrowsArgument { get; set; }
        public TipsterConfig? AddedResult { get; set; }
        public bool RemoveResult { get; set; } = true;
        public List<TipsterConfig>? ReplacedWith { get; private set; }

        public FakeTipsterService(List<TipsterConfig>? tipsters = null)
        {
            _tipsters = tipsters ?? [];
        }

        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default) =>
            Task.FromResult(_tipsters);

        public Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default)
        {
            if (AddThrowsDuplicate)
                throw new InvalidOperationException("Déjà dans la liste");
            if (AddThrowsArgument)
                throw new ArgumentException("URL invalide");
            var config = AddedResult ?? new TipsterConfig { Url = url, Name = "slug" };
            config.TryExtractSlug(out _);
            return Task.FromResult(config);
        }

        public Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default) =>
            Task.FromResult(RemoveResult);

        public Task ReplaceTipstersAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
        {
            ReplacedWith = tipsters;
            return Task.CompletedTask;
        }
    }

    private class ThrowingFakeTipsterService : ITipsterService
    {
        public Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default) =>
            throw new IOException("Fichier tipsters.json corrompu");

        public Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default) =>
            throw new IOException("Erreur IO");

        public Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default) =>
            throw new IOException("Erreur IO");

        public Task ReplaceTipstersAsync(List<TipsterConfig> tipsters, CancellationToken ct = default) =>
            throw new IOException("Erreur IO");
    }

    // --- Fake IExtendedBet2InvestClient ---

    private class FakeExtendedClient : IExtendedBet2InvestClient
    {
        public bool IsAuthenticated => true;
        public List<ScrapedTipster> TipstersToReturn { get; set; } = [];
        public bool ThrowOnScrape { get; set; }
        public string ThrowMessage { get; set; } = "Erreur API";

        public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(int tipsterNumericId, CancellationToken ct = default)
            => Task.FromResult((true, new List<PendingBet>()));
        public Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<List<ScrapedTipster>> GetFreeTipstersAsync(CancellationToken ct = default)
        {
            if (ThrowOnScrape) throw new Exception(ThrowMessage);
            return Task.FromResult(TipstersToReturn);
        }
    }

    // --- Fake IConversationStateService ---

    private class FakeConversationStateService : IConversationStateService
    {
        public long RegisteredChatId { get; private set; }
        public Func<ITelegramBotClient, string, CancellationToken, Task>? RegisteredCallback { get; private set; }

        public void Register(long chatId, Func<ITelegramBotClient, string, CancellationToken, Task> callback, TimeSpan? timeout = null)
        {
            RegisteredChatId = chatId;
            RegisteredCallback = callback;
        }

        public bool TryGet(long chatId, out Func<ITelegramBotClient, string, CancellationToken, Task>? callback)
        {
            callback = null;
            return false;
        }

        public void Clear(long chatId) { }
    }

    // --- Helpers ---

    private static Message MakeMessage(string text = "/tipsters") =>
        new() { Text = text, Chat = new Chat { Id = 99 } };

    private static TipstersCommandHandler CreateHandler(
        List<TipsterConfig>? tipsters = null,
        FakeExtendedClient? extClient = null,
        FakeConversationStateService? convState = null)
    {
        var fakeTs = new FakeTipsterService(tipsters);
        var fakeClient = extClient ?? new FakeExtendedClient();
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => fakeTs);
        services.AddScoped<IExtendedBet2InvestClient>(_ => fakeClient);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new TipstersCommandHandler(
            scopeFactory,
            new MessageFormatter(),
            convState ?? new FakeConversationStateService(),
            NullLogger<TipstersCommandHandler>.Instance);
    }

    private static (TipstersCommandHandler handler, FakeTipsterService fake, FakeConversationStateService convState)
        CreateHandlerWithFake(FakeTipsterService? fake = null, FakeExtendedClient? extClient = null)
    {
        var fakeTs = fake ?? new FakeTipsterService();
        var fakeClient = extClient ?? new FakeExtendedClient();
        var convState = new FakeConversationStateService();
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => fakeTs);
        services.AddScoped<IExtendedBet2InvestClient>(_ => fakeClient);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var handler = new TipstersCommandHandler(
            scopeFactory,
            new MessageFormatter(),
            convState,
            NullLogger<TipstersCommandHandler>.Instance);

        return (handler, fakeTs, convState);
    }

    private static TipstersCommandHandler CreateThrowingHandler()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => new ThrowingFakeTipsterService());
        services.AddScoped<IExtendedBet2InvestClient>(_ => new FakeExtendedClient { ThrowOnScrape = true });
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new TipstersCommandHandler(
            scopeFactory,
            new MessageFormatter(),
            new FakeConversationStateService(),
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

    [Fact]
    public void CanHandle_OtherCommand_ReturnsFalse()
    {
        Assert.False(CreateHandler().CanHandle("/status"));
        Assert.False(CreateHandler().CanHandle("/history"));
        Assert.False(CreateHandler().CanHandle("/start"));
    }

    // --- Tests HandleAsync liste ---

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
    public async Task HandleAsync_AddUnknownSubcommand_SendsUsageMessage()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters unknown"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Usage", bot.SentMessages[0]);
    }

    // --- Tests sous-commandes add / remove ---

    [Fact]
    public async Task HandleAsync_AddValid_SendsConfirmationWithSlug()
    {
        var fake = new FakeTipsterService
        {
            AddedResult = new TipsterConfig
            {
                Url = "https://bet2invest.com/tipsters/performance-stats/johndoe",
                Name = "johndoe"
            }
        };
        var (handler, _, _) = CreateHandlerWithFake(fake);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters add https://bet2invest.com/tipsters/performance-stats/johndoe"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("✅ Tipster ajouté", bot.SentMessages[0]);
        Assert.Contains("johndoe", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_AddDuplicate_SendsDuplicateMessage()
    {
        var fake = new FakeTipsterService { AddThrowsDuplicate = true };
        var (handler, _, _) = CreateHandlerWithFake(fake);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters add https://bet2invest.com/tipsters/performance-stats/johndoe"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("déjà dans la liste", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_AddInvalidUrl_SendsInvalidUrlMessage()
    {
        var fake = new FakeTipsterService { AddThrowsArgument = true };
        var (handler, _, _) = CreateHandlerWithFake(fake);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters add not-a-valid-url"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("URL invalide", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_AddWithoutArgument_SendsUsageMessage()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters add"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Usage : /tipsters add", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_RemoveValid_SendsConfirmationWithSlug()
    {
        var fake = new FakeTipsterService { RemoveResult = true };
        var (handler, _, _) = CreateHandlerWithFake(fake);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters remove https://bet2invest.com/tipsters/performance-stats/johndoe"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("🗑️ Tipster retiré", bot.SentMessages[0]);
        Assert.Contains("johndoe", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_RemoveNotFound_SendsNotFoundMessage()
    {
        var fake = new FakeTipsterService { RemoveResult = false };
        var (handler, _, _) = CreateHandlerWithFake(fake);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters remove https://bet2invest.com/tipsters/performance-stats/unknown"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("non trouvé", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_RemoveWithoutArgument_SendsUsageMessage()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters remove"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Usage : /tipsters remove", bot.SentMessages[0]);
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

    [Fact]
    public async Task HandleAsync_AddWhenServiceThrowsUnexpected_SendsGenericErrorMessage()
    {
        var handler = CreateThrowingHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters add https://bet2invest.com/tipsters/performance-stats/x"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌ Erreur", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_RemoveWhenServiceThrowsUnexpected_SendsGenericErrorMessage()
    {
        var handler = CreateThrowingHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot,
            MakeMessage("/tipsters remove https://bet2invest.com/tipsters/performance-stats/x"),
            CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌ Erreur", bot.SentMessages[0]);
    }

    // --- Tests /tipsters update (Story 11.1, Task 7.2) ---

    [Fact]
    public async Task HandleAsync_Update_ScrapingSucceeds_SendsListAndRegistersConversation()
    {
        var scraped = new List<ScrapedTipster>
        {
            new() { Username = "tipster1", Roi = 12.5m, BetsNumber = 100, MostBetSport = "Football" },
            new() { Username = "tipster2", Roi = 8.0m, BetsNumber = 50, MostBetSport = "Tennis" }
        };
        var extClient = new FakeExtendedClient { TipstersToReturn = scraped };
        var (handler, _, convState) = CreateHandlerWithFake(extClient: extClient);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);

        // Doit envoyer 2 messages : liste + question confirmation
        Assert.Equal(2, bot.SentMessages.Count);
        Assert.Contains("tipster1", bot.SentMessages[0]);
        Assert.Contains("Oui", bot.SentMessages[1]);
        // État de conversation enregistré
        Assert.Equal(99, convState.RegisteredChatId);
        Assert.NotNull(convState.RegisteredCallback);
    }

    [Fact]
    public async Task HandleAsync_Update_ScrapingFails_SendsErrorMessage()
    {
        var extClient = new FakeExtendedClient { ThrowOnScrape = true, ThrowMessage = "Timeout réseau" };
        var (handler, _, _) = CreateHandlerWithFake(extClient: extClient);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌ Impossible de récupérer", bot.SentMessages[0]);
        Assert.Contains("Timeout réseau", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_Update_ScrapingReturnsEmpty_SendsEmptyMessage()
    {
        var extClient = new FakeExtendedClient { TipstersToReturn = [] };
        var (handler, _, _) = CreateHandlerWithFake(extClient: extClient);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Aucun tipster gratuit", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_Update_ConfirmOui_ReplacesTipsters()
    {
        var scraped = new List<ScrapedTipster>
        {
            new() { Username = "newTipster", Roi = 15m, BetsNumber = 200, MostBetSport = "Basketball" }
        };
        var extClient = new FakeExtendedClient { TipstersToReturn = scraped };
        var fakeTs = new FakeTipsterService();
        var convState = new FakeConversationStateService();
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => fakeTs);
        services.AddScoped<IExtendedBet2InvestClient>(_ => extClient);
        var provider = services.BuildServiceProvider();
        var handler = new TipstersCommandHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageFormatter(),
            convState,
            NullLogger<TipstersCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        // Déclencher la mise à jour
        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);

        // Simuler la réponse "Oui"
        Assert.NotNull(convState.RegisteredCallback);
        bot.SentMessages.Clear();
        await convState.RegisteredCallback!(bot, "Oui", CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("✅ Liste mise à jour", bot.SentMessages[0]);
        Assert.Contains("1 tipster.", bot.SentMessages[0]);
        Assert.NotNull(fakeTs.ReplacedWith);
        Assert.Single(fakeTs.ReplacedWith!);
        Assert.Contains("newTipster", fakeTs.ReplacedWith[0].Url);
    }

    [Fact]
    public async Task HandleAsync_Update_ConfirmNon_SendsCancelMessage()
    {
        var scraped = new List<ScrapedTipster>
        {
            new() { Username = "t1", Roi = 5m, BetsNumber = 10, MostBetSport = "Football" }
        };
        var extClient = new FakeExtendedClient { TipstersToReturn = scraped };
        var fakeTs = new FakeTipsterService();
        var convState = new FakeConversationStateService();
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => fakeTs);
        services.AddScoped<IExtendedBet2InvestClient>(_ => extClient);
        var provider = services.BuildServiceProvider();
        var handler = new TipstersCommandHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageFormatter(),
            convState,
            NullLogger<TipstersCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);
        bot.SentMessages.Clear();

        await convState.RegisteredCallback!(bot, "Non", CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("❌ Mise à jour annulée", bot.SentMessages[0]);
        Assert.Null(fakeTs.ReplacedWith); // Pas de remplacement
    }

    [Fact]
    public async Task HandleAsync_Update_ConfirmFusionner_MergesWithoutDuplicates()
    {
        var existing = new List<TipsterConfig>
        {
            new() { Url = "https://bet2invest.com/tipsters/performance-stats/existing1", Name = "existing1" }
        };
        var scraped = new List<ScrapedTipster>
        {
            new() { Username = "existing1", Roi = 5m, BetsNumber = 10, MostBetSport = "Football" }, // doublon
            new() { Username = "newone", Roi = 10m, BetsNumber = 20, MostBetSport = "Tennis" }       // nouveau
        };
        var extClient = new FakeExtendedClient { TipstersToReturn = scraped };
        var fakeTs = new FakeTipsterService(existing);
        var convState = new FakeConversationStateService();
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => fakeTs);
        services.AddScoped<IExtendedBet2InvestClient>(_ => extClient);
        var provider = services.BuildServiceProvider();
        var handler = new TipstersCommandHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageFormatter(),
            convState,
            NullLogger<TipstersCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);
        bot.SentMessages.Clear();

        await convState.RegisteredCallback!(bot, "Fusionner", CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("✅ 1 tipster ajouté. Total : 2.", bot.SentMessages[0]);
        Assert.NotNull(fakeTs.ReplacedWith);
        Assert.Equal(2, fakeTs.ReplacedWith!.Count);
    }

    [Fact]
    public async Task HandleAsync_Update_UnknownReply_SendsWarningAndReregisters()
    {
        var scraped = new List<ScrapedTipster>
        {
            new() { Username = "t1", Roi = 5m, BetsNumber = 10, MostBetSport = "Football" }
        };
        var extClient = new FakeExtendedClient { TipstersToReturn = scraped };
        var fakeTs = new FakeTipsterService();
        var convState = new FakeConversationStateService();
        var services = new ServiceCollection();
        services.AddScoped<ITipsterService>(_ => fakeTs);
        services.AddScoped<IExtendedBet2InvestClient>(_ => extClient);
        var provider = services.BuildServiceProvider();
        var handler = new TipstersCommandHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MessageFormatter(),
            convState,
            NullLogger<TipstersCommandHandler>.Instance);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/tipsters update"), CancellationToken.None);
        var initialCallback = convState.RegisteredCallback;
        bot.SentMessages.Clear();

        await initialCallback!(bot, "peut-être", CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("⚠️ Réponse non reconnue", bot.SentMessages[0]);
        Assert.NotNull(convState.RegisteredCallback); // Ré-enregistré pour un nouvel essai
        Assert.Null(fakeTs.ReplacedWith);
    }
}
