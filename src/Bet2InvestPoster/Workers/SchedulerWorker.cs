using System.Globalization;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Workers;

public class SchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IExecutionStateService _executionStateService;
    private readonly TimeOnly _scheduleTime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerWorker> _logger;

    public SchedulerWorker(
        IServiceProvider serviceProvider,
        IExecutionStateService executionStateService,
        IOptions<PosterOptions> options,
        TimeProvider timeProvider,
        ILogger<SchedulerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _executionStateService = executionStateService;
        _scheduleTime = TimeOnly.Parse(options.Value.ScheduleTime, CultureInfo.InvariantCulture);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (LogContext.PushProperty("Step", "Schedule"))
        {
            _logger.LogInformation(
                "SchedulerWorker démarré — exécution planifiée à {ScheduleTime} chaque jour",
                _scheduleTime);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
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
                using var scope = _serviceProvider.CreateScope();
                var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
                await cycleService.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PostingCycleService a déjà loggé, notifié et mis à jour l'état.
                // Story 5.2 ajoutera le retry Polly ici.
                // On continue la boucle pour le prochain run quotidien.
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogError(ex, "Cycle échoué — reprise au prochain run planifié");
                }
            }
        }
    }

    internal DateTimeOffset CalculateNextRun()
    {
        var now = _timeProvider.GetUtcNow();
        var todayAtSchedule = new DateTimeOffset(
            now.Year, now.Month, now.Day,
            _scheduleTime.Hour, _scheduleTime.Minute, 0, TimeSpan.Zero);

        return todayAtSchedule > now ? todayAtSchedule : todayAtSchedule.AddDays(1);
    }
}
