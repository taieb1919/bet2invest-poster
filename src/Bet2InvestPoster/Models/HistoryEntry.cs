using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

public class HistoryEntry
{
    /// <summary>Separator used in deduplication keys (matchupId|marketKey|designation).</summary>
    public const string KeySeparator = "|";

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

    // ─── Champs résultat (Story 12.1) — nullable pour rétrocompatibilité ──────

    /// <summary>Résultat du pari : "won", "lost", "pending", null = non vérifié.</summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>Cote au moment de la publication.</summary>
    [JsonPropertyName("odds")]
    public decimal? Odds { get; set; }

    /// <summary>Nom du sport (ex: "Football", "Tennis").</summary>
    [JsonPropertyName("sport")]
    public string? Sport { get; set; }

    /// <summary>Slug/username du tipster source.</summary>
    [JsonPropertyName("tipsterName")]
    public string? TipsterName { get; set; }

    /// <summary>Unique key for deduplication: matchupId|marketKey|designation.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DeduplicationKey => $"{MatchupId}{KeySeparator}{MarketKey}{KeySeparator}{Designation?.ToLowerInvariant()}";
}
