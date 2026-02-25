using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using global::Telegram.Bot;
using global::Telegram.Bot.Args;
using global::Telegram.Bot.Exceptions;
using global::Telegram.Bot.Requests.Abstractions;
using global::Telegram.Bot.Types;
using global::Telegram.Bot.Types.Enums;

namespace Bet2InvestPoster.Tests.Telegram;

public class TelegramBotServiceSetCommandsTests
{
    // ─── Fake bot client capturant SetMyCommandsAsync ───────────────────

    private sealed class FakeSetCommandsBotClient : ITelegramBotClient
    {
        public List<BotCommand[]> CapturedCommands { get; } = [];
        public bool ThrowOnSetCommands { get; set; }

        public long BotId => 1;
        public bool LocalBotServer => false;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

#pragma warning disable CS0067
        public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest;
        public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;
#pragma warning restore CS0067

        public Task<TResponse> SendRequest<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request.GetType().Name == "SetMyCommandsRequest")
            {
                if (ThrowOnSetCommands)
                    throw new Exception("API Telegram indisponible");

                // Récupérer les commandes via la propriété Commands par réflexion
                var commandsProp = request.GetType().GetProperty("Commands");
                if (commandsProp?.GetValue(request) is IEnumerable<BotCommand> cmds)
                    CapturedCommands.Add(cmds.ToArray());
            }

            if (typeof(TResponse) == typeof(Message))
                return (Task<TResponse>)(object)Task.FromResult(new Message());

            return Task.FromResult(default(TResponse)!);
        }

        public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // ─── Fakes minimalistes ──────────────────────────────────────────────

    private sealed class FakeOnboardingService : IOnboardingService
    {
        public Task TrySendOnboardingAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeConversationStateService : IConversationStateService
    {
        public void Register(long chatId, Func<ITelegramBotClient, string, CancellationToken, Task> callback, TimeSpan? timeout = null) { }
        public bool TryGet(long chatId, out Func<ITelegramBotClient, string, CancellationToken, Task>? callback) { callback = null; return false; }
        public void Clear(long chatId) { }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static TelegramBotService CreateService(FakeSetCommandsBotClient botClient)
    {
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "fake-token",
            AuthorizedChatId = 42
        });
        var authFilter = new AuthorizationFilter(options, NullLogger<AuthorizationFilter>.Instance);

        return new TelegramBotService(
            options,
            authFilter,
            handlers: [],
            botClient: botClient,
            onboardingService: new FakeOnboardingService(),
            conversationState: new FakeConversationStateService(),
            logger: NullLogger<TelegramBotService>.Instance);
    }

    private static async Task RunServiceBrieflyAsync(TelegramBotService svc)
    {
        using var cts = new CancellationTokenSource();
        var executeTask = svc.StartAsync(cts.Token);
        await Task.Delay(200); // laisser les Task.Run s'exécuter
        cts.Cancel();
        try { await executeTask; } catch (OperationCanceledException) { }
    }

    // ─── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AppelleSetMyCommandsAvec9Commandes()
    {
        var bot = new FakeSetCommandsBotClient();
        var svc = CreateService(bot);

        await RunServiceBrieflyAsync(svc);

        Assert.Single(bot.CapturedCommands);
        Assert.Equal(9, bot.CapturedCommands[0].Length);
    }

    [Fact]
    public async Task ExecuteAsync_CommandesCorrespondentAuHelp()
    {
        var bot = new FakeSetCommandsBotClient();
        var svc = CreateService(bot);

        await RunServiceBrieflyAsync(svc);

        Assert.Single(bot.CapturedCommands);
        var commands = bot.CapturedCommands[0];
        var commandNames = commands.Select(c => c.Command).ToArray();

        Assert.Contains("run",      commandNames);
        Assert.Contains("status",   commandNames);
        Assert.Contains("start",    commandNames);
        Assert.Contains("stop",     commandNames);
        Assert.Contains("history",  commandNames);
        Assert.Contains("schedule", commandNames);
        Assert.Contains("tipsters", commandNames);
        Assert.Contains("report",   commandNames);
        Assert.Contains("help",     commandNames);
    }

    [Fact]
    public async Task ExecuteAsync_CommandesSansBarre_Slash()
    {
        var bot = new FakeSetCommandsBotClient();
        var svc = CreateService(bot);

        await RunServiceBrieflyAsync(svc);

        Assert.Single(bot.CapturedCommands);
        foreach (var cmd in bot.CapturedCommands[0])
        {
            Assert.False(cmd.Command.StartsWith('/'),
                $"La commande '{cmd.Command}' ne doit pas contenir '/'");
        }
    }

    [Fact]
    public async Task ExecuteAsync_SetMyCommandsEchoue_BotContinueNormalement()
    {
        var bot = new FakeSetCommandsBotClient { ThrowOnSetCommands = true };
        var svc = CreateService(bot);

        // Ne doit pas lever d'exception — le bot continue son démarrage
        var exception = await Record.ExceptionAsync(() => RunServiceBrieflyAsync(svc));
        Assert.Null(exception);

        // Vérifier que le bot n'a pas capturé de commandes (l'exception a bien été levée)
        Assert.Empty(bot.CapturedCommands);
    }

    [Fact]
    public async Task ExecuteAsync_DescriptionsNonVides()
    {
        var bot = new FakeSetCommandsBotClient();
        var svc = CreateService(bot);

        await RunServiceBrieflyAsync(svc);

        Assert.Single(bot.CapturedCommands);
        foreach (var cmd in bot.CapturedCommands[0])
        {
            Assert.False(string.IsNullOrWhiteSpace(cmd.Description),
                $"La commande '{cmd.Command}' doit avoir une description");
        }
    }
}
