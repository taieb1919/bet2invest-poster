using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class TipstersCommandHandler : ICommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<TipstersCommandHandler> _logger;

    public TipstersCommandHandler(
        IServiceScopeFactory scopeFactory,
        IMessageFormatter formatter,
        ILogger<TipstersCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _formatter = formatter;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/tipsters";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /tipsters reçue");

            // Vérifier si des arguments sont présents (add, remove — réservés story 8.2)
            var parts = (message.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                await bot.SendMessage(chatId,
                    "Les sous-commandes /tipsters add et /tipsters remove seront disponibles prochainement.",
                    cancellationToken: ct);
                return;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();

                var tipsters = await tipsterService.LoadTipstersAsync(ct);
                var text = _formatter.FormatTipsters(tipsters);

                await bot.SendMessage(chatId, text, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du chargement des tipsters");
                await bot.SendMessage(chatId, "❌ Erreur lors de la lecture des tipsters.", cancellationToken: ct);
            }
        }
    }
}
