using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class RunCommandHandler : ICommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IResiliencePipelineService _resiliencePipelineService;
    private readonly IExecutionStateService _stateService;
    private readonly int _maxRetryCount;
    private readonly ILogger<RunCommandHandler> _logger;

    public RunCommandHandler(
        IServiceScopeFactory scopeFactory,
        IResiliencePipelineService resiliencePipelineService,
        IExecutionStateService stateService,
        IOptions<PosterOptions> options,
        ILogger<RunCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _resiliencePipelineService = resiliencePipelineService;
        _stateService = stateService;
        _maxRetryCount = options.Value.MaxRetryCount;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/run";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /run reçue — déclenchement cycle");
        }

        try
        {
            await _resiliencePipelineService.ExecuteCycleWithRetryAsync(async token =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
                await cycleService.RunCycleAsync(token);
            }, ct);

            var state = _stateService.GetState();

            using (LogContext.PushProperty("Step", "Notify"))
            {
                _logger.LogInformation("Cycle /run terminé avec succès");
            }

            var resultDetail = state.LastRunResult is not null
                ? $" — {state.LastRunResult}"
                : "";
            await bot.SendMessage(chatId, $"✅ Cycle exécuté avec succès{resultDetail}.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("Step", "Notify"))
            {
                _logger.LogError(ex, "Erreur lors de l'exécution du cycle via /run — toutes tentatives épuisées");
            }

            await bot.SendMessage(
                chatId,
                $"❌ Échec définitif après {_maxRetryCount} tentatives — {ex.GetType().Name}",
                cancellationToken: ct);
        }
    }
}
