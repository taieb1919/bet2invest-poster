namespace Bet2InvestPoster.Models;

public class SelectionResult
{
    /// <summary>
    /// Nombre de candidats après déduplication et filtres (cotes/horaire), avant le tirage aléatoire.
    /// </summary>
    public int FilteredCount { get; init; }

    /// <summary>
    /// Liste des pronostics sélectionnés (après tirage aléatoire ou sélection intelligente).
    /// </summary>
    public IReadOnlyList<PendingBet> Selected { get; init; } = [];
}
