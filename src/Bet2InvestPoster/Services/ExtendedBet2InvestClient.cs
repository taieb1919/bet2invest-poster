using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Exceptions;
using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Api;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Wraps (composes) Bet2InvestClient from the scraper and adds authenticated endpoints
/// not covered by it: GetUpcomingBetsAsync and PublishBetAsync.
/// Manages its own HttpClient and auth state independently.
/// </summary>
public class ExtendedBet2InvestClient : IExtendedBet2InvestClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly Bet2InvestOptions _options;
    private readonly ILogger<ExtendedBet2InvestClient> _logger;
    // Kept for future reuse of GetTipstersAsync / GetSettledBetsAsync from the scraper.
    private readonly Bet2InvestClient? _scraperClient;

    private string? _accessToken;
    // Token is considered expired 60s before actual expiry (safety margin).
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new NullableDecimalConverter(), new NullableIntConverter() }
    };

    /// <summary>
    /// Treats JSON null as 0m for non-nullable decimal properties (e.g. pending bets with null wonUnits).
    /// WARNING: silently converts null → 0. Acceptable here because wonUnits/lostUnits null = no result yet (pending bets).
    /// If the API returns null for fields where 0 has a different semantic meaning, this converter will mask the issue.
    /// </summary>
    private sealed class NullableDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? 0m : reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    /// <summary>Treats JSON null as 0 for non-nullable int properties.</summary>
    private sealed class NullableIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? 0 : reader.GetInt32();
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    };

    // ─── Constructors ──────────────────────────────────────────────

    public ExtendedBet2InvestClient(
        Bet2InvestClient scraperClient,
        IOptions<Bet2InvestOptions> options,
        ILogger<ExtendedBet2InvestClient> logger)
    {
        _scraperClient = scraperClient;
        _options = options.Value;
        _logger = logger;
        _http = BuildHttpClient(_options.ApiBase);
    }

    /// <summary>
    /// Internal constructor for unit tests — accepts a pre-configured HttpClient (no real network calls).
    /// </summary>
    internal ExtendedBet2InvestClient(
        HttpClient httpClient,
        IOptions<Bet2InvestOptions> options,
        ILogger<ExtendedBet2InvestClient> logger)
    {
        _http = httpClient;
        _options = options.Value;
        _logger = logger;
        _scraperClient = null;
    }

    private static HttpClient BuildHttpClient(string apiBase)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(apiBase.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("Accept-Language", "fr");
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    // ─── Public Properties ─────────────────────────────────────────

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTime.UtcNow;

    // ─── Authentication ────────────────────────────────────────────

    public async Task LoginAsync(CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Auth"))
        {
            try
            {
                // Rate limiting: 500ms minimum between API requests (NFR8).
                await Task.Delay(_options.RequestDelayMs, ct);

                var request = new LoginRequest
                {
                    Identifier = _options.Identifier,
                    Password = _options.Password
                };

                var response = await _http.PostAsJsonAsync("/auth/login", request, JsonOptions, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("Échec de connexion : HTTP {StatusCode}", (int)response.StatusCode);
                    throw new Bet2InvestApiException("/auth/login", (int)response.StatusCode, errorBody);
                }

                var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);
                if (loginResponse == null || string.IsNullOrEmpty(loginResponse.AccessToken))
                {
                    _logger.LogError("Réponse de login invalide (token absent)");
                    throw new Bet2InvestApiException(
                        "/auth/login", (int)response.StatusCode, "Invalid login response", detectedChange: true);
                }

                _accessToken = loginResponse.AccessToken;
                // Store expiry with 60s safety margin so we re-login before the real expiry.
                _tokenExpiresAt = DateTime.UtcNow.AddSeconds(loginResponse.ExpiresIn - 60);
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);

                _logger.LogInformation(
                    "Connexion réussie — token expire dans {ExpiresIn}s", loginResponse.ExpiresIn);
            }
            catch (Bet2InvestApiException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur inattendue lors de la connexion");
                throw new Bet2InvestApiException("/auth/login", 0, ex.Message);
            }
        }
    }

    /// <summary>
    /// Ensures a valid, non-expired token is in memory before making an API call.
    /// Re-authenticates automatically if the token is missing or expired (FR3).
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        if (IsAuthenticated)
        {
            using (LogContext.PushProperty("Step", "Auth"))
            {
                _logger.LogInformation(
                    "Token valide réutilisé — expiration dans {SecondsLeft}s",
                    (int)(_tokenExpiresAt - DateTime.UtcNow).TotalSeconds);
            }
            return;
        }

        await LoginAsync(ct);
    }

    // ─── Tipster ID Resolution (GET /tipsters) ────────────────────

    public async Task ResolveTipsterIdsAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        using (LogContext.PushProperty("Step", "Scrape"))
        {
            var slugsToResolve = tipsters
                .Where(t => t.NumericId == 0)
                .ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

            if (slugsToResolve.Count == 0) return;

            _logger.LogInformation("Résolution des IDs numériques pour {Count} tipster(s)", slugsToResolve.Count);

            // Guard: max 50 pages (~1000 tipsters). Évite boucle infinie si l'API pagine en petits lots.
            const int maxPages = 50;
            for (var page = 0; slugsToResolve.Count > 0 && page < maxPages; page++)
            {
                await Task.Delay(_options.RequestDelayMs, ct);

                var url = $"/tipsters?page={page}&mostBetSport=all&mostBetType=all&minBets=0&lastActivityMonths=12&orderBy=grade&orderDirection=DESC&proFirst=true";
                var response = await _http.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode) break;

                var data = await response.Content.ReadFromJsonAsync<TipstersResponse>(JsonOptions, ct);
                if (data?.Tipsters == null || data.Tipsters.Count == 0) break;

                foreach (var apiTipster in data.Tipsters)
                {
                    if (slugsToResolve.TryGetValue(apiTipster.Username, out var config))
                    {
                        config.NumericId = apiTipster.Id;
                        config.Roi = apiTipster.GeneralStatistics?.Roi;
                        config.BetsNumber = apiTipster.GeneralStatistics?.BetsNumber;
                        config.MostBetSport = apiTipster.GeneralStatistics?.MostBetSport;
                        _logger.LogInformation("Tipster {Name} résolu → numericId={NumericId}", config.Name, apiTipster.Id);
                        slugsToResolve.Remove(apiTipster.Username);
                    }
                }

                if (data.Pagination?.NextPage == null || data.Pagination.NextPage <= page) break;
            }

            foreach (var unresolved in slugsToResolve)
            {
                _logger.LogWarning("Tipster {Slug} non trouvé dans l'API — sera ignoré", unresolved.Key);
            }
        }
    }

    // ─── Upcoming Bets (GET /v1/statistics/{numericId}) ─────────

    public async Task<(bool CanSeeBets, List<PendingBet> Bets)> GetUpcomingBetsAsync(int tipsterNumericId, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        using (LogContext.PushProperty("Step", "Scrape"))
        {
            // Rate limiting: 500ms minimum between API requests (NFR8).
            await Task.Delay(_options.RequestDelayMs, ct);

            var url = $"/v1/statistics/{tipsterNumericId}";
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new Bet2InvestApiException(url, (int)response.StatusCode, errorBody);
            }

            var statistics = await response.Content.ReadFromJsonAsync<StatisticsResponse>(JsonOptions, ct);
            var bets = statistics?.Bets?.Pending ?? [];
            var canSeeBets = statistics?.Bets?.CanSeeBets ?? false;

            _logger.LogInformation(
                "Paris à venir récupérés pour tipster {TipsterId} : {Count} paris en attente (canSeeBets={CanSeeBets})",
                tipsterNumericId, bets.Count, canSeeBets);

            return (canSeeBets, bets);
        }
    }

    // ─── Publish Bet (POST /v1/bet-orders) ────────────────────────

    public async Task<string?> PublishBetAsync(int bankrollId, BetOrderRequest bet, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        using (LogContext.PushProperty("Step", "Publish"))
        {
            // Rate limiting: 500ms minimum between API requests (NFR8).
            await Task.Delay(_options.RequestDelayMs, ct);

            var url = $"/v1/bankrolls/{bankrollId}/bets";
            var response = await _http.PostAsJsonAsync(url, bet, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Échec de publication : HTTP {StatusCode}", (int)response.StatusCode);
                throw new PublishException(0, (int)response.StatusCode,
                    $"Publication échouée avec HTTP {(int)response.StatusCode}: {errorBody}");
            }

            var result = await response.Content.ReadFromJsonAsync<BetOrderResponse>(JsonOptions, ct);
            _logger.LogInformation("Pari publié avec succès via {Url} — order {OrderId}", url, result?.Id);
            return result?.Id?.ToString();
        }
    }

    // ─── Free Tipsters Scraping (GET /tipsters, Pro == false) ──────

    public async Task<List<ScrapedTipster>> GetFreeTipstersAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        using (LogContext.PushProperty("Step", "Scrape"))
        {
            var freeTipsters = new List<ApiTipster>();
            const int maxPages = 50;

            for (var page = 0; page < maxPages; page++)
            {
                // Rate limiting: 500ms minimum entre requêtes paginées (NFR8)
                await Task.Delay(_options.RequestDelayMs, ct);

                var url = $"/tipsters?page={page}&mostBetSport=all&mostBetType=all&minBets=0&lastActivityMonths=12&orderBy=grade&orderDirection=DESC";
                var response = await _http.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    throw new Bet2InvestApiException($"/tipsters?page={page}", (int)response.StatusCode, errorBody);
                }

                var data = await response.Content.ReadFromJsonAsync<TipstersResponse>(JsonOptions, ct);
                if (data?.Tipsters == null || data.Tipsters.Count == 0) break;

                var freeOnPage = data.Tipsters.Where(t => !t.Pro).ToList();
                freeTipsters.AddRange(freeOnPage);

                _logger.LogInformation(
                    "Page {Page} : {Total} tipsters ({Free} free)",
                    page, data.Tipsters.Count, freeOnPage.Count);

                if (data.Pagination?.NextPage == null || data.Pagination.NextPage <= page) break;
            }

            var result = freeTipsters
                .Select(t => new ScrapedTipster
                {
                    Username = t.Username,
                    Roi = t.GeneralStatistics?.Roi ?? 0m,
                    BetsNumber = t.GeneralStatistics?.BetsNumber ?? 0,
                    MostBetSport = t.GeneralStatistics?.MostBetSport ?? string.Empty
                })
                .OrderByDescending(t => t.Roi)
                .ToList();

            _logger.LogInformation(
                "Scraping terminé : {Count} tipsters free trouvés, triés par ROI descendant",
                result.Count);

            return result;
        }
    }

    // ─── Settled Bets (GET /v1/statistics/{numericId}/bets/settled) ──────────

    public async Task<List<SettledBet>> GetSettledBetsForTipsterAsync(
        int numericId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        using (LogContext.PushProperty("Step", "Report"))
        {
            var allBets = new List<SettledBet>();
            var page = 0;
            var startDateStr = startDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endDateStr = endDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            const int maxPages = 20;

            for (; page < maxPages && !ct.IsCancellationRequested; page++)
            {
                await Task.Delay(_options.RequestDelayMs, ct);

                var url = $"/v1/statistics/{numericId}/bets/settled?page={page}&sportId=-1&startDate={startDateStr}&endDate={endDateStr}&onlyImportedBets=false";
                var response = await _http.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode) break;

                var data = await response.Content.ReadFromJsonAsync<SettledBetsResponse>(JsonOptions, ct);
                if (data?.SettledBets == null || data.SettledBets.Count == 0) break;

                allBets.AddRange(data.SettledBets);

                if (data.Pagination?.NextPage == null || data.Pagination.NextPage <= page) break;
            }

            _logger.LogInformation(
                "Paris résolus récupérés pour tipster {NumericId} : {Count} paris", numericId, allBets.Count);

            return allBets;
        }
    }

    // ─── Cleanup ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // ─── Private DTOs (deserialization only) ──────────────────────

    private class StatisticsResponse
    {
        [JsonPropertyName("bets")]
        public BetsData? Bets { get; set; }
    }

    private class BetsData
    {
        [JsonPropertyName("pending")]
        public List<PendingBet> Pending { get; set; } = [];

        [JsonPropertyName("pendingNumber")]
        public int PendingNumber { get; set; }

        [JsonPropertyName("canSeeBets")]
        public bool CanSeeBets { get; set; }
    }

    private class BetOrderResponse
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }
    }

    private class TipstersResponse
    {
        [JsonPropertyName("tipsters")]
        public List<ApiTipster> Tipsters { get; set; } = [];

        [JsonPropertyName("pagination")]
        public ApiPagination? Pagination { get; set; }
    }

    private class ApiTipster
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("pro")]
        public bool Pro { get; set; }

        [JsonPropertyName("tier")]
        public string? Tier { get; set; }

        [JsonPropertyName("generalStatistics")]
        public ApiTipsterStatistics? GeneralStatistics { get; set; }
    }

    private class ApiTipsterStatistics
    {
        [JsonPropertyName("roi")]
        public decimal Roi { get; set; }

        [JsonPropertyName("betsNumber")]
        public int BetsNumber { get; set; }

        [JsonPropertyName("mostBetSport")]
        public string MostBetSport { get; set; } = string.Empty;
    }

    private class ApiPagination
    {
        [JsonPropertyName("nextPage")]
        public int? NextPage { get; set; }
    }
}
