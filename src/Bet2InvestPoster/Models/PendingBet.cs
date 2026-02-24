using System.Text.Json.Serialization;
using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Models;

/// <summary>
/// Extends SettledBet with the market object present in pending bets from /v1/statistics/{id}.
/// The market contains matchupId and key needed for publishing via /v1/bet-orders.
/// </summary>
public class PendingBet : SettledBet
{
    [JsonPropertyName("market")]
    public PendingBetMarket? Market { get; set; }
}

public class PendingBetMarket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("matchupId")]
    public string MatchupId { get; set; } = string.Empty;

    [JsonPropertyName("prices")]
    public List<MarketPrice> Prices { get; set; } = [];
}

public class MarketPrice
{
    [JsonPropertyName("designation")]
    public string Designation { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("points")]
    public decimal? Points { get; set; }
}
