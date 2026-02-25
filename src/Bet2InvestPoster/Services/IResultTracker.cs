namespace Bet2InvestPoster.Services;

public interface IResultTracker
{
    /// <summary>
    /// Vérifie les résultats (won/lost) des pronostics publiés dans les 7 derniers jours.
    /// Met à jour history.json pour les entrées dont le résultat est maintenant disponible.
    /// Les entrées sans résultat restent en "pending" pour re-vérification au prochain cycle.
    /// </summary>
    Task TrackResultsAsync(CancellationToken ct = default);
}
