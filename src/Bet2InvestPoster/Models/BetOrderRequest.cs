using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

/// <summary>
/// Body for POST /v1/bet-orders — publishes a pending bet.
/// </summary>
public class BetOrderRequest
{
    [JsonPropertyName("bankrollId")]
    public string BankrollId { get; set; } = string.Empty;

    [JsonPropertyName("sportId")]
    public int SportId { get; set; }

    [JsonPropertyName("eventId")]
    public string? EventId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>TEAM1 (home), TEAM2 (away), or null.</summary>
    [JsonPropertyName("team")]
    public string? Team { get; set; }

    /// <summary>OVER, UNDER, or null.</summary>
    [JsonPropertyName("side")]
    public string? Side { get; set; }

    [JsonPropertyName("handicap")]
    public decimal? Handicap { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("units")]
    public decimal Units { get; set; }

    [JsonPropertyName("periodNumber")]
    public int PeriodNumber { get; set; }

    [JsonPropertyName("analysis")]
    public string? Analysis { get; set; }

    [JsonPropertyName("isLive")]
    public bool IsLive { get; set; }
}
