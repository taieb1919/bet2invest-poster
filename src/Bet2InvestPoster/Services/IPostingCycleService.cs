using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IPostingCycleService
{
    /// <summary>
    /// Runs the full posting cycle: purge → fetch tipsters → fetch bets → select → publish → record.
    /// Returns a CycleResult with scraping, filtering, and publishing statistics.
    /// </summary>
    Task<CycleResult> RunCycleAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs scrape + select without publishing. Returns selected bets and partial cycle result.
    /// Used by /run interactive preview.
    /// </summary>
    Task<(IReadOnlyList<PendingBet> Bets, CycleResult PartialResult)> PrepareCycleAsync(CancellationToken ct = default);
}
