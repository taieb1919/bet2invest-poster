using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class TipstersCommandHandler : ICommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageFormatter _formatter;
    private readonly IConversationStateService _conversationState;
    private readonly ILogger<TipstersCommandHandler> _logger;

    public TipstersCommandHandler(
        IServiceScopeFactory scopeFactory,
        IMessageFormatter formatter,
        IConversationStateService conversationState,
        ILogger<TipstersCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _formatter = formatter;
        _conversationState = conversationState;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/tipsters";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /tipsters reçue");

            var parts = (message.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 1)
            {
                var subCommand = parts[1].ToLowerInvariant();

                switch (subCommand)
                {
                    case "add":
                        if (parts.Length < 3)
                        {
                            await bot.SendMessage(chatId,
                                "Usage : /tipsters add <lien_tipster>",
                                cancellationToken: ct);
                            return;
                        }
                        await HandleAddAsync(bot, chatId, parts[2], ct);
                        return;

                    case "remove":
                        if (parts.Length < 3)
                        {
                            await bot.SendMessage(chatId,
                                "Usage : /tipsters remove <lien_tipster>",
                                cancellationToken: ct);
                            return;
                        }
                        await HandleRemoveAsync(bot, chatId, parts[2], ct);
                        return;

                    case "update":
                        await HandleUpdateAsync(bot, chatId, ct);
                        return;

                    default:
                        await bot.SendMessage(chatId,
                            "Usage : /tipsters | /tipsters add <lien> | /tipsters remove <lien> | /tipsters update",
                            cancellationToken: ct);
                        return;
                }
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();

                var tipsters = await tipsterService.LoadTipstersAsync(ct);
                var text = _formatter.FormatTipsters(tipsters);

                await bot.SendMessage(chatId, text, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du chargement des tipsters");
                await bot.SendMessage(chatId, "❌ Erreur lors de la lecture des tipsters.", cancellationToken: ct);
            }
        }
    }

    private async Task HandleAddAsync(ITelegramBotClient bot, long chatId, string addUrl, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();

            var added = await tipsterService.AddTipsterAsync(addUrl, ct);
            await bot.SendMessage(chatId,
                $"✅ Tipster ajouté : {added.Name}",
                cancellationToken: ct);
        }
        catch (InvalidOperationException)
        {
            await bot.SendMessage(chatId,
                "ℹ️ Ce tipster est déjà dans la liste.",
                cancellationToken: ct);
        }
        catch (ArgumentException)
        {
            await bot.SendMessage(chatId,
                "❌ URL invalide. Format attendu : https://bet2invest.com/tipsters/performance-stats/<nom>",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'ajout du tipster");
            await bot.SendMessage(chatId, "❌ Erreur lors du traitement de la commande.", cancellationToken: ct);
        }
    }

    private async Task HandleRemoveAsync(ITelegramBotClient bot, long chatId, string removeUrl, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();

            var removeDisplay = new TipsterConfig { Url = removeUrl };
            removeDisplay.TryExtractSlug(out var removeSlug);
            var removed = await tipsterService.RemoveTipsterAsync(removeUrl, ct);
            if (removed)
            {
                await bot.SendMessage(chatId,
                    $"🗑️ Tipster retiré : {removeSlug ?? removeUrl}",
                    cancellationToken: ct);
            }
            else
            {
                await bot.SendMessage(chatId,
                    "❌ Tipster non trouvé dans la liste.",
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression du tipster");
            await bot.SendMessage(chatId, "❌ Erreur lors du traitement de la commande.", cancellationToken: ct);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        List<ScrapedTipster> scraped;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();
            scraped = await client.GetFreeTipstersAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du scraping des tipsters free");
            await bot.SendMessage(chatId,
                $"❌ Impossible de récupérer la liste des tipsters — {ex.Message}",
                cancellationToken: ct);
            return;
        }

        if (scraped.Count == 0)
        {
            await bot.SendMessage(chatId,
                "📭 Aucun tipster gratuit trouvé sur bet2invest.",
                cancellationToken: ct);
            return;
        }

        _logger.LogInformation("Scraping terminé : {Count} tipsters free, action utilisateur en attente", scraped.Count);

        await bot.SendMessage(chatId, _formatter.FormatScrapedTipsters(scraped), cancellationToken: ct);
        await bot.SendMessage(chatId, _formatter.FormatScrapedTipstersConfirmation(), cancellationToken: ct);

        // Enregistrer l'état de conversation — la réponse de l'utilisateur sera routée ici
        _conversationState.Register(chatId, async (b, replyText, replyCt) =>
        {
            await HandleUpdateConfirmationAsync(b, chatId, scraped, replyText, replyCt);
        });
    }

    private async Task HandleUpdateConfirmationAsync(
        ITelegramBotClient bot,
        long chatId,
        List<ScrapedTipster> scraped,
        string replyText,
        CancellationToken ct)
    {
        var reply = replyText.Trim().ToLowerInvariant();

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Réponse confirmation /tipsters update : {Reply}", reply);

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();

                switch (reply)
                {
                    case "oui":
                        var newList = scraped.Select(s => s.ToTipsterConfig()).ToList();
                        await tipsterService.ReplaceTipstersAsync(newList, ct);
                        await bot.SendMessage(chatId,
                            $"✅ Liste mise à jour : {newList.Count} tipster{(newList.Count > 1 ? "s" : "")}.",
                            cancellationToken: ct);
                        break;

                    case "fusionner":
                        List<TipsterConfig> existing;
                        try { existing = await tipsterService.LoadTipstersAsync(ct); }
                        catch (FileNotFoundException) { existing = []; }

                        var added = 0;
                        foreach (var s in scraped)
                        {
                            var candidateConfig = s.ToTipsterConfig();
                            candidateConfig.TryExtractSlug(out var newSlug);
                            var isDuplicate = existing.Any(e =>
                                e.TryExtractSlug(out var eSlug) &&
                                string.Equals(eSlug, newSlug, StringComparison.OrdinalIgnoreCase));
                            if (!isDuplicate)
                            {
                                existing.Add(candidateConfig);
                                added++;
                            }
                        }

                        if (added > 0)
                            await tipsterService.ReplaceTipstersAsync(existing, ct);

                        await bot.SendMessage(chatId,
                            $"✅ {added} tipster{(added > 1 ? "s" : "")} ajouté{(added > 1 ? "s" : "")}. Total : {existing.Count}.",
                            cancellationToken: ct);
                        break;

                    case "non":
                        await bot.SendMessage(chatId,
                            "❌ Mise à jour annulée.",
                            cancellationToken: ct);
                        break;

                    default:
                        await bot.SendMessage(chatId,
                            "⚠️ Réponse non reconnue. Répondez Oui, Non ou Fusionner.",
                            cancellationToken: ct);
                        _conversationState.Register(chatId, async (b, rt, rct) =>
                            await HandleUpdateConfirmationAsync(b, chatId, scraped, rt, rct));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la confirmation /tipsters update");
                await bot.SendMessage(chatId,
                    "❌ Erreur lors de la mise à jour de la liste.",
                    cancellationToken: ct);
            }
        }
    }
}
