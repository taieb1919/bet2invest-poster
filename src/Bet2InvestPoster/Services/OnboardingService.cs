using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class OnboardingService : IOnboardingService
{
    private readonly IHistoryManager _historyManager;
    private readonly IExecutionStateService _executionStateService;
    private readonly INotificationService _notificationService;
    private readonly IMessageFormatter _messageFormatter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        IHistoryManager historyManager,
        IExecutionStateService executionStateService,
        INotificationService notificationService,
        IMessageFormatter messageFormatter,
        IServiceScopeFactory scopeFactory,
        ILogger<OnboardingService> logger)
    {
        _historyManager = historyManager;
        _executionStateService = executionStateService;
        _notificationService = notificationService;
        _messageFormatter = messageFormatter;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task TrySendOnboardingAsync(CancellationToken ct = default)
    {
        try
        {
            using (LogContext.PushProperty("Step", "Onboarding"))
            {
                var keys = await _historyManager.LoadPublishedKeysAsync(ct);
                if (keys.Count > 0)
                {
                    _logger.LogInformation("Onboarding ignoré — historique existant ({Count} entrées)", keys.Count);
                    return;
                }

                _logger.LogInformation("Premier démarrage détecté — envoi du message d'onboarding");

                var apiConnected = false;
                var tipsterCount = 0;

                using (var scope = _scopeFactory.CreateScope())
                {
                    var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();
                    var apiClient = scope.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();

                    try
                    {
                        await apiClient.LoginAsync(ct);
                        apiConnected = true;
                        _logger.LogInformation("Onboarding — connexion API réussie");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Onboarding — connexion API échouée");
                    }

                    try
                    {
                        var tipsters = await tipsterService.LoadTipstersAsync(ct);
                        tipsterCount = tipsters.Count;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Onboarding — impossible de charger les tipsters");
                    }
                }

                var scheduleTimes = _executionStateService.GetScheduleTimes();
                var message = _messageFormatter.FormatOnboardingMessage(apiConnected, tipsterCount, scheduleTimes);

                await _notificationService.SendMessageAsync(message, ct);
                _logger.LogInformation("Message d'onboarding envoyé avec succès");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Onboarding échoué — non bloquant");
        }
    }
}
