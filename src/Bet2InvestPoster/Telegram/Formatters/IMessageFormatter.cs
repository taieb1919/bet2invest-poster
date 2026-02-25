using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public interface IMessageFormatter
{
    string FormatStatus(ExecutionState state);
    string FormatHistory(List<HistoryEntry> entries);
    string FormatTipsters(List<TipsterConfig> tipsters);
}
