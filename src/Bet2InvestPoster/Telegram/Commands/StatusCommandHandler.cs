using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class StatusCommandHandler : ICommandHandler
{
    private readonly IExecutionStateService _stateService;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<StatusCommandHandler> _logger;

    public StatusCommandHandler(
        IExecutionStateService stateService,
        IMessageFormatter formatter,
        ILogger<StatusCommandHandler> logger)
    {
        _stateService = stateService;
        _formatter = formatter;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/status";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /status reçue");
        }

        var state = _stateService.GetState();
        var text = _formatter.FormatStatus(state);
        await bot.SendMessage(chatId, text, cancellationToken: ct);
    }
}
