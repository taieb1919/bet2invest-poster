using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

public interface IHistoryManager
{
    /// <summary>
    /// Loads all deduplication keys (matchupId|marketKey|designation) already published.
    /// Returns an empty HashSet if the file does not exist.
    /// </summary>
    Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically appends an entry to history.json using write-to-temp + rename (NFR4).
    /// Creates history.json if it does not exist.
    /// </summary>
    Task RecordAsync(HistoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes entries older than 30 days from history.json and rewrites atomically.
    /// Logs purged count with Step="Purge". No-op if file does not exist.
    /// </summary>
    Task PurgeOldEntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the <paramref name="count"/> most recent history entries sorted by PublishedAt descending.
    /// Returns an empty list if the file does not exist.
    /// </summary>
    Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// Met à jour les entrées existantes dans history.json avec les valeurs fournies (match par BetId).
    /// Les entrées non présentes dans <paramref name="updatedEntries"/> sont conservées sans modification.
    /// Écriture atomique (write-to-temp + rename).
    /// </summary>
    Task UpdateEntriesAsync(List<HistoryEntry> updatedEntries, CancellationToken ct = default);

    /// <summary>
    /// Retourne toutes les entrées publiées depuis <paramref name="since"/>, ordonnées par date décroissante.
    /// Retourne une liste vide si le fichier n'existe pas.
    /// </summary>
    Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct = default);
}
