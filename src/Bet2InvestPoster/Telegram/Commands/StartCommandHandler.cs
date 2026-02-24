using Bet2InvestPoster.Services;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class StartCommandHandler : ICommandHandler
{
    private readonly IExecutionStateService _stateService;
    private readonly ILogger<StartCommandHandler> _logger;

    public StartCommandHandler(
        IExecutionStateService stateService,
        ILogger<StartCommandHandler> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/start";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /start reçue");
        }

        if (_stateService.GetSchedulingEnabled())
        {
            var currentState = _stateService.GetState();
            var currentNextRun = currentState.NextRunAt.HasValue
                ? currentState.NextRunAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
                : "non planifié";
            await bot.SendMessage(chatId, $"ℹ️ Scheduling déjà actif. Prochain run : {currentNextRun}.", cancellationToken: ct);
            return;
        }

        _stateService.SetSchedulingEnabled(true);

        var state = _stateService.GetState();
        var nextRunText = state.NextRunAt.HasValue
            ? state.NextRunAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "non planifié";

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Scheduling activé via /start");
        }

        await bot.SendMessage(
            chatId,
            $"▶️ Scheduling activé. Prochain run : {nextRunText}.",
            cancellationToken: ct);
    }
}
