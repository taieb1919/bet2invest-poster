using Bet2InvestPoster.Services;

namespace Bet2InvestPoster;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker démarré — exécution du cycle de publication");

        try
        {
            // Epic 5 ajoutera le scheduling quotidien (SchedulerWorker).
            // Pour l'instant : exécution unique au démarrage pour valider le cycle complet.
            using var scope = _serviceProvider.CreateScope();
            var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
            await cycleService.RunCycleAsync(stoppingToken);

            _logger.LogInformation("Cycle terminé — Worker en attente");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker arrêté par annulation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur fatale durant le cycle de publication");
            throw;
        }
    }
}
