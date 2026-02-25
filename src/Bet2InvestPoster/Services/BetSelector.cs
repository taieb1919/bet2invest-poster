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
    private readonly TimeProvider _timeProvider;

    public BetSelector(IHistoryManager historyManager, IOptions<PosterOptions> posterOptions, ILogger<BetSelector> logger, TimeProvider timeProvider)
    {
        _historyManager = historyManager;
        _options = posterOptions.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<SelectionResult> SelectAsync(List<PendingBet> candidates, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Select"))
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;

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
                var horizon = now.AddHours(_options.EventHorizonHours.Value);
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

            var isIntelligent = string.Equals(_options.SelectionMode, "intelligent", StringComparison.OrdinalIgnoreCase);
            var isRandom = string.Equals(_options.SelectionMode, "random", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Mode de sélection : {Mode}", _options.SelectionMode);

            if (!isIntelligent && !isRandom)
                _logger.LogWarning("SelectionMode '{Mode}' inconnu, fallback sur random", _options.SelectionMode);

            if (available.Count <= targetCount)
            {
                selected = available;
            }
            else if (isIntelligent)
            {
                // Mode intelligent : trier par score descendant, prendre les N meilleurs
                selected = SelectIntelligent(available, targetCount, now);
            }
            else
            {
                selected = available.OrderBy(_ => Random.Shared.Next()).Take(targetCount).ToList();
            }

            // log Step="Select"
            _logger.LogInformation(
                "{Available} candidats disponibles (après filtres), {Selected} sélectionnés (cible={Target})",
                available.Count, selected.Count, targetCount);

            return new SelectionResult { FilteredCount = available.Count, Selected = selected };
        }
    }

    private List<PendingBet> SelectIntelligent(List<PendingBet> available, int targetCount, DateTime now)
    {
        // Précalculer les stats de normalisation sur l'ensemble des candidats disponibles
        var rois = available.Where(b => b.TipsterRoi.HasValue).Select(b => (double)b.TipsterRoi!.Value).ToList();
        var winRates = available.Where(b => b.TipsterWinRate.HasValue).Select(b => (double)b.TipsterWinRate!.Value).ToList();

        double minRoi = rois.Count > 0 ? rois.Min() : 0;
        double maxRoi = rois.Count > 0 ? rois.Max() : 0;
        double minWinRate = winRates.Count > 0 ? winRates.Min() : 0;
        double maxWinRate = winRates.Count > 0 ? winRates.Max() : 0;

        // Compter les sports pour la diversité (utiliser le sport du pari ou le MostBetSport du tipster)
        var sportCounts = available
            .GroupBy(b => b.Sport?.Name ?? b.TipsterSport ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count());

        // Normalisation min-max des scores de sport (Issue #7 : cohérence avec ROI/WinRate)
        var sportRawScores = available
            .Select(b => 1.0 / sportCounts.GetValueOrDefault(b.Sport?.Name ?? b.TipsterSport ?? string.Empty, 1))
            .ToList();
        double minSportScore = sportRawScores.Count > 0 ? sportRawScores.Min() : 0;
        double maxSportScore = sportRawScores.Count > 0 ? sportRawScores.Max() : 0;

        // Calculer l'horizon max en heures pour la fraîcheur
        double maxHours = available
            .Where(b => b.Event?.Starts != null)
            .Select(b => Math.Max(0, (b.Event!.Starts - now).TotalHours))
            .DefaultIfEmpty(0)
            .Max();

        // Scorer et trier
        var scored = available
            .Select(b => (Bet: b, Score: ScoreBet(b, sportCounts, maxHours, minRoi, maxRoi, minWinRate, maxWinRate, minSportScore, maxSportScore, now)))
            .OrderByDescending(x => x.Score)
            .Take(targetCount)
            .ToList();

        // Loguer chaque sélection
        foreach (var (bet, score) in scored)
        {
            var sportKey = bet.Sport?.Name ?? bet.TipsterSport ?? string.Empty;
            var rawSportScore = 1.0 / sportCounts.GetValueOrDefault(sportKey, 1);
            _logger.LogInformation(
                "[Intelligent] {Tipster} score={Score:F3} (roi={RoiScore:F2}, wr={WrScore:F2}, sport={SportScore:F2}, fresh={FreshScore:F2})",
                bet.TipsterUsername ?? "?",
                score,
                bet.TipsterRoi.HasValue ? NormalizeMinMax((double)bet.TipsterRoi.Value, minRoi, maxRoi) : double.NaN,
                bet.TipsterWinRate.HasValue ? NormalizeMinMax((double)bet.TipsterWinRate.Value, minWinRate, maxWinRate) : double.NaN,
                NormalizeMinMax(rawSportScore, minSportScore, maxSportScore),
                ComputeFreshness(bet, maxHours, now));
        }

        return scored.Select(x => x.Bet).ToList();
    }

    private static double ScoreBet(
        PendingBet bet,
        Dictionary<string, int> sportCounts,
        double maxHours,
        double minRoi, double maxRoi,
        double minWinRate, double maxWinRate,
        double minSportScore, double maxSportScore,
        DateTime now)
    {
        double weightedScore = 0;
        double totalWeight = 0;

        // ROI du tipster (40%)
        if (bet.TipsterRoi.HasValue)
        {
            var roiScore = NormalizeMinMax((double)bet.TipsterRoi.Value, minRoi, maxRoi);
            weightedScore += 0.4 * roiScore;
            totalWeight += 0.4;
        }

        // Taux de réussite / WinRate (30%)
        if (bet.TipsterWinRate.HasValue)
        {
            var wrScore = NormalizeMinMax((double)bet.TipsterWinRate.Value, minWinRate, maxWinRate);
            weightedScore += 0.3 * wrScore;
            totalWeight += 0.3;
        }

        // Diversité de sport (20%) — pénaliser les sports surreprésentés, normalisé [0,1]
        {
            var sportKey = bet.Sport?.Name ?? bet.TipsterSport ?? string.Empty;
            var count = sportCounts.GetValueOrDefault(sportKey, 1);
            var rawSportScore = 1.0 / count;
            var sportScore = NormalizeMinMax(rawSportScore, minSportScore, maxSportScore);
            weightedScore += 0.2 * sportScore;
            totalWeight += 0.2;
        }

        // Fraîcheur de l'événement (10%)
        {
            var freshnessScore = ComputeFreshness(bet, maxHours, now);
            weightedScore += 0.1 * freshnessScore;
            totalWeight += 0.1;
        }

        // Redistribuer les poids manquants (AC#4) : normaliser par le poids total disponible
        return totalWeight > 0 ? weightedScore / totalWeight : 0.5;
    }

    private static double NormalizeMinMax(double value, double min, double max)
        => (max == min) ? 0.5 : (value - min) / (max - min);

    private static double ComputeFreshness(PendingBet bet, double maxHours, DateTime now)
    {
        if (bet.Event?.Starts == null || maxHours <= 0)
            return 0.5;

        var hoursUntilStart = Math.Max(0, (bet.Event.Starts - now).TotalHours);
        return Math.Clamp(1.0 - (hoursUntilStart / maxHours), 0.0, 1.0);
    }
}
