using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IBetSelector
{
    /// <summary>
    /// Filters out already-published bets (via IHistoryManager) and randomly selects
    /// 5, 10, or 15 from the remaining candidates. Returns all available if fewer than target.
    /// Logs result with Step="Select".
    /// </summary>
    Task<List<PendingBet>> SelectAsync(List<PendingBet> candidates, CancellationToken ct = default);
}
