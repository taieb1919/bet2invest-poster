using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

public class BankrollBet
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("matchupId")]
    public string MatchupId { get; set; } = string.Empty;

    [JsonPropertyName("marketKey")]
    public string MarketKey { get; set; } = string.Empty;

    [JsonPropertyName("designation")]
    public string? Designation { get; set; }

    [JsonIgnore]
    public string DeduplicationKey =>
        $"{MatchupId}{HistoryEntry.KeySeparator}{MarketKey}{HistoryEntry.KeySeparator}{Designation?.ToLowerInvariant()}";
}
