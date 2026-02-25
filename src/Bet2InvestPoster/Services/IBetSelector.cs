using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IBetSelector
{
    /// <summary>
    /// Filters out already-published bets (via IHistoryManager), applies odds/horizon filters,
    /// then randomly selects 5, 10, or 15 from the remaining candidates.
    /// Returns a SelectionResult containing FilteredCount (after filters, before random pick)
    /// and the Selected list.
    /// Logs result with Step="Select".
    /// </summary>
    Task<SelectionResult> SelectAsync(List<PendingBet> candidates, CancellationToken ct = default);
}
