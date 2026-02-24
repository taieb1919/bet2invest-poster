using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Exceptions;
using Bet2InvestPoster.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class BetPublisher : IBetPublisher
{
    private readonly IExtendedBet2InvestClient _client;
    private readonly IHistoryManager _historyManager;
    private readonly PosterOptions _options;
    private readonly ILogger<BetPublisher> _logger;

    public BetPublisher(
        IExtendedBet2InvestClient client,
        IHistoryManager historyManager,
        IOptions<PosterOptions> options,
        ILogger<BetPublisher> logger)
    {
        _client = client;
        _historyManager = historyManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PublishAllAsync(List<PendingBet> selected, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Publish"))
        {
            if (selected.Count == 0)
            {
                _logger.LogInformation("0 pronostics sélectionnés — aucune publication");
                return 0;
            }

            int published = 0;
            foreach (var bet in selected)
            {
                if (bet.Market == null)
                {
                    _logger.LogWarning("Bet#{BetId} ignoré : market absent", bet.Id);
                    continue;
                }

                var designation = bet.DerivedDesignation;

                // Lookup current market price matching the designation
                var marketPrice = bet.Market.Prices
                    .FirstOrDefault(p => string.Equals(p.Designation, designation, StringComparison.OrdinalIgnoreCase));

                if (marketPrice == null)
                {
                    _logger.LogWarning(
                        "Bet#{BetId} ignoré : prix marché introuvable pour designation={Designation}",
                        bet.Id, designation);
                    continue;
                }

                if (bet.Sport == null)
                {
                    _logger.LogWarning("Bet#{BetId} ignoré : sport absent (SportId requis par l'API)", bet.Id);
                    continue;
                }

                if (!long.TryParse(bet.Market.MatchupId, out var matchupId))
                {
                    _logger.LogWarning("Bet#{BetId} ignoré : MatchupId invalide '{MatchupId}'", bet.Id, bet.Market.MatchupId);
                    continue;
                }

                var request = new BetOrderRequest
                {
                    SportId      = bet.Sport.Id,
                    MatchupId    = matchupId,
                    MarketKey    = bet.Market.Key,
                    Designation  = designation,
                    Type         = "straight",
                    // Toujours 1 unit : on ne suit pas le staking du tipster — choix produit pour limiter l'exposition
                    Units        = 1m,
                    Price        = marketPrice.Price,
                    Points       = marketPrice.Points,
                    Invisible    = false
                };

                // BankrollId validé comme int au démarrage (Program.cs fast-fail)
                var bankrollId = int.Parse(_options.BankrollId);
                try
                {
                    // PublishBetAsync gère le délai 500ms (NFR8) — ne pas rajouter Task.Delay ici
                    await _client.PublishBetAsync(bankrollId, request, ct);
                }
                catch (PublishException pex)
                {
                    _logger.LogWarning("Bet#{BetId} publication échouée ({StatusCode}), skip", bet.Id, pex.HttpStatusCode);
                    continue;
                }

                var description = bet.Event != null
                    ? $"{bet.Event.Home} vs {bet.Event.Away}"
                    : $"Bet#{bet.Id}";

                await _historyManager.RecordAsync(new HistoryEntry
                {
                    BetId            = bet.Id,
                    MatchupId        = bet.Market.MatchupId,
                    MarketKey        = bet.Market.Key,
                    Designation      = designation,
                    PublishedAt      = DateTime.UtcNow,
                    MatchDescription = description,
                    TipsterUrl       = null
                }, ct);

                published++;
            }

            _logger.LogInformation(
                "{Published}/{Total} pronostics publiés avec succès",
                published, selected.Count);

            return published;
        }
    }

}
