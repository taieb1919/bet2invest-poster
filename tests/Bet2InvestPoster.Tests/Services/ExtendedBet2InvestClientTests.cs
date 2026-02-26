using System.Diagnostics;
using System.Net;
using System.Text;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Exceptions;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class ExtendedBet2InvestClientTests
{
    // ─── Helpers ───────────────────────────────────────────────────

    private static IOptions<Bet2InvestOptions> DefaultOptions(int requestDelayMs = 0) =>
        new OptionsWrapper<Bet2InvestOptions>(new Bet2InvestOptions
        {
            ApiBase = "https://api.bet2invest.com",
            Identifier = "test@example.com",
            Password = "test-password",
            RequestDelayMs = requestDelayMs
        });

    private static ExtendedBet2InvestClient CreateClient(
        HttpMessageHandler handler,
        IOptions<Bet2InvestOptions>? options = null)
    {
        var opts = options ?? DefaultOptions();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(opts.Value.ApiBase.TrimEnd('/'))
        };
        return new ExtendedBet2InvestClient(
            httpClient,
            opts,
            NullLogger<ExtendedBet2InvestClient>.Instance);
    }

    private static HttpResponseMessage LoginSuccess(int expiresIn = 3600) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $@"{{""accessToken"":""fake-token"",""expiresIn"":{expiresIn}}}",
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage StatisticsSuccess() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                @"{""bets"":{""pending"":[],""pendingNumber"":0,""canSeeBets"":true}}",
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage PublishSuccess() =>
        new(HttpStatusCode.Created)
        {
            Content = new StringContent(@"{""id"":123}", Encoding.UTF8, "application/json")
        };

    // ─── EnsureAuthenticated Tests ─────────────────────────────────

    [Fact]
    public async Task GetUpcomingBets_CallsLogin_WhenNotAuthenticated()
    {
        var loginCalled = false;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login")
            {
                loginCalled = true;
                return LoginSuccess();
            }
            return StatisticsSuccess();
        });

        var client = CreateClient(handler);
        await client.GetUpcomingBetsAsync(12345);

        Assert.True(loginCalled, "LoginAsync devrait être appelé quand non authentifié");
    }

    [Fact]
    public async Task GetUpcomingBets_DoesNotLogin_WhenTokenValid()
    {
        var loginCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login")
            {
                loginCount++;
                return LoginSuccess(expiresIn: 3600);
            }
            return StatisticsSuccess();
        });

        var client = CreateClient(handler);
        // First call triggers login.
        await client.GetUpcomingBetsAsync(12345);
        // Second call: token still valid → no re-login.
        await client.GetUpcomingBetsAsync(12346);

        Assert.Equal(1, loginCount);
    }

    [Fact]
    public async Task GetUpcomingBets_RelogsIn_WhenTokenExpired()
    {
        var loginCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login")
            {
                loginCount++;
                // expiresIn=59 → _tokenExpiresAt = UtcNow + (59-60)s = UtcNow - 1s → already expired.
                return LoginSuccess(expiresIn: 59);
            }
            return StatisticsSuccess();
        });

        var client = CreateClient(handler);
        await client.GetUpcomingBetsAsync(12345);
        // Token was expired from the first login — second call must re-login.
        await client.GetUpcomingBetsAsync(12346);

        Assert.Equal(2, loginCount);
    }

    // ─── canSeeBets Tuple Extraction Tests ─────────────────────────

    [Fact]
    public async Task GetUpcomingBets_ReturnsCanSeeBetsTrue_FromJsonResponse()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return StatisticsSuccess(); // canSeeBets:true, pending:[]
        });

        var client = CreateClient(handler);
        var (canSeeBets, bets) = await client.GetUpcomingBetsAsync(12345);

        Assert.True(canSeeBets);
        Assert.Empty(bets);
    }

    [Fact]
    public async Task GetUpcomingBets_ReturnsCanSeeBetsFalse_WhenApiIndicatesRestricted()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    @"{""bets"":{""pending"":[],""pendingNumber"":0,""canSeeBets"":false}}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = CreateClient(handler);
        var (canSeeBets, bets) = await client.GetUpcomingBetsAsync(12345);

        Assert.False(canSeeBets);
        Assert.Empty(bets);
    }

    // ─── Rate Limiting Test ─────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingBets_RespectsRequestDelay()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return StatisticsSuccess();
        });

        var sw = Stopwatch.StartNew();
        var client = CreateClient(handler, DefaultOptions(requestDelayMs: 200));
        await client.GetUpcomingBetsAsync(12345);
        sw.Stop();

        // Two rate-limit delays: login (200ms) + data (200ms) — 10ms tolerance each.
        Assert.True(sw.ElapsedMilliseconds >= 380,
            $"Délai attendu ≥ 400ms (2×200ms: login + data), obtenu {sw.ElapsedMilliseconds}ms");
    }

    // ─── Exception Tests ────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ThrowsBet2InvestApiException_OnHttpError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Unauthorized", Encoding.UTF8, "text/plain")
            });

        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<Bet2InvestApiException>(() => client.LoginAsync());
        Assert.Equal("/auth/login", ex.Endpoint);
        Assert.Equal(401, ex.HttpStatusCode);
    }

    [Fact]
    public async Task GetUpcomingBets_ThrowsBet2InvestApiException_OnHttpError()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("Forbidden", Encoding.UTF8, "text/plain")
            };
        });

        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<Bet2InvestApiException>(
            () => client.GetUpcomingBetsAsync(12345));
        Assert.Equal(403, ex.HttpStatusCode);
    }

    // ─── Exceptions Properties Tests ───────────────────────────────

    [Fact]
    public void Bet2InvestApiException_HasCorrectProperties()
    {
        var ex = new Bet2InvestApiException("/test/endpoint", 404, "Not Found", detectedChange: true);

        Assert.Equal("/test/endpoint", ex.Endpoint);
        Assert.Equal(404, ex.HttpStatusCode);
        Assert.Equal("Not Found", ex.ResponsePayload);
        Assert.True(ex.DetectedChange);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void Bet2InvestApiException_DetectedChange_DefaultsFalse()
    {
        var ex = new Bet2InvestApiException("/endpoint", 500);

        Assert.False(ex.DetectedChange);
        Assert.Null(ex.ResponsePayload);
    }

    [Fact]
    public void PublishException_HasCorrectProperties()
    {
        var ex = new PublishException(betId: 42, httpStatusCode: 422, "Unprocessable Entity");

        Assert.Equal(42, ex.BetId);
        Assert.Equal(422, ex.HttpStatusCode);
        Assert.Equal("Unprocessable Entity", ex.Message);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void PublishException_ZeroBetId_IsValid()
    {
        // BetId=0 is acceptable when publication fails before an ID is assigned.
        var ex = new PublishException(betId: 0, httpStatusCode: 500, "Internal server error");

        Assert.Equal(0, ex.BetId);
        Assert.Equal(500, ex.HttpStatusCode);
    }

    // ─── PublishBetAsync Tests ────────────────────────────────────────

    [Fact]
    public async Task PublishBet_Success_ReturnsOrderId()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return PublishSuccess();
        });

        var client = CreateClient(handler);
        var result = await client.PublishBetAsync(1,
            new BetOrderRequest { Units = 1m });

        Assert.Equal("123", result);
    }

    [Fact]
    public async Task PublishBet_ThrowsPublishException_OnHttpError()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("Validation error", Encoding.UTF8, "text/plain")
            };
        });

        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PublishException>(
            () => client.PublishBetAsync(1, new BetOrderRequest()));
        Assert.Equal(422, ex.HttpStatusCode);
    }

    [Fact]
    public async Task PublishBet_RespectsRequestDelay()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return PublishSuccess();
        });

        var sw = Stopwatch.StartNew();
        var client = CreateClient(handler, DefaultOptions(requestDelayMs: 200));
        await client.PublishBetAsync(1,
            new BetOrderRequest { Units = 1m });
        sw.Stop();

        // Two rate-limit delays: login (200ms) + publish (200ms) — 10ms tolerance each.
        Assert.True(sw.ElapsedMilliseconds >= 380,
            $"Délai attendu ≥ 400ms (2×200ms: login + publish), obtenu {sw.ElapsedMilliseconds}ms");
    }

    // ─── PublishBetAsync Odd-Change Retry Tests ──────────────────────

    private static HttpResponseMessage OddChangeResponse(string newPriceStr = "193") =>
        new(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                $@"{{""statusCode"":422,""message"":[""The odd have changed to 2.93. Validate to accept the change."",""{newPriceStr}""],""error"":""UNPROCESSABLE_ENTITY""}}",
                Encoding.UTF8,
                "application/json")
        };

    [Fact]
    public async Task PublishBet_422OddChange_RetriesWithNewPrice_Success()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            callCount++;
            if (callCount == 1) return OddChangeResponse("293");
            return PublishSuccess();
        });

        var client = CreateClient(handler);
        var bet = new BetOrderRequest { Price = 250, Units = 1m };
        var result = await client.PublishBetAsync(1, bet);

        Assert.Equal("123", result);
        Assert.Equal(293, bet.Price);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task PublishBet_422MalformedBody_ThrowsPublishException()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("not json", Encoding.UTF8, "text/plain")
            };
        });

        var client = CreateClient(handler);
        var ex = await Assert.ThrowsAsync<PublishException>(
            () => client.PublishBetAsync(1, new BetOrderRequest()));
        Assert.Equal(422, ex.HttpStatusCode);
    }

    [Fact]
    public async Task PublishBet_422OddChange_RetryAlsoFails_ThrowsPublishException()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return OddChangeResponse("300");
        });

        var client = CreateClient(handler);
        var ex = await Assert.ThrowsAsync<PublishException>(
            () => client.PublishBetAsync(1, new BetOrderRequest { Price = 250 }));
        Assert.Equal(422, ex.HttpStatusCode);
    }

    // ─── DI Registration Tests ──────────────────────────────────────

    [Fact]
    public void DI_ScopedLifetime_ReturnsDifferentInstancesPerScope()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bet2Invest:ApiBase"] = "https://api.bet2invest.com",
                ["Bet2Invest:Identifier"] = "test@example.com",
                ["Bet2Invest:Password"] = "test-password"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<Bet2InvestOptions>(config.GetSection(Bet2InvestOptions.SectionName));
        services.AddLogging();

        // Register using the internal test constructor via factory (no real network).
        services.AddScoped<IExtendedBet2InvestClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Bet2InvestOptions>>();
            var logger = sp.GetRequiredService<ILogger<ExtendedBet2InvestClient>>();
            return new ExtendedBet2InvestClient(
                new HttpClient { BaseAddress = new Uri("https://api.bet2invest.com") },
                opts,
                logger);
        });

        var sp2 = services.BuildServiceProvider();
        IExtendedBet2InvestClient instance1, instance2;

        using (var scope1 = sp2.CreateScope())
            instance1 = scope1.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();

        using (var scope2 = sp2.CreateScope())
            instance2 = scope2.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();

        Assert.NotSame(instance1, instance2);
    }

    [Fact]
    public void DI_SingletonBet2InvestClient_ReturnsSameInstanceAcrossScopes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bet2Invest:ApiBase"] = "https://api.bet2invest.com",
                ["Bet2Invest:Identifier"] = "test@example.com",
                ["Bet2Invest:Password"] = "test-password"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<Bet2InvestOptions>(config.GetSection(Bet2InvestOptions.SectionName));
        services.AddLogging();
        services.AddSingleton<Bet2InvestClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<Bet2InvestClient>>();
            return new Bet2InvestClient(opts.ApiBase, opts.RequestDelayMs,
                new SerilogConsoleLoggerAdapter(logger));
        });

        var sp2 = services.BuildServiceProvider();
        Bet2InvestClient instance1, instance2;

        using (var scope1 = sp2.CreateScope())
            instance1 = scope1.ServiceProvider.GetRequiredService<Bet2InvestClient>();

        using (var scope2 = sp2.CreateScope())
            instance2 = scope2.ServiceProvider.GetRequiredService<Bet2InvestClient>();

        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void DI_RealRegistration_ResolvesExtendedBet2InvestClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bet2Invest:ApiBase"] = "https://api.bet2invest.com",
                ["Bet2Invest:Identifier"] = "test@example.com",
                ["Bet2Invest:Password"] = "test-password"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<Bet2InvestOptions>(config.GetSection(Bet2InvestOptions.SectionName));
        services.AddLogging();

        // Mirror the exact registrations from Program.cs.
        services.AddSingleton<Bet2InvestClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<Bet2InvestClient>>();
            return new Bet2InvestClient(opts.ApiBase, opts.RequestDelayMs,
                new SerilogConsoleLoggerAdapter(logger));
        });
        services.AddScoped<IExtendedBet2InvestClient, ExtendedBet2InvestClient>();

        var sp2 = services.BuildServiceProvider();
        using var scope = sp2.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();

        Assert.NotNull(client);
        Assert.IsType<ExtendedBet2InvestClient>(client);
    }

    // ─── GetFreeTipstersAsync Tests (Story 11.1, Task 7.1) ──────────

    private static HttpResponseMessage TipstersPageResponse(
        IEnumerable<object> tipsters, int? nextPage = null)
    {
        var tipstersJson = string.Join(",", tipsters.Select(t => System.Text.Json.JsonSerializer.Serialize(t)));
        var paginationJson = nextPage.HasValue
            ? $@",""pagination"":{{""nextPage"":{nextPage}}}"
            : @",""pagination"":{}";
        var json = $@"{{""tipsters"":[{tipstersJson}]{paginationJson}}}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task GetFreeTipstersAsync_FiltersFreeOnly_ReturnsFreeOrdered()
    {
        // Arrange: 2 pro, 2 free — seuls les free doivent être retournés, triés par ROI descendant
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return TipstersPageResponse(new object[]
            {
                new { id = 1, username = "proTipster", pro = true, generalStatistics = new { roi = 20.0, betsNumber = 100, mostBetSport = "Football" } },
                new { id = 2, username = "freeLow", pro = false, generalStatistics = new { roi = 5.0, betsNumber = 50, mostBetSport = "Tennis" } },
                new { id = 3, username = "freeHigh", pro = false, generalStatistics = new { roi = 15.0, betsNumber = 80, mostBetSport = "Basketball" } },
                new { id = 4, username = "proTipster2", pro = true, generalStatistics = new { roi = 30.0, betsNumber = 200, mostBetSport = "Football" } },
            });
        });

        var client = CreateClient(handler);
        var result = await client.GetFreeTipstersAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("freeHigh", result[0].Username); // ROI 15 > ROI 5
        Assert.Equal("freeLow", result[1].Username);
        Assert.Equal(15.0m, result[0].Roi);
        Assert.Equal("Basketball", result[0].MostBetSport);
        Assert.Equal(80, result[0].BetsNumber);
    }

    [Fact]
    public async Task GetFreeTipstersAsync_PaginationStops_WhenNoNextPage()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            callCount++;
            return TipstersPageResponse(new object[]
            {
                new { id = 1, username = "freeA", pro = false, generalStatistics = new { roi = 10.0, betsNumber = 10, mostBetSport = "Football" } }
            }); // pas de nextPage → arrêt après page 0
        });

        var client = CreateClient(handler);
        var result = await client.GetFreeTipstersAsync();

        Assert.Equal(1, callCount); // Une seule requête tipsters (après login)
        Assert.Single(result);
    }

    [Fact]
    public async Task GetFreeTipstersAsync_EmptyPage_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return TipstersPageResponse(Array.Empty<object>());
        });

        var client = CreateClient(handler);
        var result = await client.GetFreeTipstersAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFreeTipstersAsync_NoFreeOnPage_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath == "/auth/login") return LoginSuccess();
            return TipstersPageResponse(new object[]
            {
                new { id = 1, username = "onlyPro", pro = true, generalStatistics = new { roi = 25.0, betsNumber = 300, mostBetSport = "Football" } }
            });
        });

        var client = CreateClient(handler);
        var result = await client.GetFreeTipstersAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFreeTipstersAsync_HttpError_ThrowsBet2InvestApiException()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/auth")) return LoginSuccess();
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal Server Error", Encoding.UTF8, "text/plain")
            };
        });

        var client = CreateClient(handler);
        await Assert.ThrowsAsync<Bet2InvestApiException>(() => client.GetFreeTipstersAsync());
    }

    // ─── Fake helpers ───────────────────────────────────────────────

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
