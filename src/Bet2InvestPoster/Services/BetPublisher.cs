using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Models;
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

    public async Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default)
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
                var request = new BetOrderRequest
                {
                    BankrollId   = _options.BankrollId,
                    SportId      = bet.Sport?.Id ?? 0,
                    EventId      = bet.Event?.Slug,
                    Type         = bet.Type,
                    Team         = bet.Team,
                    Side         = bet.Side,
                    Handicap     = bet.Handicap,
                    Price        = bet.Price,
                    Units        = bet.Units,
                    PeriodNumber = bet.PeriodNumber,
                    Analysis     = bet.Analysis,
                    IsLive       = bet.IsLive
                };

                // PublishBetAsync gère le délai 500ms (NFR8) — ne pas rajouter Task.Delay ici
                await _client.PublishBetAsync(request, ct);

                var description = bet.Event != null
                    ? $"{bet.Event.Home} vs {bet.Event.Away}"
                    : $"Bet#{bet.Id}";

                await _historyManager.RecordAsync(new HistoryEntry
                {
                    BetId            = bet.Id,
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
