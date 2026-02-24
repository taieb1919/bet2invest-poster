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
}
