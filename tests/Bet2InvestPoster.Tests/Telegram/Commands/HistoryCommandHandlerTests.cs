using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.Logging.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

public class HistoryCommandHandlerTests
{
    // --- Fake IHistoryManager ---

    private class FakeHistoryManager : IHistoryManager
    {
        private readonly List<HistoryEntry> _entries;

        public FakeHistoryManager(List<HistoryEntry>? entries = null)
        {
            _entries = entries ?? [];
        }

        public Task<HashSet<string>> LoadPublishedKeysAsync(CancellationToken ct = default) =>
            Task.FromResult(new HashSet<string>());

        public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task PurgeOldEntriesAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct = default) =>
            Task.FromResult(_entries.OrderByDescending(e => e.PublishedAt).Take(count).ToList());
    }

    private static Message MakeMessage(string text = "/history") =>
        new() { Text = text, Chat = new Chat { Id = 99 } };

    private static HistoryCommandHandler CreateHandler(List<HistoryEntry>? entries = null) =>
        new(
            new FakeHistoryManager(entries),
            new MessageFormatter(),
            NullLogger<HistoryCommandHandler>.Instance);

    // --- Tests ---

    [Fact]
    public void CanHandle_History_ReturnsTrue()
    {
        Assert.True(CreateHandler().CanHandle("/history"));
    }

    [Fact]
    public void CanHandle_Status_ReturnsFalse()
    {
        Assert.False(CreateHandler().CanHandle("/status"));
    }

    [Fact]
    public async Task HandleAsync_EmptyHistory_SendsAucuneMessage()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Equal("📭 Aucune publication dans l'historique.", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_WithEntries_SendsFormattedHistory()
    {
        var entries = new List<HistoryEntry>
        {
            new()
            {
                BetId = 42,
                MatchupId = "1",
                MarketKey = "m",
                PublishedAt = new DateTime(2026, 2, 25, 14, 30, 0, DateTimeKind.Utc),
                MatchDescription = "Arsenal vs Chelsea"
            }
        };

        var handler = CreateHandler(entries);
        var bot = new FakeTelegramBotClient();

        await handler.HandleAsync(bot, MakeMessage(), CancellationToken.None);

        Assert.Single(bot.SentMessages);
        Assert.Contains("Arsenal vs Chelsea", bot.SentMessages[0]);
        Assert.Contains("📋 Historique", bot.SentMessages[0]);
    }

    [Fact]
    public async Task HandleAsync_SendsToCorrectChatId()
    {
        var handler = CreateHandler([]);
        var bot = new FakeTelegramBotClient();
        var msg = new Message { Text = "/history", Chat = new Chat { Id = 7777 } };

        await handler.HandleAsync(bot, msg, CancellationToken.None);

        Assert.Single(bot.SentChatIds);
        Assert.Equal(7777, bot.SentChatIds[0]);
    }
}
