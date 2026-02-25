using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public interface IMessageFormatter
{
    string FormatStatus(ExecutionState state);
    string FormatHistory(List<HistoryEntry> entries);
    string FormatTipsters(List<TipsterConfig> tipsters);
    string FormatOnboardingMessage(bool apiConnected, int tipsterCount, string scheduleTime);
    string FormatScrapedTipsters(List<ScrapedTipster> tipsters);
    string FormatScrapedTipstersConfirmation();
    string FormatReport(List<HistoryEntry> entries, int days);
}
