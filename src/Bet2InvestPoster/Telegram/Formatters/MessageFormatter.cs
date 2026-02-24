using System.Text;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public class MessageFormatter : IMessageFormatter
{
    public string FormatHistory(List<HistoryEntry> entries)
    {
        if (entries.Count == 0)
            return "📭 Aucune publication dans l'historique.";

        var sb = new StringBuilder();
        sb.AppendLine($"📋 Historique des {entries.Count} dernières publications");

        // PublishedAt est stocké en UTC — le groupement par date est donc en UTC
        var groups = entries
            .GroupBy(e => e.PublishedAt.Date)
            .OrderByDescending(g => g.Key);

        foreach (var group in groups)
        {
            sb.AppendLine();
            sb.AppendLine($"📅 {group.Key:yyyy-MM-dd}");
            foreach (var entry in group.OrderByDescending(e => e.PublishedAt))
            {
                var time = entry.PublishedAt.ToString("HH:mm");
                var desc = !string.IsNullOrWhiteSpace(entry.MatchDescription)
                    ? entry.MatchDescription
                    : $"betId: {entry.BetId}";
                sb.AppendLine($"  • {time} — {desc}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatStatus(ExecutionState state)
    {
        var lastRun = state.LastRunAt.HasValue
            ? state.LastRunAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
            : "Aucune";

        string result;
        if (!state.LastRunSuccess.HasValue)
            result = "—";
        else if (state.LastRunSuccess.Value)
            result = $"✅ Succès — {state.LastRunResult}";
        else
            result = $"❌ Échec — {state.LastRunResult}";

        var nextRun = state.NextRunAt.HasValue
            ? state.NextRunAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
            : "Non planifié";

        var apiStatus = state.ApiConnected.HasValue
            ? (state.ApiConnected.Value ? "✅ Connecté" : "❌ Déconnecté")
            : "— Inconnu";

        return $"📊 État du système\n• Dernière exécution : {lastRun}\n• Résultat : {result}\n• Prochain run : {nextRun}\n• Connexion API : {apiStatus}";
    }
}
