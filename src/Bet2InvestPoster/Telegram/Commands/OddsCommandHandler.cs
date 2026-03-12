using System.Globalization;
using Bet2InvestPoster.Services;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class OddsCommandHandler : ICommandHandler
{
    private static readonly string[] ValidModes = ["random", "intelligent", "all"];

    private readonly IExecutionStateService _stateService;
    private readonly ILogger<OddsCommandHandler> _logger;

    public OddsCommandHandler(
        IExecutionStateService stateService,
        ILogger<OddsCommandHandler> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/odds";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text ?? string.Empty;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /odds reçue");

            // /odds → afficher la configuration actuelle
            if (parts.Length < 2)
            {
                var minOdds = _stateService.GetMinOdds();
                var maxOdds = _stateService.GetMaxOdds();
                var mode = _stateService.GetSelectionMode();

                string modeIcon = mode switch
                {
                    "all" => "🔓",
                    "intelligent" => "🧠",
                    _ => "🎲"
                };

                string status;
                if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
                {
                    status = $"{modeIcon} Mode ALL actif — tous les pronostics sont postés (pas de filtre de cotes, pas de limite).";
                }
                else
                {
                    var minStr = minOdds.HasValue ? $"{minOdds.Value:F2}" : "aucun";
                    var maxStr = maxOdds.HasValue ? $"{maxOdds.Value:F2}" : "aucun";
                    status = $"{modeIcon} Mode : {mode}\n📊 Filtrage cotes : min={minStr}, max={maxStr}";
                }

                status += "\n\nUsage :\n" +
                    "/odds <min> <max> — Définir les cotes\n" +
                    "/odds random — Sélection aléatoire (5/10/15)\n" +
                    "/odds intelligent — Sélection intelligente\n" +
                    "/odds all — Poster tous les pronostics\n" +
                    "/odds reset — Revenir aux valeurs par défaut";

                await bot.SendMessage(chatId, status, cancellationToken: ct);
                return;
            }

            var arg = parts[1].ToLowerInvariant();

            // /odds all | /odds random | /odds intelligent
            if (ValidModes.Contains(arg))
            {
                _stateService.SetSelectionMode(arg);
                _logger.LogInformation("Mode de sélection changé en '{Mode}' via /odds", arg);

                var description = arg switch
                {
                    "all" => "🔓 Mode ALL activé — tous les pronostics seront postés sans filtre de cotes ni limite de sélection.",
                    "intelligent" => "🧠 Mode INTELLIGENT activé — les pronostics seront sélectionnés par score (ROI, WinRate, diversité, fraîcheur).",
                    _ => "🎲 Mode RANDOM activé — sélection aléatoire de 5/10/15 pronostics."
                };

                await bot.SendMessage(chatId, description, cancellationToken: ct);
                return;
            }

            // /odds reset
            if (arg == "reset")
            {
                _stateService.SetSelectionMode("random");
                _stateService.SetOddsFilter(null, null);
                _logger.LogInformation("Filtrage cotes réinitialisé via /odds reset");

                await bot.SendMessage(chatId,
                    "🔄 Filtrage cotes réinitialisé — aucun filtre de cotes actif, sélection aléatoire (5/10/15).",
                    cancellationToken: ct);
                return;
            }

            // /odds <min> [max]
            if (!decimal.TryParse(arg, NumberStyles.Any, CultureInfo.InvariantCulture, out var minValue) || minValue <= 0)
            {
                await bot.SendMessage(chatId,
                    "❌ Cote min invalide. Usage : /odds <min> <max> | /odds random | /odds intelligent | /odds all | /odds reset",
                    cancellationToken: ct);
                return;
            }

            decimal? maxValue = null;
            if (parts.Length >= 3)
            {
                if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                {
                    await bot.SendMessage(chatId,
                        "❌ Cote max invalide. Usage : /odds <min> <max>",
                        cancellationToken: ct);
                    return;
                }
                maxValue = parsed;

                if (minValue >= maxValue)
                {
                    await bot.SendMessage(chatId,
                        "❌ La cote min doit être inférieure à la cote max.",
                        cancellationToken: ct);
                    return;
                }
            }

            _stateService.SetOddsFilter(minValue, maxValue);

            var maxStr2 = maxValue.HasValue ? $"{maxValue.Value:F2}" : "aucun";
            _logger.LogInformation("Filtrage cotes mis à jour : min={Min}, max={Max}", minValue, maxValue);

            await bot.SendMessage(chatId,
                $"✅ Filtrage cotes mis à jour : min={minValue:F2}, max={maxStr2}.",
                cancellationToken: ct);
        }
    }
}
