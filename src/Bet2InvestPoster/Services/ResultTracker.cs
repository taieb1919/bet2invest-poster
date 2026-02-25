using Bet2InvestPoster.Models;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class ResultTracker : IResultTracker
{
    private readonly IHistoryManager _historyManager;
    private readonly ITipsterService _tipsterService;
    private readonly IExtendedBet2InvestClient _client;
    private readonly ILogger<ResultTracker> _logger;
    private readonly TimeProvider _timeProvider;

    private const int TrackingWindowDays = 7;

    public ResultTracker(
        IHistoryManager historyManager,
        ITipsterService tipsterService,
        IExtendedBet2InvestClient client,
        ILogger<ResultTracker> logger,
        TimeProvider? timeProvider = null)
    {
        _historyManager = historyManager;
        _tipsterService = tipsterService;
        _client = client;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task TrackResultsAsync(CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Report"))
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = now.AddDays(-TrackingWindowDays);

            // Charger toutes les entrées récentes et filtrer celles à vérifier
            var allEntries = await _historyManager.GetRecentEntriesAsync(int.MaxValue, ct);
            var toTrack = allEntries
                .Where(e => e.PublishedAt >= cutoff
                    && (e.Result == null || e.Result == "pending"))
                .ToList();

            if (toTrack.Count == 0)
            {
                _logger.LogInformation(
                    "Report — aucune entrée à vérifier dans les {Days} derniers jours", TrackingWindowDays);
                return;
            }

            _logger.LogInformation(
                "Report — {Count} entrée(s) à vérifier dans les {Days} derniers jours",
                toTrack.Count, TrackingWindowDays);

            // Charger et résoudre les tipsters pour obtenir leurs NumericId
            var tipsters = await _tipsterService.LoadTipstersAsync(ct);
            await _client.ResolveTipsterIdsAsync(tipsters, ct);

            var tipsterByName = tipsters
                .Where(t => t.NumericId > 0 && !string.IsNullOrEmpty(t.Id))
                .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

            // Grouper les entrées par tipsterName
            var grouped = toTrack
                .Where(e => !string.IsNullOrEmpty(e.TipsterName))
                .GroupBy(e => e.TipsterName!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var updatedEntries = new List<HistoryEntry>();
            int resolvedCount = 0;

            foreach (var group in grouped)
            {
                if (!tipsterByName.TryGetValue(group.Key, out var tipsterConfig))
                {
                    _logger.LogWarning(
                        "Report — tipster '{Name}' non trouvé dans la config, entrées ignorées", group.Key);
                    // Marquer en pending pour re-vérification
                    foreach (var entry in group)
                    {
                        entry.Result = "pending";
                        updatedEntries.Add(entry);
                    }
                    continue;
                }

                try
                {
                    var settledBets = await _client.GetSettledBetsForTipsterAsync(
                        tipsterConfig.NumericId, cutoff, now, ct);

                    var settledById = settledBets.ToDictionary(b => b.Id);

                    foreach (var entry in group)
                    {
                        if (settledById.TryGetValue(entry.BetId, out var settled))
                        {
                            var mapped = MapState(settled.State);
                            if (mapped != null)
                            {
                                entry.Result = mapped;
                                resolvedCount++;
                            }
                            else
                            {
                                entry.Result = "pending";
                            }
                        }
                        else
                        {
                            entry.Result = "pending";
                        }
                        updatedEntries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Report — échec de récupération des résultats pour tipster '{Name}', entrées maintenues en pending",
                        group.Key);
                    foreach (var entry in group)
                    {
                        entry.Result = "pending";
                        updatedEntries.Add(entry);
                    }
                }
            }

            // Entrées sans TipsterName : marquer pending
            var withoutTipster = toTrack.Where(e => string.IsNullOrEmpty(e.TipsterName)).ToList();
            foreach (var entry in withoutTipster)
            {
                entry.Result = "pending";
                updatedEntries.Add(entry);
            }

            if (updatedEntries.Count > 0)
                await _historyManager.UpdateEntriesAsync(updatedEntries, ct);

            var pendingCount = updatedEntries.Count(e => e.Result == "pending");

            _logger.LogInformation(
                "Report — vérification terminée : {Verified} vérifiée(s), {Resolved} résolue(s), {Pending} en attente",
                updatedEntries.Count, resolvedCount, pendingCount);
        }
    }

    private static string? MapState(string state) => state.ToUpperInvariant() switch
    {
        "WON" or "HALF_WON" => "won",
        "LOST" or "HALF_LOST" => "lost",
        "REFUNDED" or "CANCELLED" => "void",
        _ => null
    };
}
