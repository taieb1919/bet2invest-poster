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

    /// <summary>ROI du tipster source (pour scoring intelligent). Non désérialisé depuis l'API.</summary>
    [JsonIgnore]
    public decimal? TipsterRoi { get; set; }

    /// <summary>Proxy du taux de réussite du tipster (BetsNumber). Non désérialisé depuis l'API.</summary>
    [JsonIgnore]
    public decimal? TipsterWinRate { get; set; }

    /// <summary>Sport le plus parié par le tipster source. Non désérialisé depuis l'API.</summary>
    [JsonIgnore]
    public string? TipsterSport { get; set; }

    /// <summary>Slug/username du tipster source. Non désérialisé depuis l'API.</summary>
    [JsonIgnore]
    public string? TipsterUsername { get; set; }

    /// <summary>
    /// Maps bet team/side to the API designation value used by the web UI.
    /// TEAM1 → home, TEAM2 → away, OVER → over, UNDER → under.
    /// </summary>
    [JsonIgnore]
    public string? DerivedDesignation
    {
        get
        {
            if (!string.IsNullOrEmpty(Team))
                return Team switch
                {
                    "TEAM1" => "home",
                    "TEAM2" => "away",
                    _ => Team.ToLowerInvariant()
                };
            if (!string.IsNullOrEmpty(Side))
                return Side.ToLowerInvariant();
            return null;
        }
    }

    /// <summary>Deduplication key matching HistoryEntry format: matchupId|marketKey|designation.</summary>
    [JsonIgnore]
    public string? DeduplicationKey =>
        Market != null ? $"{Market.MatchupId}{HistoryEntry.KeySeparator}{Market.Key}{HistoryEntry.KeySeparator}{DerivedDesignation?.ToLowerInvariant()}" : null;
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
