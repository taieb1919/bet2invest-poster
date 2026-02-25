using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class ReportCommandHandler : ICommandHandler
{
    private readonly IHistoryManager _historyManager;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<ReportCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public ReportCommandHandler(
        IHistoryManager historyManager,
        IMessageFormatter formatter,
        ILogger<ReportCommandHandler> logger,
        TimeProvider timeProvider)
    {
        _historyManager = historyManager;
        _formatter = formatter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public bool CanHandle(string command) => command == "/report";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /report reçue");

            var parts = message.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var days = 7;

            if (parts?.Length > 1)
            {
                if (!int.TryParse(parts[1], out var parsed) || parsed <= 0 || parsed > 365)
                {
                    await bot.SendMessage(
                        chatId,
                        "Usage : /report [jours] (ex: /report 30)",
                        cancellationToken: ct);
                    return;
                }

                days = parsed;
            }

            var since = _timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(-days);
            var entries = await _historyManager.GetEntriesSinceAsync(since, ct);
            var text = _formatter.FormatReport(entries, days);

            await bot.SendMessage(chatId, text, cancellationToken: ct);
        }
    }
}
