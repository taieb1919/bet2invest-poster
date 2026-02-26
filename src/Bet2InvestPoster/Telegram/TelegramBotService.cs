using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly PreviewStateService _previewState;
    private readonly IMessageFormatter _formatter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly IExecutionStateService _executionStateService;
    private readonly ILogger<TelegramBotService> _logger;
    private volatile int _retryDelaySeconds = 1;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        AuthorizationFilter authFilter,
        IEnumerable<ICommandHandler> handlers,
        ITelegramBotClient botClient,
        IOnboardingService onboardingService,
        IConversationStateService conversationState,
        PreviewStateService previewState,
        IMessageFormatter formatter,
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IExecutionStateService executionStateService,
        ILogger<TelegramBotService> logger)
    {
        _options = options.Value;
        _authFilter = authFilter;
        _handlers = handlers;
        _botClient = botClient;
        _onboardingService = onboardingService;
        _conversationState = conversationState;
        _previewState = previewState;
        _formatter = formatter;
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _executionStateService = executionStateService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
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

        _ = Task.Run(async () =>
        {
            try
            {
                var commands = new BotCommand[]
                {
                    new() { Command = "run",      Description = "Exécuter un cycle de publication" },
                    new() { Command = "status",   Description = "Afficher l'état du système" },
                    new() { Command = "start",    Description = "Activer le scheduling automatique" },
                    new() { Command = "stop",     Description = "Suspendre le scheduling automatique" },
                    new() { Command = "history",  Description = "Historique des publications récentes" },
                    new() { Command = "schedule", Description = "Configurer les horaires d'exécution [HH:mm,...]" },
                    new() { Command = "tipsters", Description = "Gérer la liste des tipsters" },
                    new() { Command = "report",   Description = "Tableau de bord des performances [jours]" },
                    new() { Command = "help",     Description = "Afficher cette aide" },
                };
                await _botClient.SetMyCommands(commands: commands, cancellationToken: stoppingToken);
                using (LogContext.PushProperty("Step", "Notify"))
                {
                    _logger.LogInformation("Commandes du bot enregistrées via setMyCommands ({Count} commandes)", commands.Length);
                }
            }
            catch (Exception ex)
            {
                using (LogContext.PushProperty("Step", "Notify"))
                {
                    _logger.LogWarning(ex, "Échec de l'enregistrement des commandes Telegram");
                }
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

        // Handle callback queries (inline keyboard buttons)
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback)
        {
            await HandleCallbackQueryAsync(bot, callback, ct);
            return;
        }

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

    private async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id ?? 0;
        if (chatId == 0 || !_authFilter.IsAuthorized(chatId))
        {
            await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            return;
        }

        var data = callback.Data ?? string.Empty;

        try
        {
            if (data.StartsWith("t:"))
            {
                // Toggle: t:{sessionId}:{index}
                var parts = data.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[2], out var index))
                {
                    await bot.AnswerCallbackQuery(callback.Id, "Données invalides", cancellationToken: ct);
                    return;
                }

                var session = _previewState.GetBySessionId(parts[1]);
                if (session == null)
                {
                    await bot.AnswerCallbackQuery(callback.Id, "⏰ Session expirée", cancellationToken: ct);
                    return;
                }

                session.Toggle(index);
                var text = _formatter.FormatPreview(session);
                var keyboard = RunCommandHandler.BuildPreviewKeyboard(session);
                await bot.EditMessageText(chatId, session.MessageId, text, replyMarkup: keyboard, cancellationToken: ct);
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            }
            else if (data.StartsWith("pub:"))
            {
                // Publish: pub:{sessionId}
                var sessionId = data[4..];
                var session = _previewState.GetBySessionId(sessionId);
                if (session == null)
                {
                    await bot.AnswerCallbackQuery(callback.Id, "⏰ Session expirée", cancellationToken: ct);
                    return;
                }

                var selected = session.GetSelectedBets();
                _previewState.Remove(chatId);

                if (selected.Count == 0)
                {
                    await bot.EditMessageText(chatId, session.MessageId,
                        "🚫 Aucun pronostic sélectionné — annulé.", cancellationToken: ct);
                    await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
                    return;
                }

                await bot.EditMessageText(chatId, session.MessageId,
                    $"📤 Publication de {selected.Count} pronostic(s)...", cancellationToken: ct);
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var publisher = scope.ServiceProvider.GetRequiredService<IBetPublisher>();
                var published = await publisher.PublishAllAsync(selected, ct);

                var cycleResult = session.PartialCycleResult with { PublishedBets = published };
                _executionStateService.RecordSuccess(published.Count);
                await _notificationService.NotifySuccessAsync(cycleResult, ct);

                _logger.LogInformation("Preview publié — {Count} pronostics", published.Count);
            }
            else if (data.StartsWith("can:"))
            {
                // Cancel: can:{sessionId}
                var sessionId = data[4..];
                var session = _previewState.GetBySessionId(sessionId);
                if (session != null)
                    _previewState.Remove(chatId);

                var messageId = session?.MessageId ?? callback.Message?.MessageId ?? 0;
                if (messageId > 0)
                    await bot.EditMessageText(chatId, messageId, "🚫 Publication annulée.", cancellationToken: ct);

                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            }
            else
            {
                await bot.AnswerCallbackQuery(callback.Id, "Action inconnue", cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du traitement du callback query");
            await bot.AnswerCallbackQuery(callback.Id, "❌ Erreur", cancellationToken: ct);
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
