using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class PostingCycleService : IPostingCycleService
{
    private readonly IHistoryManager _historyManager;
    private readonly ITipsterService _tipsterService;
    private readonly IUpcomingBetsFetcher _upcomingBetsFetcher;
    private readonly IBetSelector _betSelector;
    private readonly IBetPublisher _betPublisher;
    private readonly ILogger<PostingCycleService> _logger;

    public PostingCycleService(
        IHistoryManager historyManager,
        ITipsterService tipsterService,
        IUpcomingBetsFetcher upcomingBetsFetcher,
        IBetSelector betSelector,
        IBetPublisher betPublisher,
        ILogger<PostingCycleService> logger)
    {
        _historyManager      = historyManager;
        _tipsterService      = tipsterService;
        _upcomingBetsFetcher = upcomingBetsFetcher;
        _betSelector         = betSelector;
        _betPublisher        = betPublisher;
        _logger              = logger;
    }

    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Cycle"))
        {
            _logger.LogInformation("Cycle de publication démarré");

            // 1. Purge des entrées > 30 jours (Step="Purge" géré dans HistoryManager)
            await _historyManager.PurgeOldEntriesAsync(ct);

            // 2. Lecture des tipsters (Step="Scrape" géré dans TipsterService)
            var tipsters = await _tipsterService.LoadTipstersAsync(ct);

            // 3. Récupération des paris à venir (Step="Scrape" géré dans UpcomingBetsFetcher)
            var candidates = await _upcomingBetsFetcher.FetchAllAsync(tipsters, ct);

            // 4. Sélection aléatoire (Step="Select" géré dans BetSelector)
            var selected = await _betSelector.SelectAsync(candidates, ct);

            // 5. Publication et enregistrement (Step="Publish" géré dans BetPublisher)
            var published = await _betPublisher.PublishAllAsync(selected, ct);

            _logger.LogInformation(
                "Cycle terminé — {Published} pronostics publiés sur {Candidates} candidats",
                published, candidates.Count);
        }
    }
}
