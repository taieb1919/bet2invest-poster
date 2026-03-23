using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

/// <summary>
/// Statistics from GET /v1/statistics/{userId} — mirrors the bet2invest dashboard.
/// </summary>
public class UserStats
{
    [JsonPropertyName("general")]
    public UserStatsGeneral General { get; set; } = new();

    [JsonPropertyName("bettingSummary")]
    public UserStatsBettingSummary BettingSummary { get; set; } = new();

    [JsonPropertyName("bets")]
    public UserStatsBets? Bets { get; set; }
}

public class UserStatsGeneral
{
    [JsonPropertyName("betsNumber")]
    public int BetsNumber { get; set; }

    [JsonPropertyName("settledBetsNumber")]
    public int SettledBetsNumber { get; set; }

    [JsonPropertyName("roi")]
    public decimal Roi { get; set; }

    [JsonPropertyName("profit")]
    public decimal Profit { get; set; }

    [JsonPropertyName("averagePrice")]
    public decimal AveragePrice { get; set; }

    [JsonPropertyName("averageBetMax")]
    public decimal AverageBetMax { get; set; }

    [JsonPropertyName("clv")]
    public decimal Clv { get; set; }

    [JsonPropertyName("mostBetSport")]
    public string MostBetSport { get; set; } = string.Empty;

    [JsonPropertyName("mostBetType")]
    public string MostBetType { get; set; } = string.Empty;

    [JsonPropertyName("maxDrawdown")]
    public decimal MaxDrawdown { get; set; }

    [JsonPropertyName("flatStakeProfit")]
    public decimal FlatStakeProfit { get; set; }

    [JsonPropertyName("flatRoi")]
    public decimal FlatRoi { get; set; }
}

public class UserStatsBettingSummary
{
    [JsonPropertyName("won")]
    public int Won { get; set; }

    [JsonPropertyName("halfWon")]
    public int HalfWon { get; set; }

    [JsonPropertyName("lost")]
    public int Lost { get; set; }

    [JsonPropertyName("halfLost")]
    public int HalfLost { get; set; }

    [JsonPropertyName("refunded")]
    public int Refunded { get; set; }
}

public class UserStatsBets
{
    [JsonPropertyName("pendingNumber")]
    public int PendingNumber { get; set; }
}
