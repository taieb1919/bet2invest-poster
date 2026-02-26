using System.Text;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public class MessageFormatter : IMessageFormatter
{
    private const int MaxDisplayedBets = 15;
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

    public string FormatOnboardingMessage(bool apiConnected, int tipsterCount, string[] scheduleTimes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🚀 Bienvenue sur bet2invest-poster !");
        sb.AppendLine();

        if (apiConnected)
            sb.AppendLine("📡 Connexion API bet2invest : ✅ Connecté");
        else
            sb.AppendLine("⚠️ Connexion API bet2invest échouée — vérifiez vos credentials.");

        sb.AppendLine($"👥 Tipsters configurés : {tipsterCount}");
        sb.AppendLine($"⏰ Publications planifiées : {string.Join(", ", scheduleTimes)}");
        sb.AppendLine();
        sb.AppendLine("📋 Commandes disponibles :");
        sb.AppendLine("  /run — lancer une publication manuelle");
        sb.AppendLine("  /status — état du système");
        sb.AppendLine("  /start — activer le scheduling");
        sb.AppendLine("  /stop — désactiver le scheduling");
        sb.AppendLine("  /history — historique des publications");
        sb.AppendLine("  /report — rapport de performances");
        sb.AppendLine("  /schedule — configurer l'horaire");
        sb.AppendLine("  /tipsters — gérer les tipsters");
        sb.AppendLine();
        if (apiConnected)
            sb.Append("💡 Envoyez /run pour tester une première publication, ou /status pour vérifier l'état.");
        else
            sb.Append("⚠️ Corrigez vos credentials avant d'utiliser /run.");

        return sb.ToString();
    }

    public string FormatScrapedTipsters(List<ScrapedTipster> tipsters)
    {
        if (tipsters.Count == 0)
            return "📭 Aucun tipster gratuit trouvé sur bet2invest.";

        var sb = new StringBuilder();
        sb.AppendLine($"🔍 {tipsters.Count} tipsters free trouvés (triés par ROI)");
        sb.AppendLine();

        for (var i = 0; i < tipsters.Count; i++)
        {
            var t = tipsters[i];
            var roi = t.Roi >= 0 ? $"+{t.Roi:F1}%" : $"{t.Roi:F1}%";
            sb.AppendLine($"{i + 1}. {t.Username} — ROI: {roi} | {t.BetsNumber} paris | {t.MostBetSport}");
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatScrapedTipstersConfirmation()
        => "Voulez-vous remplacer votre liste actuelle ?\n[Oui / Non / Fusionner]";

    public string FormatReport(List<HistoryEntry> entries, int days)
    {
        var resolved = entries.Where(e => e.Result is "won" or "lost").ToList();

        if (resolved.Count == 0)
            return "📊 Aucun pronostic résolu sur cette période. Les résultats sont vérifiés quotidiennement.";

        var won = resolved.Where(e => e.Result == "won").ToList();
        var pending = entries.Where(e => e.Result is "pending" or null).ToList();

        var winRate = (double)won.Count / resolved.Count * 100;

        var totalStake = (double)resolved.Count;
        var totalReturn = won.Sum(e => (double)(e.Odds ?? 0m));
        var roi = totalStake > 0 ? (totalReturn - totalStake) / totalStake * 100 : 0;

        var avgOdds = resolved.Count > 0 ? resolved.Average(e => (double)(e.Odds ?? 0m)) : 0;

        var sb = new StringBuilder();
        sb.AppendLine($"📊 Rapport — {days} jour{(days > 1 ? "s" : "")}");
        sb.AppendLine();
        sb.AppendLine("📋 Résumé");
        sb.AppendLine($"• Pronostics publiés : {entries.Count}");
        sb.AppendLine($"• Résultats disponibles : {resolved.Count} / {entries.Count}");
        sb.AppendLine($"• En attente : {pending.Count}");
        sb.AppendLine();
        sb.AppendLine("📈 Performances");
        sb.AppendLine($"• Taux de réussite : {winRate:F1}% ({won.Count}/{resolved.Count})");
        var roiStr = roi >= 0 ? $"+{roi:F1}%" : $"{roi:F1}%";
        sb.AppendLine($"• ROI : {roiStr}");
        sb.AppendLine($"• Cote moyenne : {avgOdds:F2}");

        // Répartition par sport
        var bySport = entries
            .GroupBy(e => e.Sport ?? "Inconnu")
            .Select(g => new
            {
                Sport = g.Key,
                Won = g.Count(e => e.Result == "won"),
                Lost = g.Count(e => e.Result == "lost"),
                Pending = g.Count(e => e.Result is "pending" or null),
                Total = g.Count(e => e.Result is "won" or "lost")
            })
            .OrderByDescending(s => s.Won + s.Lost)
            .ToList();

        if (bySport.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚽ Par sport");
            foreach (var s in bySport)
            {
                var sr = s.Total > 0 ? $" ({(double)s.Won / s.Total * 100:F1}%)" : "";
                sb.AppendLine($"• {s.Sport} : {s.Won} ✅ {s.Lost} ❌ {s.Pending}{sr}");
            }
        }

        // Top 3 tipsters
        var topTipsters = resolved
            .GroupBy(e => e.TipsterName ?? "Inconnu")
            .Select(g => new
            {
                Name = g.Key,
                WinRate = (double)g.Count(e => e.Result == "won") / g.Count() * 100,
                Won = g.Count(e => e.Result == "won"),
                Count = g.Count()
            })
            .Where(t => t.Count >= 2)
            .OrderByDescending(t => t.WinRate)
            .ThenByDescending(t => t.Count)
            .Take(3)
            .ToList();

        if (topTipsters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("🏆 Top tipsters");
            for (var i = 0; i < topTipsters.Count; i++)
            {
                var t = topTipsters[i];
                sb.AppendLine($"{i + 1}. {t.Name} — {t.WinRate:F1}% ({t.Won}/{t.Count})");
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

        var scheduleTimes = string.Join(", ", state.ScheduleTimes);
        return $"📊 État du système\n• Dernière exécution : {lastRun}\n• Résultat : {result}\n• Horaires configurés : {scheduleTimes}\n• Prochain run : {nextRun}\n• Connexion API : {apiStatus}";
    }

    public string FormatCycleSuccess(CycleResult result)
    {
        if (result.ScrapedCount == 0)
            return "⚠️ Aucun pronostic disponible chez les tipsters configurés.";

        string summary;
        if (result.HasActiveFilters)
        {
            var icon = result.PublishedCount == 0 ? "⚠️" : "✅";
            summary = $"{icon} {result.PublishedCount}/{result.FilteredCount} filtrés sur {result.ScrapedCount} scrapés.";
        }
        else
        {
            summary = $"✅ {result.PublishedCount} pronostics publiés sur {result.ScrapedCount} scrapés.";
        }

        if (result.PublishedBets.Count == 0)
            return summary;

        var sb = new StringBuilder();
        sb.AppendLine(summary);

        var bets = result.PublishedBets;
        var displayCount = Math.Min(bets.Count, MaxDisplayedBets);
        sb.AppendLine();

        for (var i = 0; i < displayCount; i++)
        {
            var bet = bets[i];
            var matchDesc = bet.Event?.Home != null && bet.Event?.Away != null
                ? $"{bet.Event.Home} vs {bet.Event.Away}"
                : "(sans description)";
            var tipster = bet.TipsterUsername ?? "inconnu";
            sb.AppendLine($"• {matchDesc} — {bet.Price:F2} ({tipster})");
        }

        if (bets.Count > MaxDisplayedBets)
            sb.AppendLine($"... et {bets.Count - MaxDisplayedBets} autres");

        return sb.ToString().TrimEnd();
    }

    public string FormatPreview(PreviewSession session)
    {
        var selectedCount = session.Selected.Count(s => s);
        var sb = new StringBuilder();
        sb.AppendLine($"👁 Aperçu — {selectedCount}/{session.Bets.Count} sélectionnés");
        sb.AppendLine($"({session.PartialCycleResult.ScrapedCount} scrapés, {session.PartialCycleResult.FilteredCount} filtrés)");
        sb.AppendLine();

        for (var i = 0; i < session.Bets.Count; i++)
        {
            var bet = session.Bets[i];
            var icon = session.Selected[i] ? "✅" : "❌";
            var matchDesc = bet.Event?.Home != null && bet.Event?.Away != null
                ? $"{bet.Event.Home} vs {bet.Event.Away}"
                : "(sans description)";
            var pick = FormatPick(bet);
            var tipster = bet.TipsterUsername ?? "inconnu";
            sb.AppendLine($"{icon} {i + 1}. {matchDesc} — {pick} @ {bet.Price:F2} ({tipster})");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatPick(PendingBet bet)
    {
        var designation = bet.DerivedDesignation;
        var type = bet.Type;

        // Traduire le type de pari en label court
        var typeLabel = type switch
        {
            "MONEYLINE" => "1X2",
            "SPREAD" => "Handicap",
            "TOTAL_POINTS" => "O/U",
            "TEAM_TOTAL_POINTS" => "Team O/U",
            _ => type
        };

        // Récupérer les points (ligne) depuis le market price correspondant
        var points = bet.Market?.Prices
            .FirstOrDefault(p => string.Equals(p.Designation, designation, StringComparison.OrdinalIgnoreCase))
            ?.Points;

        // Traduire la désignation en texte lisible
        var pickLabel = designation switch
        {
            "home" => bet.Event?.Home ?? "Dom.",
            "away" => bet.Event?.Away ?? "Ext.",
            "over" => "Over",
            "under" => "Under",
            _ => designation ?? "?"
        };

        // Ajouter les points pour O/U, Handicap, etc.
        if (points.HasValue)
            pickLabel += $" {points.Value:G}";

        return $"{typeLabel}: {pickLabel}";
    }
}

