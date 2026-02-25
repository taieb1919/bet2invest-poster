using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class BetSelector : IBetSelector
{
    private static readonly int[] ValidCounts = [5, 10, 15];

    private readonly IHistoryManager _historyManager;
    private readonly PosterOptions _options;
    private readonly ILogger<BetSelector> _logger;

    public BetSelector(IHistoryManager historyManager, IOptions<PosterOptions> posterOptions, ILogger<BetSelector> logger)
    {
        _historyManager = historyManager;
        _options = posterOptions.Value;
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

            // Filtrage avancé par cotes et plage horaire (AC: FR35, FR36)
            // Appliqué AVANT la sélection aléatoire
            var beforeFilterCount = available.Count;

            if (_options.MinOdds.HasValue)
                available = available.Where(b => b.Price >= _options.MinOdds.Value).ToList();

            if (_options.MaxOdds.HasValue)
                available = available.Where(b => b.Price <= _options.MaxOdds.Value).ToList();

            if (_options.EventHorizonHours.HasValue)
            {
                var horizon = DateTime.UtcNow.AddHours(_options.EventHorizonHours.Value);
                available = available.Where(b => b.Event?.Starts == null || b.Event.Starts <= horizon).ToList();
            }

            if (beforeFilterCount != available.Count)
            {
                _logger.LogInformation(
                    "Filtrage avancé : {Before} → {After} candidats (MinOdds={Min}, MaxOdds={Max}, Horizon={Horizon}h)",
                    beforeFilterCount, available.Count, _options.MinOdds, _options.MaxOdds, _options.EventHorizonHours);
            }

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

            // log Step="Select"
            _logger.LogInformation(
                "{Available} candidats disponibles (après filtres), {Selected} sélectionnés (cible={Target})",
                available.Count, selected.Count, targetCount);

            return selected;
        }
    }
}
