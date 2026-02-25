using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Telegram.Formatters;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Tests.Telegram.Commands;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class NotificationServiceTests
{
    private static NotificationService CreateService(
        FakeTelegramBotClient botClient,
        long chatId = 99999)
    {
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            AuthorizedChatId = chatId
        });
        return new NotificationService(
            botClient,
            options,
            new MessageFormatter(),
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task NotifySuccessAsync_SendsCorrectSuccessMessage()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake);
        var bets = Enumerable.Range(1, 10)
            .Select(i => new PendingBet { Id = i, Price = 2.00m, TipsterUsername = "tipster" })
            .ToList();

        await service.NotifySuccessAsync(new CycleResult { ScrapedCount = 45, FilteredCount = 45, PublishedBets = bets });

        Assert.Single(fake.SentMessages);
        Assert.StartsWith("✅ 10 pronostics publiés sur 45 scrapés.", fake.SentMessages[0]);
    }

    [Fact]
    public async Task NotifySuccessAsync_ZeroCount_SendsCorrectMessage()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake);

        await service.NotifySuccessAsync(new CycleResult { ScrapedCount = 0, FilteredCount = 0 });

        Assert.Single(fake.SentMessages);
        Assert.Equal("⚠️ Aucun pronostic disponible chez les tipsters configurés.", fake.SentMessages[0]);
    }

    [Fact]
    public async Task NotifyFailureAsync_SendsCorrectFailureMessage()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake);

        await service.NotifyFailureAsync("InvalidOperationException");

        Assert.Single(fake.SentMessages);
        Assert.Equal("❌ Échec — InvalidOperationException.", fake.SentMessages[0]);
    }

    [Fact]
    public async Task NotifySuccessAsync_SendsMessageToConfiguredChatId()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake, chatId: 42000L);

        var bets5 = Enumerable.Range(1, 5).Select(i => new PendingBet { Id = i, Price = 2.00m, TipsterUsername = "tipster" }).ToList();
        await service.NotifySuccessAsync(new CycleResult { ScrapedCount = 10, FilteredCount = 10, PublishedBets = bets5 });

        Assert.Single(fake.SentChatIds);
        Assert.Equal(42000L, fake.SentChatIds[0]);
    }

    [Fact]
    public async Task NotifyFailureAsync_SendsMessageToConfiguredChatId()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake, chatId: 77777L);

        await service.NotifyFailureAsync("TestException");

        Assert.Single(fake.SentChatIds);
        Assert.Equal(77777L, fake.SentChatIds[0]);
    }

    [Fact]
    public async Task NotifyFailureAsync_EmptyReason_SendsMessage()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake);

        await service.NotifyFailureAsync("");

        Assert.Single(fake.SentMessages);
        Assert.Contains("Échec", fake.SentMessages[0]);
    }

    [Fact]
    public async Task NotifySuccessAsync_WithFilters_SendsFilteredFormatMessage()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake);

        var bets10 = Enumerable.Range(1, 10).Select(i => new PendingBet { Id = i, Price = 2.00m, TipsterUsername = "tipster" }).ToList();
        await service.NotifySuccessAsync(new CycleResult { ScrapedCount = 45, FilteredCount = 32, FiltersWereActive = true, PublishedBets = bets10 });

        Assert.Single(fake.SentMessages);
        Assert.StartsWith("✅ 10/32 filtrés sur 45 scrapés.", fake.SentMessages[0]);
    }

    [Fact]
    public async Task NotifySuccessAsync_ZeroScraped_SendsWarning()
    {
        var fake = new FakeTelegramBotClient();
        var service = CreateService(fake);

        await service.NotifySuccessAsync(new CycleResult { ScrapedCount = 0, FilteredCount = 0 });

        Assert.Single(fake.SentMessages);
        Assert.Equal("⚠️ Aucun pronostic disponible chez les tipsters configurés.", fake.SentMessages[0]);
    }
}
