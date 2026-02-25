using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Models;

public record CycleResult
{
    public int ScrapedCount { get; init; }
    public int FilteredCount { get; init; }

    /// <summary>
    /// Dérivé de PublishedBets.Count. Toujours cohérent avec la liste effective.
    /// </summary>
    public int PublishedCount => PublishedBets.Count;

    /// <summary>
    /// Vrai si des filtres cotes/horaire étaient actifs lors du cycle (indépendant de la déduplication).
    /// Positionné explicitement par PostingCycleService selon PosterOptions.
    /// </summary>
    public bool FiltersWereActive { get; init; }

    /// <summary>
    /// Vrai si des filtres cotes/horaire étaient actifs lors du cycle.
    /// </summary>
    public bool HasActiveFilters => FiltersWereActive;

    /// <summary>
    /// Liste des pronostics effectivement publiés lors de ce cycle.
    /// Vide par défaut pour rétrocompatibilité.
    /// </summary>
    public IReadOnlyList<PendingBet> PublishedBets { get; init; } = Array.Empty<PendingBet>();
}
