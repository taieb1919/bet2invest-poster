using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

public class HistoryEntry
{
    [JsonPropertyName("betId")]
    public int BetId { get; set; }

    [JsonPropertyName("matchupId")]
    public string MatchupId { get; set; } = string.Empty;

    [JsonPropertyName("marketKey")]
    public string MarketKey { get; set; } = string.Empty;

    [JsonPropertyName("designation")]
    public string? Designation { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTime PublishedAt { get; set; }

    /// <summary>Human-readable match description for log/audit (optional).</summary>
    [JsonPropertyName("matchDescription")]
    public string? MatchDescription { get; set; }

    /// <summary>Tipster URL from which the bet was sourced (optional).</summary>
    [JsonPropertyName("tipsterUrl")]
    public string? TipsterUrl { get; set; }

    /// <summary>Unique key for deduplication: matchupId|marketKey|designation.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DeduplicationKey => $"{MatchupId}|{MarketKey}|{Designation?.ToLowerInvariant()}";
}
