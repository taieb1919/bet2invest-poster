using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class MyStatsCommandHandler : ICommandHandler
{
    private readonly IHistoryManager _historyManager;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<MyStatsCommandHandler> _logger;

    public MyStatsCommandHandler(
        IHistoryManager historyManager,
        IMessageFormatter formatter,
        ILogger<MyStatsCommandHandler> logger)
    {
        _historyManager = historyManager;
        _formatter = formatter;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/mystats";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /mystats reçue");

            var entries = await _historyManager.GetEntriesSinceAsync(DateTime.MinValue, ct);
            var text = _formatter.FormatMyStats(entries);

            await bot.SendMessage(chatId, text, cancellationToken: ct);
        }
    }
}
