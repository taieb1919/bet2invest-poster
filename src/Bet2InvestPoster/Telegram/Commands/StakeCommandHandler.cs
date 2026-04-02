using System.Text;
using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class StakeCommandHandler : ICommandHandler
{
    private readonly PosterOptions _options;

    public StakeCommandHandler(IOptions<PosterOptions> options)
    {
        _options = options.Value;
    }

    public bool CanHandle(string command) => command == "/stake";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var rules = _options.StakeRules;

        if (rules is not { Count: > 0 })
        {
            await bot.SendMessage(message.Chat.Id,
                "📊 Grille de stake active\n\nAucune règle configurée — stake par défaut : 1u",
                cancellationToken: ct);
            return;
        }

        var sorted = rules.OrderBy(r => r.MaxOdds ?? decimal.MaxValue).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("📊 Grille de stake active");
        sb.AppendLine();

        for (var i = 0; i < sorted.Count; i++)
        {
            var rule = sorted[i];
            var prevMax = i == 0 ? null : sorted[i - 1].MaxOdds;
            string range;
            if (i == 0 && rule.MaxOdds.HasValue)
                range = $"Cotes < {rule.MaxOdds.Value:F2}";
            else if (!rule.MaxOdds.HasValue && prevMax.HasValue)
                range = $"Cotes > {prevMax.Value:F2}";
            else if (prevMax.HasValue && rule.MaxOdds.HasValue)
                range = $"Cotes {prevMax.Value:F2} – {rule.MaxOdds.Value:F2}";
            else
                range = "Cotes (toutes)";

            sb.AppendLine($"{range} → {rule.Units}u");
        }

        await bot.SendMessage(message.Chat.Id, sb.ToString().TrimEnd(), cancellationToken: ct);
    }
}
