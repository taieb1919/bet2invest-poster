using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Telegram.Bot;

namespace Bet2InvestPoster.Services;

public class NotificationService : INotificationService
{
    private readonly ITelegramBotClient _botClient;
    private readonly long _chatId;
    private readonly IMessageFormatter _messageFormatter;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ITelegramBotClient botClient,
        IOptions<TelegramOptions> options,
        IMessageFormatter messageFormatter,
        ILogger<NotificationService> logger)
    {
        _botClient = botClient;
        _chatId = options.Value.AuthorizedChatId;
        _messageFormatter = messageFormatter;
        _logger = logger;
    }

    public async Task NotifySuccessAsync(CycleResult result, CancellationToken ct = default)
    {
        var text = _messageFormatter.FormatCycleSuccess(result);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation(
                "Envoi notification succès — {Published}/{Filtered} sur {Scraped} scrapés",
                result.PublishedCount, result.FilteredCount, result.ScrapedCount);
            await _botClient.SendMessage(_chatId, text, cancellationToken: ct);
        }
    }

    public async Task NotifyFailureAsync(string reason, CancellationToken ct = default)
    {
        var text = $"❌ Échec — {reason}.";

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogWarning("Envoi notification échec — {Reason}", reason);
            await _botClient.SendMessage(_chatId, text, cancellationToken: ct);
        }
    }

    public async Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default)
    {
        var text = $"❌ Échec définitif — {reason} après {attempts} tentatives.";

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogWarning(
                "Envoi notification échec définitif — {Attempts} tentatives, raison: {Reason}",
                attempts, reason);
            await _botClient.SendMessage(_chatId, text, cancellationToken: ct);
        }
    }

    public async Task NotifyNoFilteredCandidatesAsync(string filterDetails, CancellationToken ct = default)
    {
        var text = $"⚠️ Aucun pronostic ne correspond aux critères de filtrage ({filterDetails}).";

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogWarning("Envoi notification filtrage vide — {FilterDetails}", filterDetails);
            await _botClient.SendMessage(_chatId, text, cancellationToken: ct);
        }
    }

    public async Task SendMessageAsync(string message, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Envoi message générique — {Length} caractères", message.Length);
            await _botClient.SendMessage(_chatId, message, cancellationToken: ct);
        }
    }
}
