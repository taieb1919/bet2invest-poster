using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram;
using Bet2InvestPoster.Telegram.Commands;
using Bet2InvestPoster.Telegram.Formatters;
using Bet2InvestPoster.Workers;
using JTDev.Bet2InvestScraper.Api;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// UseSystemd integration for VPS deployment
builder.Services.AddSystemd();

// Read log path early from configuration before Serilog setup
var logPath = builder.Configuration.GetValue<string>("Poster:LogPath") ?? "logs";
// Read log retention early (same pattern as LogPath — needed before Serilog setup)
var logRetentionDays = builder.Configuration.GetValue<int?>("Poster:LogRetentionDays") ?? 30;
if (logRetentionDays <= 0)
    throw new InvalidOperationException(
        $"Poster:LogRetentionDays doit être > 0 (valeur actuelle : {logRetentionDays})");

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
        retainedFileCountLimit: logRetentionDays,
        outputTemplate: fileTemplate));

// Register Options — env vars automatically override appsettings.json via Generic Host
// (set via Bet2Invest__Identifier, Bet2Invest__Password, Telegram__BotToken, Telegram__AuthorizedChatId)
builder.Services.Configure<Bet2InvestOptions>(
    builder.Configuration.GetSection(Bet2InvestOptions.SectionName));
builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<PosterOptions>(
    builder.Configuration.GetSection(PosterOptions.SectionName));

// TimeProvider: Singleton — system clock used by SchedulerWorker (overridden in tests via FakeTimeProvider)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SchedulerWorker>();

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

// HistoryManager: Singleton — SemaphoreSlim must be shared across all cycles (scheduler + /run).
// Scoped would create a new instance per cycle, making the semaphore ineffective for inter-cycle protection.
builder.Services.AddSingleton<IHistoryManager, HistoryManager>();

// BetSelector: Scoped — filters duplicates and randomly selects 5/10/15 bets per cycle.
builder.Services.AddScoped<IBetSelector, BetSelector>();

// BetPublisher: Scoped — publishes selected bets via API and records them in history.
builder.Services.AddScoped<IBetPublisher, BetPublisher>();

// ResultTracker: Scoped — vérifie les résultats des pronostics publiés une fois par cycle.
builder.Services.AddScoped<IResultTracker, ResultTracker>();

// PostingCycleService: Scoped — orchestrates the full posting cycle per execution.
builder.Services.AddScoped<IPostingCycleService, PostingCycleService>();

// ResiliencePipelineService: Singleton — builds ResiliencePipeline once from config.
builder.Services.AddSingleton<IResiliencePipelineService, ResiliencePipelineService>();

// AuthorizationFilter: Singleton — filters authorized chat ID for Telegram commands.
builder.Services.AddSingleton<AuthorizationFilter>();

// ITelegramBotClient: Singleton — shared between TelegramBotService (polling) and NotificationService (outgoing).
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramBotClient(opts.BotToken);
});

// NotificationService: Singleton — sole service authorized to send outgoing Telegram messages.
builder.Services.AddSingleton<INotificationService, NotificationService>();

// ExecutionStateService: Singleton — tracks last/next run state et scheduling enabled/disabled.
// DataPath injecté pour la persistance de l'état de scheduling (scheduling-state.json).
builder.Services.AddSingleton<IExecutionStateService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PosterOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<ExecutionStateService>>();
    return new ExecutionStateService(opts.DataPath, opts.ScheduleTime, logger);
});

// MessageFormatter: Singleton — formats Telegram status messages.
builder.Services.AddSingleton<IMessageFormatter, MessageFormatter>();

// ConversationStateService: Singleton — état de conversation partagé entre tous les scopes.
builder.Services.AddSingleton<IConversationStateService, ConversationStateService>();

// OnboardingService: Singleton — sends onboarding message on first launch.
builder.Services.AddSingleton<IOnboardingService, OnboardingService>();

// Command handlers: Singleton — /run, /status, /start, /stop, /history, /schedule dispatch.
builder.Services.AddSingleton<ICommandHandler, RunCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StatusCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StartCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StopCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, HistoryCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ScheduleCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, TipstersCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ReportCommandHandler>();

// TelegramBotService: HostedService — bot long polling running in background.
builder.Services.AddHostedService<TelegramBotService>();

// Health checks — NFR15
builder.Services.AddHealthChecks()
    .AddCheck<Bet2InvestHealthCheck>("bet2invest");

// Kestrel: écoute uniquement sur le port health check (pas le port HTTP standard)
var healthCheckPort = builder.Configuration.GetValue<int?>("Poster:HealthCheckPort") ?? 8080;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(healthCheckPort);
});

var app = builder.Build();

// Fast-fail: validate required credentials before starting
// Credentials must be provided via environment variables, never in appsettings.json
var b2iOpts = app.Services.GetRequiredService<IOptions<Bet2InvestOptions>>().Value;
var tgOpts = app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value;
var posterOpts = app.Services.GetRequiredService<IOptions<PosterOptions>>().Value;
var missingVars = new List<string>();
if (string.IsNullOrWhiteSpace(b2iOpts.Identifier)) missingVars.Add("Bet2Invest__Identifier");
if (string.IsNullOrWhiteSpace(b2iOpts.Password)) missingVars.Add("Bet2Invest__Password");
if (string.IsNullOrWhiteSpace(tgOpts.BotToken)) missingVars.Add("Telegram__BotToken");
if (tgOpts.AuthorizedChatId == 0) missingVars.Add("Telegram__AuthorizedChatId");
if (string.IsNullOrWhiteSpace(posterOpts.BankrollId)) missingVars.Add("Poster__BankrollId");
if (missingVars.Count > 0)
    throw new InvalidOperationException(
        $"Required environment variables not configured: {string.Join(", ", missingVars)}");

// Poster__BankrollId doit être un entier valide (utilisé par int.Parse dans BetPublisher)
if (!int.TryParse(posterOpts.BankrollId, out _))
    throw new InvalidOperationException(
        $"Poster:BankrollId doit être un entier valide (valeur actuelle : '{posterOpts.BankrollId}')");

// Validation des filtres avancés — log warnings si configuration incohérente
if (posterOpts.MinOdds.HasValue && posterOpts.MinOdds.Value <= 0)
    Log.Warning("Configuration: Poster:MinOdds ({MinOdds}) est <= 0 — aucun pari ne sera sélectionné", posterOpts.MinOdds);
if (posterOpts.MaxOdds.HasValue && posterOpts.MaxOdds.Value <= 0)
    Log.Warning("Configuration: Poster:MaxOdds ({MaxOdds}) est <= 0 — aucun pari ne sera sélectionné", posterOpts.MaxOdds);
if (posterOpts.MinOdds.HasValue && posterOpts.MaxOdds.HasValue && posterOpts.MinOdds > posterOpts.MaxOdds)
    Log.Warning("Configuration: Poster:MinOdds ({MinOdds}) > Poster:MaxOdds ({MaxOdds}) — aucun pari ne correspondra aux filtres", posterOpts.MinOdds, posterOpts.MaxOdds);
if (posterOpts.EventHorizonHours.HasValue && posterOpts.EventHorizonHours.Value <= 0)
    Log.Warning("Configuration: Poster:EventHorizonHours ({EventHorizonHours}) est <= 0 — tous les paris seront exclus", posterOpts.EventHorizonHours);

// NFR8 : délai minimum 500ms entre requêtes API (rate limiting)
if (b2iOpts.RequestDelayMs < 500)
    throw new InvalidOperationException(
        $"Bet2Invest:RequestDelayMs doit être >= 500ms (valeur actuelle : {b2iOpts.RequestDelayMs}ms)");

// Délai minimum 1000ms entre tentatives Polly (évite les retries instantanés en boucle)
if (posterOpts.RetryDelayMs < 1000)
    throw new InvalidOperationException(
        $"Poster:RetryDelayMs doit être >= 1000ms (valeur actuelle : {posterOpts.RetryDelayMs}ms)");

if (posterOpts.CircuitBreakerFailureThreshold <= 0)
    throw new InvalidOperationException(
        $"Poster:CircuitBreakerFailureThreshold doit être > 0 (valeur actuelle : {posterOpts.CircuitBreakerFailureThreshold})");
if (posterOpts.CircuitBreakerDurationSeconds <= 0)
    throw new InvalidOperationException(
        $"Poster:CircuitBreakerDurationSeconds doit être > 0 (valeur actuelle : {posterOpts.CircuitBreakerDurationSeconds})");
if (posterOpts.HealthCheckPort is < 1 or > 65535)
    throw new InvalidOperationException(
        $"Poster:HealthCheckPort doit être entre 1 et 65535 (valeur actuelle : {posterOpts.HealthCheckPort})");

app.MapHealthChecks("/health");

app.Run();
