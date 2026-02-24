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
}
