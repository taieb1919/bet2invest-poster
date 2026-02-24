namespace Bet2InvestPoster.Services;

public interface IPostingCycleService
{
    /// <summary>
    /// Runs the full posting cycle: purge → fetch tipsters → fetch bets → select → publish → record.
    /// </summary>
    Task RunCycleAsync(CancellationToken ct = default);
}
