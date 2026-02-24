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
}
