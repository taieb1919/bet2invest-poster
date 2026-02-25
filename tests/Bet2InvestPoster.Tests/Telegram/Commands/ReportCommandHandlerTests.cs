using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class ReportCommandHandlerTests
{
    // --- Fake IHistoryManager ---

    private class FakeHistoryManager : IHistoryManager
    {
        private readonly List<HistoryEntry> _all;

        public FakeHistoryManager(List<HistoryEntry>? entries = null)
        {
            _all = entries ?? [];
        }

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default) =>
            Task.FromResult(new HashSet<string>());

        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task PurgeOldEntriesAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default) =>
            Task.FromResult(_all.OrderByDescending(e => e.PublishedAt).Take(count).ToList());

        public Task UpdateEntriesAsync(List<HistoryEntry> updatedEntries, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct = default) =>
            Task.FromResult(_all.Where(e => e.PublishedAt >= since).OrderByDescending(e => e.PublishedAt).ToList());
    }

    private static Message MakeMessage(string text = "/report") =>
        new() { Text = text, Chat = new Chat { Id = 99 } };

    private static ReportCommandHandler CreateHandler(List<HistoryEntry>? entries = null) =>
        new(
            new FakeHistoryManager(entries),
            new MessageFormatter(),
            NullLogger<ReportCommandHandler>.Instance,
            TimeProvider.System);

    // --- Tests CanHandle ---

    [Fact]
    public void CanHandle_Report_ReturnsTrue()
    {
        Assert.True(CreateHandler().CanHandle("/report"));
    }

    [Fact]
    public void CanHandle_OtherCommand_ReturnsFalse()
    {
        Assert.False(CreateHandler().CanHandle("/history"));
    }

    // --- Tests HandleAsync ---

    [Fact]
    public async Task HandleAsync_NoResolvedEntries_SendsEmptyMessage()
    {
        // Aucune entrée résolue (only pending)
        var entries = new List<HistoryEntry>
        {
            new()
            {
                BetId = 1, MatchupId = "1", MarketKey = "m",
                PublishedAt = DateTime.UtcNow.AddDays(-1),
                Result = "pending"
            }
        };

        var handler = CreateHandler(entries);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Aucun pronostic résolu", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WithEntries_SendsFormattedReport()
    {
        var entries = new List<HistoryEntry>
        {
            new()
            {
                BetId = 1, MatchupId = "1", MarketKey = "m",
                PublishedAt = DateTime.UtcNow.AddDays(-1),
                Result = "won", Odds = 1.85m, Sport = "Football", TipsterName = "johndoe"
            },
            new()
            {
                BetId = 2, MatchupId = "2", MarketKey = "m",
                PublishedAt = DateTime.UtcNow.AddDays(-2),
                Result = "lost", Odds = 2.10m, Sport = "Football", TipsterName = "johndoe"
            }
        };

        var handler = CreateHandler(entries);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        var msg = bot.SentMessages[0];
        Assert.Contains("📊 Rapport", msg);
        Assert.Contains("Taux de réussite", msg);
        Assert.Contains("ROI", msg);
        Assert.Contains("Top tipsters", msg);
    }

    [Fact]
    public async Task HandleAsync_WithDaysArgument_FiltersCorrectly()
    {
        // Entry il y a 20 jours — dans /report 30 mais pas dans /report 7
        var old = new HistoryEntry
        {
            BetId = 10, MatchupId = "10", MarketKey = "m",
            PublishedAt = DateTime.UtcNow.AddDays(-20),
            Result = "won", Odds = 1.90m
        };
        // Entry récente
        var recent = new HistoryEntry
        {
            BetId = 11, MatchupId = "11", MarketKey = "m",
            PublishedAt = DateTime.UtcNow.AddDays(-2),
            Result = "won", Odds = 2.00m
        };

        var handler = CreateHandler([old, recent]);
        var bot30 = new FakeTelegramBotClient();
        var bot7 = new FakeTelegramBotClient();

        await handler.HandleAsync(bot30, MakeMessage("/report 30"), CancellationToken.None);
        await handler.HandleAsync(bot7, MakeMessage("/report 7"), CancellationToken.None);

        // /report 30 doit voir les 2 entrées
        Assert.Contains("2 / 2", bot30.SentMessages[0]);
        // /report 7 ne voit que l'entrée récente
        Assert.Contains("1 / 1", bot7.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_InvalidArgument_SendsUsageMessage()
    {
        var handler = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/report abc"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Usage", bot.SentMessages[0]);
        Assert.Contains("/report [jours]", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_ZeroDaysArgument_SendsUsageMessage()
    {
        var handler = CreateHandler();
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage("/report 0"), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Usage", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_EmptyHistory_SendsEmptyMessage()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Aucun pronostic résolu", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_SendsToCorrectChatId()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();
        var msg = new Message { Text = "/report", Chat = new Chat { Id = 1234 } };

        await handler.HandleAsync(bot, msg, CancellationToken.None);

        Assert.Single(bot.SentChatIds);
        Assert.Equal(1234, bot.SentChatIds[0]);
    }
}

public class MessageFormatterReportTests
{
    private readonly MessageFormatter _formatter = new();

    private static HistoryEntry Won(decimal odds = 1.80m, string sport = "Football", string tipster = "john") =>
        new()
        {
            BetId = 1, MatchupId = "1", MarketKey = "m",
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            Result = "won", Odds = odds, Sport = sport, TipsterName = tipster
        };

    private static HistoryEntry Lost(string sport = "Football", string tipster = "john") =>
        new()
        {
            BetId = 2, MatchupId = "2", MarketKey = "m",
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            Result = "lost", Odds = 2.00m, Sport = sport, TipsterName = tipster
        };

    [Fact]
    public void FormatReport_NoResolved_ReturnsEmptyMessage()
    {
        var result = _formatter.FormatReport([], 7);
        Assert.Contains("Aucun pronostic résolu", result);
    }

    [Fact]
    public void FormatReport_WinRate_IsCorrect()
    {
        // 2 won, 2 lost → 50%
        var entries = new List<HistoryEntry> { Won(), Won(), Lost(), Lost() };
        var result = _formatter.FormatReport(entries, 7);
        Assert.Contains("50.0%", result);
        Assert.Contains("(2/4)", result);
    }

    [Fact]
    public void FormatReport_ROI_IsCorrect()
    {
        // 1 won à 2.00, 1 lost → totalReturn=2, totalStake=2, roi=0%
        var entries = new List<HistoryEntry>
        {
            Won(odds: 2.00m),
            Lost()
        };
        var result = _formatter.FormatReport(entries, 7);
        Assert.Contains("ROI", result);
        Assert.Contains("0.0%", result);
    }

    [Fact]
    public void FormatReport_SportBreakdown_IsIncluded()
    {
        var entries = new List<HistoryEntry>
        {
            Won(sport: "Football"),
            Lost(sport: "Tennis")
        };
        var result = _formatter.FormatReport(entries, 7);
        Assert.Contains("Football", result);
        Assert.Contains("Tennis", result);
    }

    [Fact]
    public void FormatReport_TopTipsters_SortedByWinRate()
    {
        // johndoe: 3 won / 3 = 100%, betmaster: 1 won / 2 = 50% (>= 2 minimum)
        var entries = new List<HistoryEntry>
        {
            Won(tipster: "johndoe"),
            Won(tipster: "johndoe"),
            Won(tipster: "johndoe"),
            Won(tipster: "betmaster"),
            Lost(tipster: "betmaster")
        };
        var result = _formatter.FormatReport(entries, 7);
        var idxJohn = result.IndexOf("johndoe", StringComparison.Ordinal);
        var idxBet = result.IndexOf("betmaster", StringComparison.Ordinal);
        Assert.True(idxJohn < idxBet, "johndoe (100%) doit apparaître avant betmaster (50%)");
    }

    [Fact]
    public void FormatReport_TopTipsters_ExcludesSingleBetTipsters()
    {
        // solo: 1 won / 1 = 100% mais < 2 paris → exclu
        var entries = new List<HistoryEntry>
        {
            Won(tipster: "solo"),
            Won(tipster: "regular"),
            Lost(tipster: "regular")
        };
        var result = _formatter.FormatReport(entries, 7);
        Assert.DoesNotContain("solo", result);
        Assert.Contains("regular", result);
    }
}
