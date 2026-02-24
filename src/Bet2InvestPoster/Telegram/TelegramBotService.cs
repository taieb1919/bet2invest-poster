using Bet2InvestPoster.Configuration;
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
    private readonly ILogger<TelegramBotService> _logger;
    private volatile int _retryDelaySeconds = 1;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        AuthorizationFilter authFilter,
        ILogger<TelegramBotService> logger)
    {
        _options = options.Value;
        _authFilter = authFilter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bot = new TelegramBotClient(_options.BotToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
            DropPendingUpdates = true
        };

        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Bot Telegram démarré — polling actif");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt propre — normal
        }
    }

    private Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        _retryDelaySeconds = 1; // Reset backoff — any update proves connectivity

        var chatId = update.Message?.Chat.Id ?? 0;
        if (chatId == 0 || !_authFilter.IsAuthorized(chatId))
            return Task.CompletedTask;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            var text = update.Message?.Text ?? "(no text)";
            _logger.LogInformation("Message reçu — commande: {Command}", text);
        }

        // Story 4.2 ajoutera le dispatch des commandes /run et /status
        return Task.CompletedTask;
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
