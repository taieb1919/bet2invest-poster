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
        sb.AppendLine("📋 Historique des dernières publications");

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

    public string FormatTipsters(List<TipsterConfig> tipsters)
    {
        if (tipsters.Count == 0)
            return "📭 Aucun tipster configuré. Utilisez /tipsters add <lien> pour en ajouter.";

        var sb = new StringBuilder();
        sb.AppendLine("📋 Tipsters configurés");
        sb.AppendLine();

        for (var i = 0; i < tipsters.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {tipsters[i].Name} — {tipsters[i].Url} (free)");
        }

        sb.AppendLine();
        sb.Append($"Total : {tipsters.Count} tipster{(tipsters.Count > 1 ? "s" : "")}");

        return sb.ToString().TrimEnd();
    }

    public string FormatOnboardingMessage(bool apiConnected, int tipsterCount, string scheduleTime)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🚀 Bienvenue sur bet2invest-poster !");
        sb.AppendLine();

        if (apiConnected)
            sb.AppendLine("📡 Connexion API bet2invest : ✅ Connecté");
        else
            sb.AppendLine("⚠️ Connexion API bet2invest échouée — vérifiez vos credentials.");

        sb.AppendLine($"👥 Tipsters configurés : {tipsterCount}");
        sb.AppendLine($"⏰ Publication planifiée : {scheduleTime}");
        sb.AppendLine();
        sb.AppendLine("📋 Commandes disponibles :");
        sb.AppendLine("  /run — lancer une publication manuelle");
        sb.AppendLine("  /status — état du système");
        sb.AppendLine("  /start — activer le scheduling");
        sb.AppendLine("  /stop — désactiver le scheduling");
        sb.AppendLine("  /history — historique des publications");
        sb.AppendLine("  /schedule — configurer l'horaire");
        sb.AppendLine("  /tipsters — gérer les tipsters");
        sb.AppendLine();
        if (apiConnected)
            sb.Append("💡 Envoyez /run pour tester une première publication, ou /status pour vérifier l'état.");
        else
            sb.Append("⚠️ Corrigez vos credentials avant d'utiliser /run.");

        return sb.ToString();
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
