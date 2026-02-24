using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

/// <summary>
/// Body for POST /v1/bankrolls/{bankrollId}/bets — publishes a bet like the web UI.
/// </summary>
public class BetOrderRequest
{
    [JsonPropertyName("sportId")]
    public int SportId { get; set; }

    [JsonPropertyName("matchupId")]
    public long MatchupId { get; set; }

    [JsonPropertyName("marketKey")]
    public string MarketKey { get; set; } = string.Empty;

    [JsonPropertyName("designation")]
    public string? Designation { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "straight";

    [JsonPropertyName("units")]
    public decimal Units { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("points")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Points { get; set; }

    [JsonPropertyName("invisible")]
    public bool Invisible { get; set; }
}
