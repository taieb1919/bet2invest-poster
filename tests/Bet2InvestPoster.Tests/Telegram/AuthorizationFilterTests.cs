using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Tests.Telegram.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bet2InvestPoster.Tests.Telegram;

public class AuthorizationFilterTests
{
    private static AuthorizationFilter CreateFilter(long authorizedChatId)
    {
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            AuthorizedChatId = authorizedChatId
        });
        return new AuthorizationFilter(options, NullLogger<AuthorizationFilter>.Instance);
    }

    [Fact]
    public void IsAuthorized_WithMatchingChatId_ReturnsTrue()
    {
        var filter = CreateFilter(12345L);
        Assert.True(filter.IsAuthorized(12345L));
    }

    [Fact]
    public void IsAuthorized_WithDifferentChatId_ReturnsFalse()
    {
        var filter = CreateFilter(12345L);
        Assert.False(filter.IsAuthorized(99999L));
    }

    [Fact]
    public void IsAuthorized_WithZeroChatId_ReturnsFalse()
    {
        var filter = CreateFilter(12345L);
        Assert.False(filter.IsAuthorized(0L));
    }

    [Fact]
    public void IsAuthorized_WithNegativeChatId_ReturnsFalse()
    {
        var filter = CreateFilter(12345L);
        Assert.False(filter.IsAuthorized(-987654L));
    }

    [Fact]
    public void AuthorizationFilter_RegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test";
            o.AuthorizedChatId = 1;
        });
        services.AddSingleton<AuthorizationFilter>();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var f1 = sp.GetRequiredService<AuthorizationFilter>();
        var f2 = sp.GetRequiredService<AuthorizationFilter>();

        Assert.Same(f1, f2);
    }

    [Fact]
    public void TelegramBotService_RegisteredAsHostedService()
    {
        var services = new ServiceCollection();
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test";
            o.AuthorizedChatId = 1;
        });
        services.AddSingleton<AuthorizationFilter>();
        services.AddSingleton<ITelegramBotClient>(_ => new FakeTelegramBotClient());
        services.AddSingleton<IOnboardingService, FakeOnboardingService>();
        services.AddSingleton<IConversationStateService, ConversationStateService>();
        services.AddHostedService<TelegramBotService>();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var hosted = sp.GetRequiredService<IHostedService>();

        Assert.IsType<TelegramBotService>(hosted);
    }

    private class FakeOnboardingService : IOnboardingService
    {
        public Task TrySendOnboardingAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
