using Bet2InvestPoster.Services;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class StopCommandHandler : ICommandHandler
{
    private readonly IExecutionStateService _stateService;
    private readonly ILogger<StopCommandHandler> _logger;

    public StopCommandHandler(
        IExecutionStateService stateService,
        ILogger<StopCommandHandler> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/stop";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /stop reçue");
        }

        if (!_stateService.GetSchedulingEnabled())
        {
            await bot.SendMessage(chatId, "ℹ️ Scheduling déjà suspendu.", cancellationToken: ct);
            return;
        }

        _stateService.SetSchedulingEnabled(false);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Scheduling suspendu via /stop");
        }

        await bot.SendMessage(
            chatId,
            "⏸️ Scheduling suspendu. Utilisez /start pour reprendre.",
            cancellationToken: ct);
    }
}
