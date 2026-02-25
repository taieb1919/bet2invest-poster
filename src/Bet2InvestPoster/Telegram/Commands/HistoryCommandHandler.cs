using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class HistoryCommandHandler : ICommandHandler
{
    private readonly IHistoryManager _historyManager;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<HistoryCommandHandler> _logger;

    public HistoryCommandHandler(
        IHistoryManager historyManager,
        IMessageFormatter formatter,
        ILogger<HistoryCommandHandler> logger)
    {
        _historyManager = historyManager;
        _formatter = formatter;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/history";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /history reçue");

            var entries = await _historyManager.GetRecentEntriesAsync(7, ct);
            var text = _formatter.FormatHistory(entries);

            await bot.SendMessage(chatId, text, cancellationToken: ct);
        }
    }
}
