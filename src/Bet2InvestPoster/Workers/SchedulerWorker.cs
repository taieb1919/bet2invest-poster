using System.Globalization;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Serilog.Context;

namespace Bet2InvestPoster.Workers;

public class SchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IExecutionStateService _executionStateService;
    private readonly IResiliencePipelineService _resiliencePipelineService;
    private readonly INotificationService _notificationService;
    private readonly int _maxRetryCount;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerWorker> _logger;
    private string[]? _lastScheduleTimes;

    public SchedulerWorker(
        IServiceProvider serviceProvider,
        IExecutionStateService executionStateService,
        IResiliencePipelineService resiliencePipelineService,
        INotificationService notificationService,
        IOptions<PosterOptions> options,
        TimeProvider timeProvider,
        ILogger<SchedulerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _executionStateService = executionStateService;
        _resiliencePipelineService = resiliencePipelineService;
        _notificationService = notificationService;
        _maxRetryCount = options.Value.MaxRetryCount;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (LogContext.PushProperty("Step", "Schedule"))
        {
            var times = _executionStateService.GetScheduleTimes();
            _logger.LogInformation(
                "SchedulerWorker démarré — exécutions planifiées à {ScheduleTimes} chaque jour",
                string.Join(", ", times));
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentScheduleTimes = _executionStateService.GetScheduleTimes();
            if (_lastScheduleTimes is not null && !currentScheduleTimes.SequenceEqual(_lastScheduleTimes))
            {
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogInformation(
                        "Horaires de scheduling modifiés : {OldTimes} → {NewTimes}",
                        string.Join(", ", _lastScheduleTimes),
                        string.Join(", ", currentScheduleTimes));
                }
            }
            _lastScheduleTimes = currentScheduleTimes;

            // Attente que le scheduling soit activé (vérification toutes les 5s)
            while (!_executionStateService.GetSchedulingEnabled() && !stoppingToken.IsCancellationRequested)
            {
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogDebug("Scheduling suspendu — vérification dans 5s");
                }
                await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            var nextRun = CalculateNextRun();
            _executionStateService.SetNextRun(nextRun);

            using (LogContext.PushProperty("Step", "Schedule"))
            {
                _logger.LogInformation(
                    "Prochain run planifié : {NextRun:yyyy-MM-dd HH:mm:ss} UTC", nextRun);
            }

            var delay = nextRun - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _timeProvider, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
                break;

            using (LogContext.PushProperty("Step", "Schedule"))
            {
                _logger.LogInformation("Déclenchement cycle planifié");
            }

            try
            {
                await _resiliencePipelineService.ExecuteCycleWithRetryAsync(async ct =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
                    await cycleService.RunCycleAsync(ct);
                }, stoppingToken);
            }
            catch (BrokenCircuitException)
            {
                var remaining = _resiliencePipelineService.GetCircuitBreakerRemainingDuration();
                var minutes = remaining?.TotalMinutes ?? 5;
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogWarning("Circuit breaker actif — cycle ignoré. Réessai dans {Minutes:F0} min.", minutes);
                }
                using var cbCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _notificationService.SendMessageAsync(
                    $"🔴 Circuit breaker actif — service API indisponible. Réessai automatique dans {minutes:F0} min.",
                    cbCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogError(ex, "Toutes les tentatives épuisées — cycle définitivement échoué");
                }
                using var failCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _notificationService.NotifyFinalFailureAsync(
                    _maxRetryCount, ex.GetType().Name, failCts.Token);
            }
        }
    }

    internal DateTimeOffset CalculateNextRun()
    {
        var scheduleTimes = _executionStateService.GetScheduleTimes();
        var now = _timeProvider.GetUtcNow();

        DateTimeOffset? closest = null;
        foreach (var timeStr in scheduleTimes)
        {
            if (!TimeOnly.TryParseExact(timeStr, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogWarning("Horaire invalide ignoré : '{TimeStr}' — format attendu HH:mm", timeStr);
                }
                continue;
            }

            var todayAt = new DateTimeOffset(
                now.Year, now.Month, now.Day,
                t.Hour, t.Minute, 0, TimeSpan.Zero);

            var candidate = todayAt > now ? todayAt : todayAt.AddDays(1);
            if (closest is null || candidate < closest)
                closest = candidate;
        }

        return closest ?? now.AddDays(1);
    }
}
