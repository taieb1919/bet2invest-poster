using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Fetches all upcoming (pending, non-resolved) bets from a list of configured tipsters
/// and aggregates them into a single candidate pool for publication.
/// Rate limiting (NFR8) is delegated to <see cref="IExtendedBet2InvestClient"/>.
/// API change detection (NFR9) propagates as <see cref="Exceptions.Bet2InvestApiException"/>.
/// </summary>
public interface IUpcomingBetsFetcher
{
    /// <summary>
    /// Fetches upcoming bets for each tipster in <paramref name="tipsters"/> and returns
    /// the aggregated list of candidate bets. Tipsters with canSeeBets=false are skipped
    /// with a warning log (FR6 level-2 filter). Exceptions from the API propagate as-is.
    /// </summary>
    Task<List<SettledBet>> FetchAllAsync(List<TipsterConfig> tipsters, CancellationToken ct = default);
}
