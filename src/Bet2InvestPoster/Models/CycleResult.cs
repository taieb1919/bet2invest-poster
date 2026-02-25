namespace Bet2InvestPoster.Models;

public record CycleResult
{
    public int ScrapedCount { get; init; }
    public int FilteredCount { get; init; }
    public int PublishedCount { get; init; }

    /// <summary>
    /// Vrai si des filtres cotes/horaire étaient actifs lors du cycle (indépendant de la déduplication).
    /// Positionné explicitement par PostingCycleService selon PosterOptions.
    /// </summary>
    public bool FiltersWereActive { get; init; }

    /// <summary>
    /// Vrai si des filtres cotes/horaire étaient actifs lors du cycle.
    /// </summary>
    public bool HasActiveFilters => FiltersWereActive;
}
