namespace Bet2InvestPoster.Services;

public interface INotificationService
{
    Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default);

    Task NotifyFailureAsync(string reason, CancellationToken ct = default);

    // FR18 — notification après épuisement de toutes les tentatives Polly
    Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default);

    // FR36 — notification quand zéro candidats après filtrage avancé
    Task NotifyNoFilteredCandidatesAsync(string filterDetails, CancellationToken ct = default);

    // Story 10.1 — envoi d'un message générique (utilisé par l'onboarding)
    Task SendMessageAsync(string message, CancellationToken ct = default);
}
