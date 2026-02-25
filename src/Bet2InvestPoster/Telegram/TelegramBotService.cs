using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bet2InvestPoster.Telegram;

public class TelegramBotService : BackgroundService
{
    private readonly TelegramOptions _options;
    private readonly AuthorizationFilter _authFilter;
    private readonly IEnumerable<ICommandHandler> _handlers;
    private readonly ITelegramBotClient _botClient;
    private readonly IOnboardingService _onboardingService;
    private readonly IConversationStateService _conversationState;
    private readonly ILogger<TelegramBotService> _logger;
    private volatile int _retryDelaySeconds = 1;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        AuthorizationFilter authFilter,
        IEnumerable<ICommandHandler> handlers,
        ITelegramBotClient botClient,
        IOnboardingService onboardingService,
        IConversationStateService conversationState,
        ILogger<TelegramBotService> logger)
    {
        _options = options.Value;
        _authFilter = authFilter;
        _handlers = handlers;
        _botClient = botClient;
        _onboardingService = onboardingService;
        _conversationState = conversationState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Bot Telegram démarré — polling actif");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _onboardingService.TrySendOnboardingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Onboarding échoué — non bloquant");
            }
        }, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt propre — normal
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        _retryDelaySeconds = 1; // Reset backoff — any update proves connectivity

        var chatId = update.Message?.Chat.Id ?? 0;
        if (chatId == 0 || !_authFilter.IsAuthorized(chatId))
            return;

        var text = update.Message?.Text ?? string.Empty;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Message reçu — texte: {Text}", text);
        }

        // Si un état de conversation est en attente pour ce chat, le router en priorité
        if (_conversationState.TryGet(chatId, out var pendingCallback) && pendingCallback is not null)
        {
            _conversationState.Clear(chatId);
            try
            {
                await pendingCallback(bot, text, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement de la réponse conversation");
                await bot.SendMessage(chatId, "❌ Erreur lors du traitement de votre réponse.", cancellationToken: ct);
            }
            return;
        }

        var command = text.Split(' ')[0].ToLowerInvariant();

        if (!command.StartsWith('/'))
            return;

        var handler = _handlers.FirstOrDefault(h => h.CanHandle(command));
        if (handler is not null)
        {
            await handler.HandleAsync(bot, update.Message!, ct);
        }
        else
        {
            await bot.SendMessage(chatId,
                "Commande inconnue. Tapez /help pour la liste des commandes.",
                cancellationToken: ct);
        }
    }

    private async Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        using (LogContext.PushProperty("Step", "Notify"))
        {
            var safeMessage = string.IsNullOrEmpty(_options.BotToken)
                ? ex.Message
                : ex.Message.Replace(_options.BotToken, "[REDACTED]");
            _logger.LogWarning("Erreur polling Telegram ({Source}): {Message} — retry dans {Delay}s",
                source, safeMessage, _retryDelaySeconds);
        }

        if (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds), ct);
            _retryDelaySeconds = Math.Min(_retryDelaySeconds * 2, 60);
        }
    }
}
