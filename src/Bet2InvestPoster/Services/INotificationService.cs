namespace Bet2InvestPoster.Services;

public interface INotificationService
{
    Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default);

    Task NotifyFailureAsync(string reason, CancellationToken ct = default);

    // FR18 — notification après épuisement de toutes les tentatives Polly
    Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default);
}
