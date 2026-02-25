using Bet2InvestPoster.Models;
using Bet2InvestPoster.Telegram.Formatters;
using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Tests.Telegram.Formatters;

/// <summary>Tests Story 13.1 + 13.2 — FormatCycleSuccess.</summary>
public class MessageFormatterCycleSuccessTests
{
    private readonly MessageFormatter _formatter = new();

    private static PendingBet MakeBet(string home, string away, decimal price, string tipster)
        => new PendingBet
        {
            Id              = 1,
            Price           = price,
            TipsterUsername = tipster,
            Event           = new BetEvent { Home = home, Away = away, Starts = DateTime.UtcNow }
        };

    private static PendingBet MakeBetNoEvent(decimal price, string tipster)
        => new PendingBet { Id = 2, Price = price, TipsterUsername = tipster };

    // ─── Tests Story 13.1 — résumé seul (sans PublishedBets) ─────────────────

    [Fact]
    public void FormatCycleSuccess_StandardCase_ReturnsPublishedOnScraped()
    {
        // AC#1 : sans filtres actifs — 10 bets publiés
        var bets = Enumerable.Range(1, 10).Select(i => MakeBet($"Home{i}", $"Away{i}", 2.00m, "tipster")).ToList();
        var result = new CycleResult { ScrapedCount = 45, FilteredCount = 45, PublishedBets = bets };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.StartsWith("✅ 10 pronostics publiés sur 45 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_WithFilters_ReturnsFilteredFormat()
    {
        // AC#2 : avec filtres actifs (FiltersWereActive = true)
        var bets = Enumerable.Range(1, 10).Select(i => MakeBet($"Home{i}", $"Away{i}", 2.00m, "tipster")).ToList();
        var result = new CycleResult { ScrapedCount = 45, FilteredCount = 32, FiltersWereActive = true, PublishedBets = bets };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.StartsWith("✅ 10/32 filtrés sur 45 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_ZeroScraped_ReturnsWarningMessage()
    {
        // AC#3 : zéro pronostic scrapé
        var result = new CycleResult { ScrapedCount = 0, FilteredCount = 0 };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("⚠️ Aucun pronostic disponible chez les tipsters configurés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_FilteredEqualsScraped_UsesStandardFormat()
    {
        // Quand FilteredCount == ScrapedCount : pas de filtrage actif → format standard
        var bets = Enumerable.Range(1, 5).Select(i => MakeBet($"Home{i}", $"Away{i}", 2.00m, "tipster")).ToList();
        var result = new CycleResult { ScrapedCount = 20, FilteredCount = 20, PublishedBets = bets };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.StartsWith("✅ 5 pronostics publiés sur 20 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_ZeroPublished_WithFilters_ShowsWarningIcon()
    {
        // Cas edge : filtres actifs mais 0 publié → ⚠️ (pas ✅) car ce n'est pas un vrai succès
        var result = new CycleResult { ScrapedCount = 30, FilteredCount = 10, FiltersWereActive = true };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("⚠️ 0/10 filtrés sur 30 scrapés.", message);
        Assert.DoesNotContain("•", message);
    }

    // ─── Tests Story 13.2 — détail des bets publiés ──────────────────────────

    [Fact]
    public void FormatCycleSuccess_WithPublishedBets_IncludesBetDetails()
    {
        // AC#1 : détail de chaque bet publié — match, cote, tipster
        var bets = new List<PendingBet>
        {
            MakeBet("Arsenal", "Man City", 2.50m, "john_tipster"),
            MakeBet("Real Madrid", "Barcelona", 1.85m, "alice_pro"),
            MakeBet("PSG", "Lyon", 3.10m, "bet_master")
        };
        var result = new CycleResult
        {
            ScrapedCount  = 45,
            FilteredCount = 45,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Contains("• Arsenal vs Man City — 2.50 (john_tipster)", message);
        Assert.Contains("• Real Madrid vs Barcelona — 1.85 (alice_pro)", message);
        Assert.Contains("• PSG vs Lyon — 3.10 (bet_master)", message);
        Assert.StartsWith("✅ 3 pronostics publiés sur 45 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_WithFiltersAndPublishedBets_IncludesBetDetails()
    {
        // AC#1 avec filtres actifs
        var bets = new List<PendingBet>
        {
            MakeBet("Arsenal", "Man City", 2.50m, "john_tipster"),
            MakeBet("PSG", "Lyon", 3.10m, "bet_master")
        };
        var result = new CycleResult
        {
            ScrapedCount   = 45,
            FilteredCount  = 12,
            FiltersWereActive = true,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.StartsWith("✅ 2/12 filtrés sur 45 scrapés.", message);
        Assert.Contains("• Arsenal vs Man City — 2.50 (john_tipster)", message);
        Assert.Contains("• PSG vs Lyon — 3.10 (bet_master)", message);
    }

    [Fact]
    public void FormatCycleSuccess_MoreThan15Bets_TruncatesWithNote()
    {
        // AC#2 : troncature à 15 + note "... et {n} autres"
        var bets = Enumerable.Range(1, 18)
            .Select(i => MakeBet($"Home{i}", $"Away{i}", 2.00m, "tipster"))
            .ToList();
        var result = new CycleResult
        {
            ScrapedCount   = 50,
            FilteredCount  = 50,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var betLines = lines.Where(l => l.TrimStart().StartsWith("•")).ToList();
        Assert.Equal(15, betLines.Count);
        Assert.Contains("... et 3 autres", message);
    }

    [Fact]
    public void FormatCycleSuccess_BetWithNullEvent_ShowsSansDescription()
    {
        // AC#3 : pronostic sans Event → "(sans description)"
        var bets = new List<PendingBet>
        {
            MakeBet("Arsenal", "Man City", 2.50m, "john_tipster"),
            MakeBetNoEvent(1.85m, "alice_pro")
        };
        var result = new CycleResult
        {
            ScrapedCount   = 45,
            FilteredCount  = 45,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Contains("• (sans description) — 1.85 (alice_pro)", message);
        Assert.Contains("• Arsenal vs Man City — 2.50 (john_tipster)", message);
    }

    [Fact]
    public void FormatCycleSuccess_BetWithNullTipster_ShowsInconnu()
    {
        // Issue #1 : TipsterUsername null → "(inconnu)"
        var bets = new List<PendingBet>
        {
            new PendingBet { Id = 1, Price = 2.00m, TipsterUsername = null,
                Event = new BetEvent { Home = "Team A", Away = "Team B", Starts = DateTime.UtcNow } }
        };
        var result = new CycleResult
        {
            ScrapedCount   = 10,
            FilteredCount  = 10,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Contains("• Team A vs Team B — 2.00 (inconnu)", message);
    }

    [Fact]
    public void FormatCycleSuccess_BetWithNullEventHome_ShowsSansDescription()
    {
        // Issue #8 : Event non null mais Home ou Away null → "(sans description)"
        var bets = new List<PendingBet>
        {
            new PendingBet { Id = 1, Price = 1.90m, TipsterUsername = "tipster",
                Event = new BetEvent { Home = null!, Away = "Team B", Starts = DateTime.UtcNow } }
        };
        var result = new CycleResult
        {
            ScrapedCount   = 5,
            FilteredCount  = 5,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Contains("• (sans description) — 1.90 (tipster)", message);
    }

    [Fact]
    public void FormatCycleSuccess_Exactly15Bets_NoTruncationNote()
    {
        // Exactement 15 bets → pas de note de troncature
        var bets = Enumerable.Range(1, 15)
            .Select(i => MakeBet($"Home{i}", $"Away{i}", 2.00m, "tipster"))
            .ToList();
        var result = new CycleResult
        {
            ScrapedCount   = 20,
            FilteredCount  = 20,
            PublishedBets  = bets
        };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.DoesNotContain("autres", message);
        var betLines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith("•")).ToList();
        Assert.Equal(15, betLines.Count);
    }
}
