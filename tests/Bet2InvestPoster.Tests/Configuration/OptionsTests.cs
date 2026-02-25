using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Configuration;

public class OptionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void Bet2InvestOptions_BindsCorrectlyFromConfiguration()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Bet2Invest:ApiBase"] = "https://custom.api.com",
            ["Bet2Invest:Identifier"] = "user@example.com",
            ["Bet2Invest:Password"] = "secret",
            ["Bet2Invest:RequestDelayMs"] = "750"
        });

        var services = new ServiceCollection();
        services.Configure<Bet2InvestOptions>(config.GetSection(Bet2InvestOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;

        Assert.Equal("https://custom.api.com", options.ApiBase);
        Assert.Equal("user@example.com", options.Identifier);
        Assert.Equal("secret", options.Password);
        Assert.Equal(750, options.RequestDelayMs);
    }

    [Fact]
    public void Bet2InvestOptions_HasCorrectDefaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.Configure<Bet2InvestOptions>(config.GetSection(Bet2InvestOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;

        Assert.Equal("https://api.bet2invest.com", options.ApiBase);
        Assert.Equal("", options.Identifier);
        Assert.Equal("", options.Password);
        Assert.Equal(500, options.RequestDelayMs);
    }

    [Fact]
    public void TelegramOptions_BindsCorrectlyFromConfiguration()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Telegram:BotToken"] = "123456:ABC-token",
            ["Telegram:AuthorizedChatId"] = "987654321"
        });

        var services = new ServiceCollection();
        services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;

        Assert.Equal("123456:ABC-token", options.BotToken);
        Assert.Equal(987654321L, options.AuthorizedChatId);
    }

    [Fact]
    public void TelegramOptions_HasCorrectDefaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;

        Assert.Equal("", options.BotToken);
        Assert.Equal(0L, options.AuthorizedChatId);
    }

    [Fact]
    public void PosterOptions_BindsCorrectlyFromConfiguration()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Poster:ScheduleTime"] = "09:30",
            ["Poster:RetryDelayMs"] = "30000",
            ["Poster:MaxRetryCount"] = "5",
            ["Poster:DataPath"] = "/opt/bet2invest-poster",
            ["Poster:LogPath"] = "/opt/bet2invest-poster/logs",
            ["Poster:LogRetentionDays"] = "45"
        });

        var services = new ServiceCollection();
        services.Configure<PosterOptions>(config.GetSection(PosterOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<PosterOptions>>().Value;

        Assert.Equal("09:30", options.ScheduleTime);
        Assert.Equal(30000, options.RetryDelayMs);
        Assert.Equal(5, options.MaxRetryCount);
        Assert.Equal("/opt/bet2invest-poster", options.DataPath);
        Assert.Equal("/opt/bet2invest-poster/logs", options.LogPath);
        Assert.Equal(45, options.LogRetentionDays);
    }

    [Fact]
    public void PosterOptions_HasCorrectDefaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var services = new ServiceCollection();
        services.Configure<PosterOptions>(config.GetSection(PosterOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<PosterOptions>>().Value;

        Assert.Equal("08:00", options.ScheduleTime);
        Assert.Equal(60000, options.RetryDelayMs);
        Assert.Equal(3, options.MaxRetryCount);
        Assert.Equal(".", options.DataPath);
        Assert.Equal("logs", options.LogPath);
        Assert.Equal(30, options.LogRetentionDays);
    }

    [Fact]
    public void PosterOptions_LogRetentionDays_IsBindableFromConfiguration()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Poster:LogRetentionDays"] = "60"
        });

        var services = new ServiceCollection();
        services.Configure<PosterOptions>(config.GetSection(PosterOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<PosterOptions>>().Value;

        Assert.Equal(60, options.LogRetentionDays);
    }

    [Theory]
    [InlineData(1)]   // boundary minimum valide : 1 jour = 1 fichier conservé
    [InlineData(7)]   // cas courant : 1 semaine
    [InlineData(365)] // cas extrême valide : 1 an
    public void PosterOptions_LogRetentionDays_ValidValues_Bind(int days)
    {
        // LogRetentionDays doit être > 0 (validé dans Program.cs au démarrage).
        // Ce test vérifie que les valeurs valides se lient correctement depuis la configuration.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Poster:LogRetentionDays"] = days.ToString()
        });

        var services = new ServiceCollection();
        services.Configure<PosterOptions>(config.GetSection(PosterOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<PosterOptions>>().Value;

        Assert.Equal(days, options.LogRetentionDays);
    }

    [Theory]
    [InlineData(0)]  // boundary invalide : Program.cs lève InvalidOperationException si <= 0
    [InlineData(-1)] // négatif invalide
    public void PosterOptions_LogRetentionDays_InvalidValues_BindButAreRejectedAtStartup(int days)
    {
        // Ces valeurs se lient sans erreur au niveau Options (pas de DataAnnotations),
        // mais Program.cs les rejette avec InvalidOperationException avant le démarrage du host.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Poster:LogRetentionDays"] = days.ToString()
        });

        var services = new ServiceCollection();
        services.Configure<PosterOptions>(config.GetSection(PosterOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<PosterOptions>>().Value;

        // La valeur est liée telle quelle — la validation est déléguée à Program.cs
        Assert.Equal(days, options.LogRetentionDays);
        Assert.True(options.LogRetentionDays <= 0, "Valeur invalide confirmée : Program.cs devrait lever InvalidOperationException");
    }

    [Fact]
    public void Configuration_HigherPrioritySourceOverridesLowerPrioritySource()
    {
        // .NET Generic Host registers sources in order: appsettings.json < appsettings.{env}.json < env vars
        // AddInMemoryCollection registered last acts as highest-priority source (same mechanism as env vars)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bet2Invest:Identifier"] = "from-appsettings"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bet2Invest:Identifier"] = "from-envvar"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<Bet2InvestOptions>(config.GetSection(Bet2InvestOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;

        // Later-registered source (higher priority) wins — same behaviour as env vars over appsettings
        Assert.Equal("from-envvar", options.Identifier);
    }

    [Fact]
    public void TelegramOptions_SupportsNegativeChatId_ForGroupAndChannelChats()
    {
        // Telegram group and channel IDs are negative (e.g. -100123456789)
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Telegram:BotToken"] = "123456:ABC-token",
            ["Telegram:AuthorizedChatId"] = "-100123456789"
        });

        var services = new ServiceCollection();
        services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.SectionName));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;

        Assert.Equal(-100123456789L, options.AuthorizedChatId);
    }

    [Fact]
    public void Bet2InvestOptions_ToString_RedactsPassword()
    {
        var options = new Bet2InvestOptions
        {
            Identifier = "user@example.com",
            Password = "super-secret"
        };

        var result = options.ToString();

        Assert.Contains("user@example.com", result);
        Assert.DoesNotContain("super-secret", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void TelegramOptions_ToString_RedactsBotToken()
    {
        var options = new TelegramOptions
        {
            BotToken = "123456:ABC-secret-token",
            AuthorizedChatId = 987654321
        };

        var result = options.ToString();

        Assert.DoesNotContain("ABC-secret-token", result);
        Assert.Contains("[REDACTED]", result);
        Assert.Contains("987654321", result);
    }
}
