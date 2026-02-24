using Bet2InvestPoster.Models;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class BetSelector : IBetSelector
{
    private static readonly int[] ValidCounts = [5, 10, 15];

    private readonly IHistoryManager _historyManager;
    private readonly ILogger<BetSelector> _logger;

    public BetSelector(IHistoryManager historyManager, ILogger<BetSelector> logger)
    {
        _historyManager = historyManager;
        _logger = logger;
    }

    public async Task<List<PendingBet>> SelectAsync(List<PendingBet> candidates, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Select"))
        {
            // AC#1 : exclure les paris déjà publiés (même match + marché + côté)
            var publishedKeys = await _historyManager.LoadPublishedKeysAsync(ct);
            var available = candidates
                .Where(b => b.Market != null)
                .Where(b =>
                {
                    var key = b.DeduplicationKey;
                    return key != null && !publishedKeys.Contains(key);
                })
                .ToList();

            // AC#2 : cible aléatoire parmi 5, 10, 15
            var targetCount = ValidCounts[Random.Shared.Next(ValidCounts.Length)];

            // AC#3 : si moins de candidats que la cible, retourner tout ce qui est disponible
            List<PendingBet> selected;
            if (available.Count <= targetCount)
            {
                selected = available;
            }
            else
            {
                selected = available.OrderBy(_ => Random.Shared.Next()).Take(targetCount).ToList();
            }

            // AC#4 : log Step="Select"
            _logger.LogInformation(
                "{Available} candidats disponibles (après filtre doublons), {Selected} sélectionnés (cible={Target})",
                available.Count, selected.Count, targetCount);

            return selected;
        }
    }

}
