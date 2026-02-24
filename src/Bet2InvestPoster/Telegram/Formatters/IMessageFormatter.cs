using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public interface IMessageFormatter
{
    string FormatStatus(ExecutionState state);
}
