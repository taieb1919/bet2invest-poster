namespace Bet2InvestPoster.Services;

public interface IOnboardingService
{
    /// <summary>
    /// Envoie le message d'onboarding si c'est le premier démarrage (history vide).
    /// Ne lève jamais d'exception — les erreurs sont loggées et ignorées.
    /// </summary>
    Task TrySendOnboardingAsync(CancellationToken ct = default);
}
