using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

/// <summary>
/// Represents a tipster entry from tipsters.json.
/// </summary>
public class TipsterConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Numeric tipster ID extracted from the URL.
    /// Not deserialized from JSON — set via <see cref="TryExtractId(out int)"/>.
    /// </summary>
    [JsonIgnore]
    public int Id { get; private set; }

    /// <summary>
    /// Attempts to extract the numeric tipster ID from the URL.
    /// Sets <see cref="Id"/> and returns it via <paramref name="id"/> on success.
    /// Supports: /tipster/{id} and /fr/tipster/{id}. Only positive IDs are accepted.
    /// </summary>
    public bool TryExtractId(out int id)
    {
        id = 0;

        if (string.IsNullOrWhiteSpace(Url))
            return false;

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            return false;

        var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
        for (var i = segments.Length - 1; i >= 1; i--)
        {
            if (int.TryParse(segments[i], out var parsed) &&
                parsed > 0 &&
                segments[i - 1].Equals("tipster", StringComparison.OrdinalIgnoreCase))
            {
                Id = parsed;
                id = parsed;
                return true;
            }
        }

        return false;
    }
}
