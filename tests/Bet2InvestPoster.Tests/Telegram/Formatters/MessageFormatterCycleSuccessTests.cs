using Bet2InvestPoster.Models;
using Bet2InvestPoster.Telegram.Formatters;

namespace Bet2InvestPoster.Tests.Telegram.Formatters;

/// <summary>Tests Story 13.1 — FormatCycleSuccess : 3 cas (standard, filtres, zéro scrapé).</summary>
public class MessageFormatterCycleSuccessTests
{
    private readonly MessageFormatter _formatter = new();

    [Fact]
    public void FormatCycleSuccess_StandardCase_ReturnsPublishedOnScraped()
    {
        // AC#1 : sans filtres actifs
        var result = new CycleResult { ScrapedCount = 45, FilteredCount = 45, PublishedCount = 10 };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("✅ 10 pronostics publiés sur 45 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_WithFilters_ReturnsFilteredFormat()
    {
        // AC#2 : avec filtres actifs (FiltersWereActive = true)
        var result = new CycleResult { ScrapedCount = 45, FilteredCount = 32, PublishedCount = 10, FiltersWereActive = true };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("✅ 10/32 filtrés sur 45 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_ZeroScraped_ReturnsWarningMessage()
    {
        // AC#3 : zéro pronostic scrapé
        var result = new CycleResult { ScrapedCount = 0, FilteredCount = 0, PublishedCount = 0 };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("⚠️ Aucun pronostic disponible chez les tipsters configurés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_FilteredEqualsScraped_UsesStandardFormat()
    {
        // Quand FilteredCount == ScrapedCount : pas de filtrage actif → format standard
        var result = new CycleResult { ScrapedCount = 20, FilteredCount = 20, PublishedCount = 5 };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("✅ 5 pronostics publiés sur 20 scrapés.", message);
    }

    [Fact]
    public void FormatCycleSuccess_ZeroPublished_WithFilters_ShowsWarningIcon()
    {
        // Cas edge : filtres actifs mais 0 publié → ⚠️ (pas ✅) car ce n'est pas un vrai succès
        var result = new CycleResult { ScrapedCount = 30, FilteredCount = 10, PublishedCount = 0, FiltersWereActive = true };

        var message = _formatter.FormatCycleSuccess(result);

        Assert.Equal("⚠️ 0/10 filtrés sur 30 scrapés.", message);
    }
}
