using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class PostingCycleService : IPostingCycleService
{
    private readonly IExtendedBet2InvestClient _client;
    private readonly IHistoryManager _historyManager;
    private readonly ITipsterService _tipsterService;
    private readonly IUpcomingBetsFetcher _upcomingBetsFetcher;
    private readonly IBetSelector _betSelector;
    private readonly IBetPublisher _betPublisher;
    private readonly INotificationService _notificationService;
    private readonly IExecutionStateService _executionStateService;
    private readonly ILogger<PostingCycleService> _logger;

    public PostingCycleService(
        IExtendedBet2InvestClient client,
        IHistoryManager historyManager,
        ITipsterService tipsterService,
        IUpcomingBetsFetcher upcomingBetsFetcher,
        IBetSelector betSelector,
        IBetPublisher betPublisher,
        INotificationService notificationService,
        IExecutionStateService executionStateService,
        ILogger<PostingCycleService> logger)
    {
        _client               = client;
        _historyManager       = historyManager;
        _tipsterService       = tipsterService;
        _upcomingBetsFetcher  = upcomingBetsFetcher;
        _betSelector          = betSelector;
        _betPublisher         = betPublisher;
        _notificationService  = notificationService;
        _executionStateService = executionStateService;
        _logger               = logger;
    }

    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Cycle"))
        {
            _logger.LogInformation("Cycle de publication démarré");

            try
            {
                // 1. Purge des entrées > 30 jours (Step="Purge" géré dans HistoryManager)
                await _historyManager.PurgeOldEntriesAsync(ct);

                // 2. Lecture des tipsters (Step="Scrape" géré dans TipsterService)
                var tipsters = await _tipsterService.LoadTipstersAsync(ct);

                // 2b. Résolution des IDs numériques via l'API /tipsters
                await _client.ResolveTipsterIdsAsync(tipsters, ct);

                // 3. Récupération des paris à venir (Step="Scrape" géré dans UpcomingBetsFetcher)
                var candidates = await _upcomingBetsFetcher.FetchAllAsync(tipsters, ct);

                // 4. Sélection aléatoire (Step="Select" géré dans BetSelector)
                var selected = await _betSelector.SelectAsync(candidates, ct);

                // 5. Publication et enregistrement (Step="Publish" géré dans BetPublisher)
                var published = await _betPublisher.PublishAllAsync(selected, ct);

                _logger.LogInformation(
                    "Cycle terminé — {Published} pronostics publiés sur {Candidates} candidats",
                    published, candidates.Count);

                // 6. Mise à jour état + notification succès (Story 4.3)
                _executionStateService.RecordSuccess(published);
                await _notificationService.NotifySuccessAsync(published, ct);
            }
            catch (Exception ex)
            {
                using (LogContext.PushProperty("Step", "Cycle"))
                {
                    _logger.LogError(ex, "Cycle échoué — {ExceptionType}: {Message}",
                        ex.GetType().Name, ex.Message);
                }

                // Sanitize : utiliser le type d'exception, jamais ex.Message (peut contenir des credentials)
                var sanitizedReason = ex.GetType().Name;
                _executionStateService.RecordFailure(sanitizedReason);
                await _notificationService.NotifyFailureAsync(sanitizedReason, ct);

                throw; // Re-throw pour que Polly (Epic 5) puisse retenter
            }
        }
    }
}
