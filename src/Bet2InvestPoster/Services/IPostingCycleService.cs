using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IPostingCycleService
{
    /// <summary>
    /// Runs the full posting cycle: purge → fetch tipsters → fetch bets → select → publish → record.
    /// Returns a CycleResult with scraping, filtering, and publishing statistics.
    /// </summary>
    Task<CycleResult> RunCycleAsync(CancellationToken ct = default);
}
