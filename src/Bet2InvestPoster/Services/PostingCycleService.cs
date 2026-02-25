using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly PosterOptions _options;
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
        IOptions<PosterOptions> posterOptions,
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
        _options              = posterOptions.Value;
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

                // 2b. Résolution des IDs numériques via l'API /tipsters (auth implicite)
                await _client.ResolveTipsterIdsAsync(tipsters, ct);
                _executionStateService.SetApiConnectionStatus(true);

                // 3. Récupération des paris à venir (Step="Scrape" géré dans UpcomingBetsFetcher)
                var candidates = await _upcomingBetsFetcher.FetchAllAsync(tipsters, ct);

                // 4. Sélection aléatoire avec filtrage avancé (Step="Select" géré dans BetSelector)
                var selected = await _betSelector.SelectAsync(candidates, ct);

                // AC#4 : détecter zéro candidats après filtrage avancé
                // candidates.Count > 0 évite de blâmer les filtres si la cause est l'absence de bets ou la déduplication
                if (selected.Count == 0 && candidates.Count > 0 && HasActiveFilters())
                {
                    var filterDetails = BuildFilterDetails();
                    _logger.LogWarning(
                        "Aucun pronostic ne correspond aux critères de filtrage — {FilterDetails}", filterDetails);
                    await _notificationService.NotifyNoFilteredCandidatesAsync(filterDetails, ct);
                    return;
                }

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
                if (ex is Exceptions.Bet2InvestApiException)
                    _executionStateService.SetApiConnectionStatus(false);
                _executionStateService.RecordFailure(sanitizedReason);
                await _notificationService.NotifyFailureAsync(sanitizedReason, ct);

                throw; // Re-throw pour que Polly (Epic 5) puisse retenter
            }
        }
    }

    private bool HasActiveFilters()
        => _options.MinOdds.HasValue || _options.MaxOdds.HasValue || _options.EventHorizonHours.HasValue;

    private string BuildFilterDetails()
    {
        var parts = new List<string>();
        if (_options.MinOdds.HasValue && _options.MaxOdds.HasValue)
            parts.Add($"cotes: {_options.MinOdds.Value:F2}-{_options.MaxOdds.Value:F2}");
        else if (_options.MinOdds.HasValue)
            parts.Add($"cotes min: {_options.MinOdds.Value:F2}");
        else if (_options.MaxOdds.HasValue)
            parts.Add($"cotes max: {_options.MaxOdds.Value:F2}");
        if (_options.EventHorizonHours.HasValue)
            parts.Add($"horizon: {_options.EventHorizonHours.Value}h");

        return parts.Count > 0 ? string.Join(", ", parts) : "aucun filtre";
    }
}
