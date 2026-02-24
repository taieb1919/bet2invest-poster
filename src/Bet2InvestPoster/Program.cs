using Bet2InvestPoster;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using JTDev.Bet2InvestScraper.Api;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

// UseSystemd integration for VPS deployment
builder.Services.AddSystemd();

// Read log path early from configuration before Serilog setup
var logPath = builder.Configuration.GetValue<string>("Poster:LogPath") ?? "logs";

// Console: clean human-readable output without JSON property blob
const string consoleTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{Step}] {Message:lj}{NewLine}{Exception}";
// File: full structured output with properties for analysis/audit
const string fileTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{Step}] {Message:lj} {Properties:j}{NewLine}{Exception}";

// Configure Serilog with console + file sinks
builder.Services.AddSerilog(lc => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: consoleTemplate)
    .WriteTo.File(
        path: Path.Combine(logPath, "bet2invest-poster-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: fileTemplate));

// Register Options — env vars automatically override appsettings.json via Generic Host
// (set via Bet2Invest__Identifier, Bet2Invest__Password, Telegram__BotToken, Telegram__AuthorizedChatId)
builder.Services.Configure<Bet2InvestOptions>(
    builder.Configuration.GetSection(Bet2InvestOptions.SectionName));
builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<PosterOptions>(
    builder.Configuration.GetSection(PosterOptions.SectionName));

builder.Services.AddHostedService<Worker>();

// Bet2InvestClient from the scraper submodule: Singleton — one instance shared across cycles.
// Uses an adapter to bridge the scraper's IConsoleLogger to Microsoft.Extensions.Logging.
builder.Services.AddSingleton<Bet2InvestClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<Bet2InvestClient>>();
    return new Bet2InvestClient(opts.ApiBase, opts.RequestDelayMs, new SerilogConsoleLoggerAdapter(logger));
});

// ExtendedBet2InvestClient: Scoped — one instance per execution cycle.
builder.Services.AddScoped<IExtendedBet2InvestClient, ExtendedBet2InvestClient>();

// TipsterService: Scoped — reads tipsters.json on every cycle.
builder.Services.AddScoped<ITipsterService, TipsterService>();

// UpcomingBetsFetcher: Scoped — fetches and aggregates upcoming bets per cycle.
builder.Services.AddScoped<IUpcomingBetsFetcher, UpcomingBetsFetcher>();

// HistoryManager: Scoped — reads/writes history.json atomically per cycle.
builder.Services.AddScoped<IHistoryManager, HistoryManager>();

var host = builder.Build();

// Fast-fail: validate required credentials before starting
// Credentials must be provided via environment variables, never in appsettings.json
var b2iOpts = host.Services.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;
var tgOpts = host.Services.GetRequiredService<IOptions<TelegramOptions>>().Value;
var missingVars = new List<string>();
if (string.IsNullOrWhiteSpace(b2iOpts.Identifier)) missingVars.Add("Bet2Invest__Identifier");
if (string.IsNullOrWhiteSpace(b2iOpts.Password)) missingVars.Add("Bet2Invest__Password");
if (string.IsNullOrWhiteSpace(tgOpts.BotToken)) missingVars.Add("Telegram__BotToken");
if (tgOpts.AuthorizedChatId == 0) missingVars.Add("Telegram__AuthorizedChatId");
if (missingVars.Count > 0)
    throw new InvalidOperationException(
        $"Required environment variables not configured: {string.Join(", ", missingVars)}");

host.Run();
