using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IBetPublisher
{
    /// <summary>
    /// Publishes each selected bet via IExtendedBet2InvestClient and records it in history.
    /// Returns the list of successfully published bets.
    /// Logs with Step="Publish".
    /// </summary>
    Task<IReadOnlyList<PendingBet>> PublishAllAsync(IReadOnlyList<PendingBet> selected, CancellationToken ct = default);
}
