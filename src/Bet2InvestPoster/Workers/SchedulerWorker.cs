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
    private string? _lastScheduleTime;

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
            _logger.LogInformation(
                "SchedulerWorker démarré — exécution planifiée à {ScheduleTime} chaque jour",
                _executionStateService.GetScheduleTime());
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentScheduleTime = _executionStateService.GetScheduleTime();
            if (_lastScheduleTime is not null && _lastScheduleTime != currentScheduleTime)
            {
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogInformation(
                        "Heure de scheduling modifiée : {OldTime} → {NewTime}",
                        _lastScheduleTime, currentScheduleTime);
                }
            }
            _lastScheduleTime = currentScheduleTime;

            // Attente que le scheduling soit activé (vérification toutes les 5s)
            // Vérifié AVANT le calcul du prochain run pour réagir immédiatement au /stop
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
                // Toutes les tentatives Polly épuisées — notifier l'échec définitif (FR18)
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
        var scheduleTime = TimeOnly.Parse(_executionStateService.GetScheduleTime(), CultureInfo.InvariantCulture);
        var now = _timeProvider.GetUtcNow();
        var todayAtSchedule = new DateTimeOffset(
            now.Year, now.Month, now.Day,
            scheduleTime.Hour, scheduleTime.Minute, 0, TimeSpan.Zero);

        return todayAtSchedule > now ? todayAtSchedule : todayAtSchedule.AddDays(1);
    }
}
