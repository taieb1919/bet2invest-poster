using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;

namespace Bet2InvestPoster.Tests.Telegram.Formatters;

public class MessageFormatterTests
{
    private readonly MessageFormatter _formatter = new();

    [Fact]
    public void FormatStatus_NoRun_ContainsAucune()
    {
        var state = new ExecutionState(null, null, null, null, null);

        var result = _formatter.FormatStatus(state);

        Assert.Contains("Aucune", result);
        Assert.Contains("—", result);
        Assert.Contains("Non planifié", result);
    }

    [Fact]
    public void FormatStatus_WithSuccess_ContainsSucces()
    {
        var state = new ExecutionState(
            DateTimeOffset.UtcNow,
            true,
            "5 pronostic(s) publiés",
            null,
            true);

        var result = _formatter.FormatStatus(state);

        Assert.Contains("✅ Succès", result);
        Assert.Contains("5 pronostic(s) publiés", result);
        Assert.Contains("UTC", result);
    }

    [Fact]
    public void FormatStatus_WithFailure_ContainsEchec()
    {
        var state = new ExecutionState(
            DateTimeOffset.UtcNow,
            false,
            "API indisponible",
            null,
            false);

        var result = _formatter.FormatStatus(state);

        Assert.Contains("❌ Échec", result);
        Assert.Contains("API indisponible", result);
    }

    [Fact]
    public void FormatStatus_WithNextRun_ContainsNextRunDate()
    {
        var nextRun = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var state = new ExecutionState(null, null, null, nextRun, null);

        var result = _formatter.FormatStatus(state);

        Assert.DoesNotContain("Non planifié", result);
        Assert.Contains("2026-03-01 08:00:00 UTC", result);
    }

    [Fact]
    public void FormatStatus_ContainsSystemHeader()
    {
        var state = new ExecutionState(null, null, null, null, null);

        var result = _formatter.FormatStatus(state);

        Assert.Contains("📊 État du système", result);
    }

    // --- FormatHistory ---

    [Fact]
    public void FormatHistory_WithEntries_ContainsHeader()
    {
        var entries = new List<HistoryEntry>
        {
            new() { BetId = 1, MatchupId = "1", MarketKey = "m", PublishedAt = new DateTime(2026, 2, 25, 14, 30, 0, DateTimeKind.Utc), MatchDescription = "Arsenal vs Chelsea" }
        };

        var result = _formatter.FormatHistory(entries);

        Assert.Contains("📋 Historique", result);
        Assert.Contains("Arsenal vs Chelsea", result);
        Assert.Contains("2026-02-25", result);
    }

    [Fact]
    public void FormatHistory_EmptyList_ReturnsAucuneMessage()
    {
        var result = _formatter.FormatHistory([]);

        Assert.Equal("📭 Aucune publication dans l'historique.", result);
    }

    [Fact]
    public void FormatHistory_GroupsByDate()
    {
        var entries = new List<HistoryEntry>
        {
            new() { BetId = 1, MatchupId = "1", MarketKey = "m", PublishedAt = new DateTime(2026, 2, 25, 14, 30, 0, DateTimeKind.Utc), MatchDescription = "Match A" },
            new() { BetId = 2, MatchupId = "2", MarketKey = "m", PublishedAt = new DateTime(2026, 2, 24, 8, 15, 0, DateTimeKind.Utc), MatchDescription = "Match B" }
        };

        var result = _formatter.FormatHistory(entries);

        Assert.Contains("📅 2026-02-25", result);
        Assert.Contains("📅 2026-02-24", result);
        Assert.Contains("Match A", result);
        Assert.Contains("Match B", result);
    }

    [Fact]
    public void FormatHistory_WhenNoMatchDescription_UsesBetId()
    {
        var entries = new List<HistoryEntry>
        {
            new() { BetId = 42, MatchupId = "1", MarketKey = "m", PublishedAt = DateTime.UtcNow }
        };

        var result = _formatter.FormatHistory(entries);

        Assert.Contains("betId: 42", result);
    }

    // --- FormatTipsters ---

    [Fact]
    public void FormatTipsters_EmptyList_ReturnsAucunTipsterMessage()
    {
        var result = _formatter.FormatTipsters([]);

        Assert.Equal("📭 Aucun tipster configuré. Utilisez /tipsters add <lien> pour en ajouter.", result);
    }

    [Fact]
    public void FormatTipsters_WithOneTipster_ContainsNameAndUrl()
    {
        var tipsters = new List<TipsterConfig>
        {
            new() { Name = "NG1", Url = "https://bet2invest.com/tipsters/performance-stats/NG1" }
        };

        var result = _formatter.FormatTipsters(tipsters);

        Assert.Contains("NG1", result);
        Assert.Contains("https://bet2invest.com/tipsters/performance-stats/NG1", result);
        Assert.Contains("Total : 1 tipster", result);
    }

    [Fact]
    public void FormatTipsters_WithMultipleTipsters_ContainsAllAndTotal()
    {
        var tipsters = new List<TipsterConfig>
        {
            new() { Name = "NG1", Url = "https://bet2invest.com/tipsters/performance-stats/NG1" },
            new() { Name = "Edge Analytics", Url = "https://bet2invest.com/tipsters/performance-stats/Edge_Analytics" },
            new() { Name = "ProTips", Url = "https://bet2invest.com/tipsters/performance-stats/ProTips" }
        };

        var result = _formatter.FormatTipsters(tipsters);

        Assert.Contains("📋 Tipsters configurés", result);
        Assert.Contains("1. NG1", result);
        Assert.Contains("2. Edge Analytics", result);
        Assert.Contains("3. ProTips", result);
        Assert.Contains("(free)", result);
        Assert.Contains("Total : 3 tipsters", result);
    }

    [Fact]
    public void FormatTipsters_WithOneTipster_UseSingular()
    {
        var tipsters = new List<TipsterConfig>
        {
            new() { Name = "NG1", Url = "https://bet2invest.com/tipsters/performance-stats/NG1" }
        };

        var result = _formatter.FormatTipsters(tipsters);

        Assert.Contains("1 tipster", result);
        Assert.DoesNotContain("1 tipsters", result);
    }

    [Fact]
    public void FormatTipsters_WithExcludedMarkets_DisplaysExclusions()
    {
        var tipsters = new List<TipsterConfig>
        {
            new() { Name = "Tipingmaster", Url = "https://bet2invest.com/tipsters/performance-stats/Tipingmaster", ExcludedMarkets = ["s;0;ou;2.5"] },
            new() { Name = "NG1", Url = "https://bet2invest.com/tipsters/performance-stats/NG1" }
        };

        var result = _formatter.FormatTipsters(tipsters);

        Assert.Contains("🚫 Marchés exclus : s;0;ou;2.5", result);
        // NG1 sans exclusion ne doit pas afficher la ligne
        Assert.DoesNotContain("NG1\n   🚫", result);
    }

    // --- FormatMyStats ---

    [Fact]
    public void FormatMyStats_DisplaysRealApiStats()
    {
        var stats = new UserStats
        {
            General = new UserStatsGeneral
            {
                BetsNumber = 316,
                SettledBetsNumber = 316,
                Roi = -0.92m,
                Profit = -2.90m,
                AveragePrice = 1.95m,
                AverageBetMax = 4100m,
                Clv = 0.32m,
                MostBetSport = "soccer",
                MostBetType = "SPREAD",
                MaxDrawdown = -19.97m,
                FlatStakeProfit = 0.12m,
                FlatRoi = 0.04m
            },
            BettingSummary = new UserStatsBettingSummary
            {
                Won = 146,
                HalfWon = 5,
                Lost = 146,
                HalfLost = 4,
                Refunded = 15
            },
            Bets = new UserStatsBets { PendingNumber = 3 }
        };

        var result = _formatter.FormatMyStats(stats);

        Assert.Contains("📊 Mes statistiques (bet2invest)", result);
        Assert.Contains("Paris total : 316", result);
        Assert.Contains("Profit : -2.90u", result);
        Assert.Contains("ROI : -0.92%", result);
        Assert.Contains("Cote moyenne : 1.95", result);
        Assert.Contains("CLV : +0.32%", result);
        Assert.Contains("Gagnés : 146", result);
        Assert.Contains("Gagnés à moitié : 5", result);
        Assert.Contains("Perdus : 146", result);
        Assert.Contains("Perdus à moitié : 4", result);
        Assert.Contains("Remboursés : 15", result);
        Assert.Contains("En attente : 3", result);
        Assert.Contains("Handicap", result);
        Assert.Contains("soccer", result);
    }
}
