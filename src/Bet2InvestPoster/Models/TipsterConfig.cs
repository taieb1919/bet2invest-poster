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
    /// Tipster slug (name identifier) extracted from the URL.
    /// Not deserialized from JSON — set via <see cref="TryExtractSlug(out string?)"/>.
    /// </summary>
    [JsonIgnore]
    public string Id { get; private set; } = string.Empty;

    /// <summary>
    /// Attempts to extract the tipster slug from the URL.
    /// Sets <see cref="Id"/> and returns it via <paramref name="slug"/> on success.
    /// Supports: /tipsters/performance-stats/{slug}
    /// </summary>
    public bool TryExtractSlug(out string? slug)
    {
        slug = null;

        if (string.IsNullOrWhiteSpace(Url))
            return false;

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            return false;

        var lastSegment = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault();

        if (string.IsNullOrWhiteSpace(lastSegment))
            return false;

        Id = lastSegment;
        slug = lastSegment;
        return true;
    }
}
