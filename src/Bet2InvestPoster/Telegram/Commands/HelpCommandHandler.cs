using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bet2InvestPoster.Telegram.Commands;

public class HelpCommandHandler : ICommandHandler
{
    private const string HelpMessage =
        "<b>Commandes disponibles</b>\n\n" +
        "/run — Exécuter un cycle de publication\n" +
        "/status — Afficher l'état du système\n" +
        "/start — Activer le scheduling automatique\n" +
        "/stop — Suspendre le scheduling automatique\n" +
        "/history — Historique des publications récentes\n" +
        "/schedule [HH:mm,...] — Configurer les horaires d'exécution\n" +
        "/tipsters — Gérer la liste des tipsters\n" +
        "/report [jours] — Tableau de bord des performances\n" +
        "/help — Afficher cette aide";

    public bool CanHandle(string command) => command == "/help";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMessage(
            message.Chat.Id,
            HelpMessage,
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }
}
