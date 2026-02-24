using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public interface ICommandHandler
{
    bool CanHandle(string command);
    Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct);
}
