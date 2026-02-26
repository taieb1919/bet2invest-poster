using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bet2InvestPoster.Telegram.Commands;

public class RunCommandHandler : ICommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IResiliencePipelineService _resiliencePipelineService;
    private readonly PreviewStateService _previewState;
    private readonly IMessageFormatter _formatter;
    private readonly int _maxRetryCount;
    private readonly ILogger<RunCommandHandler> _logger;

    public RunCommandHandler(
        IServiceScopeFactory scopeFactory,
        IResiliencePipelineService resiliencePipelineService,
        IExecutionStateService stateService,
        PreviewStateService previewState,
        IMessageFormatter formatter,
        IOptions<PosterOptions> options,
        ILogger<RunCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _resiliencePipelineService = resiliencePipelineService;
        _ = stateService;
        _previewState = previewState;
        _formatter = formatter;
        _maxRetryCount = options.Value.MaxRetryCount;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/run";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /run reçue — scraping pour aperçu");
        }

        await bot.SendMessage(chatId, "⏳ Scraping en cours...", cancellationToken: ct);

        try
        {
            IReadOnlyList<PendingBet> bets;
            CycleResult partialResult;

            await _resiliencePipelineService.ExecuteCycleWithRetryAsync(async token =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
                (bets, partialResult) = await cycleService.PrepareCycleAsync(token);

                if (bets.Count == 0)
                {
                    await bot.SendMessage(chatId,
                        "📭 Aucun pronostic disponible après filtrage.",
                        cancellationToken: token);
                    return;
                }

                var session = new PreviewSession
                {
                    ChatId = chatId,
                    Bets = bets,
                    Selected = Enumerable.Repeat(true, bets.Count).ToArray(),
                    PartialCycleResult = partialResult
                };
                _previewState.Set(chatId, session);

                var text = _formatter.FormatPreview(session);
                var keyboard = BuildPreviewKeyboard(session);
                var sent = await bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: token);
                session.MessageId = sent.Id;
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            var remaining = _resiliencePipelineService.GetCircuitBreakerRemainingDuration();
            var minutes = remaining?.TotalMinutes ?? 5;
            using (LogContext.PushProperty("Step", "Notify"))
            {
                _logger.LogWarning("Circuit breaker actif — commande /run rejetée");
            }
            await bot.SendMessage(
                chatId,
                $"🔴 Circuit breaker actif — service API indisponible. Réessai automatique dans {minutes:F0} min.",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("Step", "Notify"))
            {
                _logger.LogError(ex, "Erreur lors du scraping via /run — toutes tentatives épuisées");
            }

            await bot.SendMessage(
                chatId,
                $"❌ Échec définitif après {_maxRetryCount} tentatives — {ex.GetType().Name}",
                cancellationToken: ct);
        }
    }

    internal static InlineKeyboardMarkup BuildPreviewKeyboard(PreviewSession session)
    {
        var rows = new List<InlineKeyboardButton[]>();

        for (var i = 0; i < session.Bets.Count; i++)
        {
            var bet = session.Bets[i];
            var icon = session.Selected[i] ? "✅" : "❌";
            var matchDesc = bet.Event?.Home != null && bet.Event?.Away != null
                ? $"{bet.Event.Home} vs {bet.Event.Away}"
                : "(sans description)";
            // Truncate to fit Telegram button limit
            var label = $"{icon} {i + 1}. {matchDesc}";
            if (label.Length > 40)
                label = label[..37] + "...";

            rows.Add([InlineKeyboardButton.WithCallbackData(label, $"t:{session.Id}:{i}")]);
        }

        rows.Add([
            InlineKeyboardButton.WithCallbackData("📤 Publier", $"pub:{session.Id}"),
            InlineKeyboardButton.WithCallbackData("🚫 Annuler", $"can:{session.Id}")
        ]);

        return new InlineKeyboardMarkup(rows);
    }
}
