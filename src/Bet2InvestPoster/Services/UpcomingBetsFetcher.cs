using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Iterates over each configured tipster, calls the API to retrieve upcoming bets,
/// filters out inaccessible (pro) tipsters, and returns the aggregated candidate pool.
/// </summary>
public class UpcomingBetsFetcher : IUpcomingBetsFetcher
{
    private readonly IExtendedBet2InvestClient _client;
    private readonly ILogger<UpcomingBetsFetcher> _logger;

    public UpcomingBetsFetcher(IExtendedBet2InvestClient client, ILogger<UpcomingBetsFetcher> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<PendingBet>> FetchAllAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Scrape"))
        {
            var allBets = new List<PendingBet>();

            foreach (var tipster in tipsters)
            {
                if (tipster.NumericId == 0)
                {
                    _logger.LogWarning("Tipster {Name} ignoré : ID numérique non résolu", tipster.Name);
                    continue;
                }

                try
                {
                    // NFR8: 500ms delay is handled inside ExtendedBet2InvestClient.GetUpcomingBetsAsync.
                    var (canSeeBets, bets) = await _client.GetUpcomingBetsAsync(tipster.NumericId, ct);

                    if (!canSeeBets)
                    {
                        // FR6 level-2: tipster is pro or access is restricted — skip silently with warning.
                        _logger.LogWarning(
                            "Tipster {Name} (id={Id}) ignoré : canSeeBets=false (tipster pro ou accès restreint)",
                            tipster.Name, tipster.Id);
                        continue;
                    }

                    if (bets.Count == 0)
                    {
                        _logger.LogWarning(
                            "Aucun pari à venir pour tipster {Name} (id={Id})",
                            tipster.Name, tipster.Id);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "{Count} paris à venir pour tipster {Name} (id={Id})",
                            bets.Count, tipster.Name, tipster.Id);

                        // Enrichir chaque pari avec les métadonnées du tipster source (scoring intelligent story 11.2)
                        foreach (var bet in bets)
                        {
                            bet.TipsterUsername = tipster.Name;
                            bet.TipsterRoi = tipster.Roi;
                            bet.TipsterWinRate = tipster.BetsNumber.HasValue ? (decimal)tipster.BetsNumber.Value : null;
                            bet.TipsterSport = tipster.MostBetSport;
                        }

                        allBets.AddRange(bets);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Un tipster en erreur ne doit pas empêcher les suivants d'être fetchés
                    _logger.LogWarning(ex,
                        "Tipster {Name} (id={Id}) échec du fetch — continue avec les suivants",
                        tipster.Name, tipster.Id);
                }
            }

            _logger.LogInformation(
                "{Total} paris candidats agrégés depuis {TipsterCount} tipster(s)",
                allBets.Count, tipsters.Count);

            return allBets;
        }
    }
}
