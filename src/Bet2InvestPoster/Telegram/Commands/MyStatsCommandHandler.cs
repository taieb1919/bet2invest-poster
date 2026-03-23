using Bet2InvestPoster.Services;
using Bet2InvestPoster.Telegram.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class MyStatsCommandHandler : ICommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<MyStatsCommandHandler> _logger;

    public MyStatsCommandHandler(
        IServiceScopeFactory scopeFactory,
        IMessageFormatter formatter,
        ILogger<MyStatsCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _formatter = formatter;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/mystats";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /mystats reçue");

            await bot.SendMessage(chatId, "⏳ Récupération des stats...", cancellationToken: ct);

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var client = scope.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();
                var stats = await client.GetUserStatsAsync(ct);
                var text = _formatter.FormatMyStats(stats);

                await bot.SendMessage(chatId, text, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des stats");
                await bot.SendMessage(chatId,
                    $"❌ Erreur : {ex.GetType().Name} — {ex.Message}",
                    cancellationToken: ct);
            }
        }
    }
}
