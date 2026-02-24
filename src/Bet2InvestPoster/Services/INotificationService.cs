namespace Bet2InvestPoster.Services;

public interface INotificationService
{
    Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default);

    // TODO: FR18 — ajouter retryCount parameter quand Polly (Epic 5) sera implémenté
    Task NotifyFailureAsync(string reason, CancellationToken ct = default);
}
