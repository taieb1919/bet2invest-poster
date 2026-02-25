using Bet2InvestPoster.Models;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Reads tipster configurations from tipsters.json.
/// Re-reads the file on every call (hot-editable, no cache).
/// </summary>
public interface ITipsterService
{
    /// <summary>
    /// Loads and validates tipsters from tipsters.json.
    /// Invalid entries (empty URL/name, non-extractable ID) are excluded.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Valid tipster configurations with extracted IDs.</returns>
    /// <exception cref="FileNotFoundException">tipsters.json not found.</exception>
    /// <exception cref="System.Text.Json.JsonException">Invalid JSON content.</exception>
    Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a tipster to tipsters.json using atomic write (write-to-temp + rename).
    /// </summary>
    /// <param name="url">URL of the tipster to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The added TipsterConfig with extracted slug.</returns>
    /// <exception cref="ArgumentException">URL is invalid or slug cannot be extracted.</exception>
    /// <exception cref="InvalidOperationException">Tipster already exists in the list.</exception>
    Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Removes a tipster from tipsters.json using atomic write (write-to-temp + rename).
    /// </summary>
    /// <param name="url">URL of the tipster to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the tipster was removed; false if not found.</returns>
    Task<bool> RemoveTipsterAsync(string url, CancellationToken ct = default);
}
