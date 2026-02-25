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
    private readonly ILogger<TipstersCommandHandler> _logger;

    public TipstersCommandHandler(
        IServiceScopeFactory scopeFactory,
        IMessageFormatter formatter,
        ILogger<TipstersCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _formatter = formatter;
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

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();

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
                            var addUrl = parts[2];
                            try
                            {
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
                            return;

                        case "remove":
                            if (parts.Length < 3)
                            {
                                await bot.SendMessage(chatId,
                                    "Usage : /tipsters remove <lien_tipster>",
                                    cancellationToken: ct);
                                return;
                            }
                            var removeUrl = parts[2];
                            // Extract slug for display before removing
                            var removeDisplay = new Bet2InvestPoster.Models.TipsterConfig { Url = removeUrl };
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
                            return;

                        default:
                            await bot.SendMessage(chatId,
                                "Usage : /tipsters | /tipsters add <lien> | /tipsters remove <lien>",
                                cancellationToken: ct);
                            return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors du traitement de la sous-commande /tipsters {SubCommand}", subCommand);
                    await bot.SendMessage(chatId, "❌ Erreur lors du traitement de la commande.", cancellationToken: ct);
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
}
