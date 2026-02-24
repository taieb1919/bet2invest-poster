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
        }

        // Pas d'argument → afficher l'heure courante + aide
        if (parts.Length < 2)
        {
            var currentTime = _stateService.GetScheduleTime();
            await bot.SendMessage(
                chatId,
                $"⏰ Heure actuelle : {currentTime}. Usage : /schedule HH:mm",
                cancellationToken: ct);
            return;
        }

        var timeArg = parts[1].Trim();

        // Validation du format HH:mm
        if (!TimeOnly.TryParseExact(timeArg, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            await bot.SendMessage(
                chatId,
                "❌ Format invalide. Usage : /schedule HH:mm (ex: /schedule 08:00)",
                cancellationToken: ct);
            return;
        }

        _stateService.SetScheduleTime(timeArg);

        // Le SchedulerWorker recalculera NextRunAt au prochain CalculateNextRun()
        var state = _stateService.GetState();
        var nextRunText = state.NextRunAt.HasValue
            ? state.NextRunAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "sera recalculé sous peu";

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Heure de scheduling mise à jour : {ScheduleTime}", timeArg);
        }

        await bot.SendMessage(
            chatId,
            $"⏰ Heure de publication mise à jour : {timeArg}. Prochain run : {nextRunText}.",
            cancellationToken: ct);
    }
}
