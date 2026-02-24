using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public class MessageFormatter : IMessageFormatter
{
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
