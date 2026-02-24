using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Extends the scraper's Bet2InvestClient with authenticated endpoints not covered by the scraper:
/// upcoming (pending) bets and bet order publication.
/// </summary>
public interface IExtendedBet2InvestClient
{
    /// <summary>True when a valid, non-expired token is held in memory.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Authenticates with the credentials from Bet2InvestOptions.</summary>
    Task LoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves tipster slugs (usernames) to numeric IDs via the /tipsters API.
    /// Sets <see cref="Models.TipsterConfig.NumericId"/> on each resolved tipster.
    /// </summary>
    Task ResolveTipsterIdsAsync(List<Models.TipsterConfig> tipsters, CancellationToken ct = default);

    /// <summary>
    /// Returns the pending (upcoming) bets for the given tipster numeric ID and whether the authenticated
    /// user can see that tipster's bets (canSeeBets). Calls GET /v1/statistics/{numericId}.
    /// When canSeeBets is false the tipster is pro/restricted; bets will be empty.
    /// </summary>
    Task<(bool CanSeeBets, List<Models.PendingBet> Bets)> GetUpcomingBetsAsync(int tipsterNumericId, CancellationToken ct = default);

    /// <summary>Publishes a bet order via POST /v1/bet-orders. Returns the order ID if available.</summary>
    Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default);
}
