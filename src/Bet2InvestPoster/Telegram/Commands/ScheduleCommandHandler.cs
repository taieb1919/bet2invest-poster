using System.Globalization;
using Bet2InvestPoster.Services;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class ScheduleCommandHandler : ICommandHandler
{
    private readonly IExecutionStateService _stateService;
    private readonly ILogger<ScheduleCommandHandler> _logger;

    public ScheduleCommandHandler(
        IExecutionStateService stateService,
        ILogger<ScheduleCommandHandler> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/schedule";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text ?? string.Empty;
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /schedule reçue");

            // Pas d'argument → afficher les horaires courants + aide
            if (parts.Length < 2)
            {
                var currentTimes = _stateService.GetScheduleTimes();
                await bot.SendMessage(
                    chatId,
                    $"⏰ Horaires actuels : {string.Join(", ", currentTimes)}. Usage : /schedule HH:mm[,HH:mm,...]",
                    cancellationToken: ct);
                return;
            }

            var timeArg = parts[1].Trim();

            // Parser les horaires séparés par virgule
            var rawTimes = timeArg.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToArray();

            // Valider chaque horaire
            foreach (var raw in rawTimes)
            {
                if (!TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    await bot.SendMessage(
                        chatId,
                        $"❌ Horaire invalide : {raw}. Usage : /schedule HH:mm[,HH:mm,...]",
                        cancellationToken: ct);
                    return;
                }
            }

            // Trier et dédupliquer — tri chronologique explicite via TimeOnly (pas lexicographique)
            var validTimes = rawTimes
                .Distinct()
                .OrderBy(t => TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture))
                .ToArray();

            _stateService.SetScheduleTimes(validTimes);

            // Toujours afficher "sera recalculé" — le SchedulerWorker recalcule NextRun au prochain tour
            const string nextRunText = "sera recalculé sous peu";

            _logger.LogInformation("Horaires de scheduling mis à jour : {ScheduleTimes}", string.Join(", ", validTimes));

            await bot.SendMessage(
                chatId,
                $"⏰ Horaires mis à jour : {string.Join(", ", validTimes)}. Prochain run : {nextRunText}.",
                cancellationToken: ct);
        }
    }
}
