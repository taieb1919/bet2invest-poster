# Story 12.2: Commande /report — Tableau de Bord des Performances

Status: done

## Story

As a l'utilisateur,
I want consulter un rapport de performances de mes pronostics publiés via `/report`,
so that je puisse évaluer l'efficacité de ma stratégie de sélection et l'ajuster.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé
   **When** l'utilisateur envoie `/report`
   **Then** `ReportCommandHandler` génère un rapport basé sur `history.json` (FR34)
   **And** le rapport inclut :
   - Période couverte (ex: "7 derniers jours")
   - Nombre total de pronostics publiés
   - Taux de réussite (won / total résolu)
   - ROI moyen des pronostics gagnants
   - Répartition par sport
   - Top 3 tipsters les plus performants
   **And** le message est formaté via `MessageFormatter` en bloc lisible

2. **Given** l'utilisateur envoie `/report 30` (avec argument jours)
   **When** le bot reçoit la commande
   **Then** le rapport couvre les 30 derniers jours au lieu de la période par défaut (7 jours)

3. **Given** aucun pronostic résolu dans la période demandée
   **When** l'utilisateur envoie `/report`
   **Then** le bot répond `"📊 Aucun pronostic résolu sur cette période. Les résultats sont vérifiés quotidiennement."`

## Tasks / Subtasks

- [x] Task 1 — Ajouter méthode `GetEntriesSinceAsync` à IHistoryManager (AC: #1, #2)
  - [x] 1.1 Ajouter `Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct)` à `IHistoryManager`
  - [x] 1.2 Implémenter dans `HistoryManager` — filtrer par `PublishedAt >= since`, ordonné par date desc
  - [x] 1.3 Tests unitaires dans `HistoryManagerTests` si existant, sinon vérifier via tests d'intégration du handler

- [x] Task 2 — Ajouter `FormatReport` à IMessageFormatter / MessageFormatter (AC: #1, #3)
  - [x] 2.1 Ajouter `string FormatReport(List<HistoryEntry> entries, int days)` à `IMessageFormatter`
  - [x] 2.2 Implémenter dans `MessageFormatter` avec les calculs statistiques :
    - Nombre total publiés dans la période
    - Nombre résolu (won + lost), nombre pending, nombre non vérifié (null)
    - Taux de réussite = won / (won + lost) * 100
    - ROI = ((somme odds des won) - nombre résolu) / nombre résolu * 100 (mise unitaire constante)
    - Répartition par sport : groupBy Sport, compter won/lost/pending
    - Top 3 tipsters : groupBy TipsterName, trier par taux de réussite desc
  - [x] 2.3 Si aucun pronostic résolu → retourner message vide `"📊 Aucun pronostic résolu sur cette période..."`
  - [x] 2.4 Formatter avec emojis cohérents : `📊`, `📈`, `⚽`, `✅`, `❌`

- [x] Task 3 — Créer ReportCommandHandler (AC: #1, #2, #3)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Telegram/Commands/ReportCommandHandler.cs`
  - [x] 3.2 Implémenter `ICommandHandler` avec pattern identique à `HistoryCommandHandler`
  - [x] 3.3 `CanHandle` → `/report`
  - [x] 3.4 `HandleAsync` : parser l'argument optionnel (nombre de jours, défaut 7), appeler `GetEntriesSinceAsync`, formatter via `FormatReport`
  - [x] 3.5 Valider l'argument jours : si invalide → message d'usage `"Usage : /report [jours] (ex: /report 30)"`

- [x] Task 4 — Enregistrer ReportCommandHandler en DI (AC: #1)
  - [x] 4.1 Ajouter `builder.Services.AddSingleton<ICommandHandler, ReportCommandHandler>();` dans `Program.cs`

- [x] Task 5 — Tests unitaires (AC: #1, #2, #3)
  - [x] 5.1 `ReportCommandHandlerTests.cs` : `CanHandle_Report_ReturnsTrue`
  - [x] 5.2 Test : `CanHandle_OtherCommand_ReturnsFalse`
  - [x] 5.3 Test : `HandleAsync_NoResolvedEntries_SendsEmptyMessage`
  - [x] 5.4 Test : `HandleAsync_WithEntries_SendsFormattedReport`
  - [x] 5.5 Test : `HandleAsync_WithDaysArgument_FiltersCorrectly`
  - [x] 5.6 Test : `HandleAsync_InvalidArgument_SendsUsageMessage`
  - [x] 5.7 Test : `HandleAsync_SendsToCorrectChatId`
  - [x] 5.8 Tests MessageFormatter `FormatReport` : vérifier calculs taux de réussite, ROI, répartition sport, top tipsters

## Dev Notes

### Pattern de commande — Copier HistoryCommandHandler

Le `ReportCommandHandler` suit exactement le même pattern que `HistoryCommandHandler` :
- Constructeur : `IHistoryManager`, `IMessageFormatter`, `ILogger<ReportCommandHandler>`
- `CanHandle("/report")`
- `HandleAsync` avec `LogContext.PushProperty("Step", "Notify")`
- Enregistrement DI en **Singleton**

### Parsing de l'argument jours

```csharp
// Dans HandleAsync :
var parts = message.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
var days = 7; // défaut
if (parts?.Length > 1 && int.TryParse(parts[1], out var parsed) && parsed > 0 && parsed <= 365)
    days = parsed;
else if (parts?.Length > 1)
    // argument invalide → message d'usage
```

### Calculs statistiques dans FormatReport

**Taux de réussite** :
```
resolved = entries.Where(e => e.Result is "won" or "lost")
winRate = resolved.Count(e => e.Result == "won") / resolved.Count() * 100
```

**ROI (Return On Investment)** — mise unitaire constante de 1 unité :
```
totalStake = resolved.Count()  // 1u par pari
totalReturn = won.Sum(e => e.Odds ?? 0)  // gain = cote * mise (1u)
roi = (totalReturn - totalStake) / totalStake * 100
```

**Répartition par sport** :
```
entries.GroupBy(e => e.Sport ?? "Inconnu")
  .Select(g => new { Sport = g.Key, Won = g.Count(e => e.Result == "won"), Lost = g.Count(e => e.Result == "lost"), Pending = g.Count(e => e.Result is "pending" or null) })
```

**Top 3 tipsters** :
```
resolved.GroupBy(e => e.TipsterName ?? "Inconnu")
  .Select(g => new { Name = g.Key, WinRate = g.Count(e => e.Result == "won") / (double)g.Count() * 100, Count = g.Count() })
  .OrderByDescending(t => t.WinRate)
  .Take(3)
```

### Nouvelle méthode HistoryManager — GetEntriesSinceAsync

`GetRecentEntriesAsync(count)` retourne les N plus récentes, mais pour `/report 30` il faut filtrer par date. Ajouter :

```csharp
public async Task<List<HistoryEntry>> GetEntriesSinceAsync(DateTime since, CancellationToken ct)
{
    await _semaphore.WaitAsync(ct);
    try
    {
        var entries = await LoadEntriesAsync(ct);
        return entries.Where(e => e.PublishedAt >= since).OrderByDescending(e => e.PublishedAt).ToList();
    }
    finally
    {
        _semaphore.Release();
    }
}
```

Ce pattern suit exactement `GetRecentEntriesAsync` mais avec un filtre date au lieu d'un count.

### Format de sortie Telegram attendu

```
📊 Rapport — 7 derniers jours

📋 Résumé
• Pronostics publiés : 42
• Résultats disponibles : 35 / 42
• En attente : 7

📈 Performances
• Taux de réussite : 62.9% (22/35)
• ROI : +14.3%
• Cote moyenne : 1.87

⚽ Par sport
• Football : 15 ✅ 8 ❌ 4 (65.2%)
• Tennis : 5 ✅ 4 ❌ 2 (55.6%)
• Basketball : 2 ✅ 1 ❌ 1 (66.7%)

🏆 Top tipsters
1. johndoe — 75.0% (9/12)
2. betmaster — 63.6% (7/11)
3. sportguru — 50.0% (6/12)
```

### Champs HistoryEntry disponibles (Story 12.1)

Tous les champs nécessaires sont déjà présents sur `HistoryEntry` :
- `Result` (string?) — "won", "lost", "pending", null
- `Odds` (decimal?) — cote au moment de la publication
- `Sport` (string?) — nom du sport
- `TipsterName` (string?) — nom/slug du tipster
- `PublishedAt` (DateTime) — date de publication

### FakeTelegramBotClient

Le fake client existe déjà dans `tests/Bet2InvestPoster.Tests/Telegram/Commands/FakeTelegramBotClient.cs`. Il capture `SentMessages` et `SentChatIds`. L'utiliser directement.

### Attention — NE PAS modifier le submodule

Le submodule `jtdev-bet2invest-scraper/` est en **lecture seule**. Ne jamais modifier ses fichiers.

### Project Structure Notes

- `ReportCommandHandler.cs` → `src/Bet2InvestPoster/Telegram/Commands/` (même dossier que tous les handlers)
- `ReportCommandHandlerTests.cs` → `tests/Bet2InvestPoster.Tests/Telegram/Commands/`
- Aucun nouveau dossier à créer
- Aucun conflit avec la structure existante

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Epic 12 — Story 12.2]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Telegram Commands Pattern]
- [Source: src/Bet2InvestPoster/Telegram/Commands/HistoryCommandHandler.cs — pattern de référence]
- [Source: src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs — pattern formatage]
- [Source: src/Bet2InvestPoster/Services/HistoryManager.cs — accès données history]
- [Source: src/Bet2InvestPoster/Models/HistoryEntry.cs — modèle enrichi story 12.1]
- [Source: .bmadOutput/implementation-artifacts/12-1-suivi-des-resultats-des-pronostics-publies.md — story précédente]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6

### Debug Log References

### Completion Notes List

- Implémentation complète story 12.2 par claude-sonnet-4-6 (2026-02-25)
- GetEntriesSinceAsync ajouté à IHistoryManager + HistoryManager (filtrage par date, pattern identique à GetRecentEntriesAsync)
- FormatReport ajouté à IMessageFormatter + MessageFormatter : taux de réussite, ROI, répartition sport, top 3 tipsters
- ReportCommandHandler créé suivant exactement le pattern HistoryCommandHandler
- Parsing argument jours (défaut 7, max 365) avec validation et message d'usage
- Enregistrement DI Singleton dans Program.cs
- 318 tests passent (dont 14 nouveaux : 7 ReportCommandHandlerTests + 5 MessageFormatterReportTests)
- Fakes IHistoryManager dans 6 fichiers tests mis à jour avec GetEntriesSinceAsync
- FakeMessageFormatter dans OnboardingServiceTests mis à jour avec FormatReport
- Ultimate context engine analysis completed — comprehensive developer guide created
- Pattern HistoryCommandHandler analysé et documenté comme référence exacte
- Calculs statistiques (taux réussite, ROI, répartition sport, top tipsters) spécifiés avec formules
- Format de sortie Telegram maquetté avec emojis cohérents du projet

### File List

- `src/Bet2InvestPoster/Telegram/Commands/ReportCommandHandler.cs` — nouveau handler /report (TimeProvider injecté)
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` — ajout FormatReport
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` — implémentation FormatReport, /report dans onboarding, cote moyenne corrigée, top tipsters >= 2 paris
- `src/Bet2InvestPoster/Services/IHistoryManager.cs` — ajout GetEntriesSinceAsync
- `src/Bet2InvestPoster/Services/HistoryManager.cs` — implémentation GetEntriesSinceAsync
- `src/Bet2InvestPoster/Program.cs` — enregistrement ReportCommandHandler en DI
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/ReportCommandHandlerTests.cs` — tests unitaires (+ 2 tests review)
- `tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs` — fake IHistoryManager mis à jour (GetEntriesSinceAsync)
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs` — fake IHistoryManager mis à jour
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs` — fake IHistoryManager mis à jour
- `tests/Bet2InvestPoster.Tests/Services/OnboardingServiceTests.cs` — fake IMessageFormatter mis à jour (FormatReport)
- `tests/Bet2InvestPoster.Tests/Services/ResultTrackerTests.cs` — fake IHistoryManager mis à jour
- `tests/Bet2InvestPoster.Tests/Services/UpcomingBetsFetcherTests.cs` — fake IHistoryManager mis à jour


## Change Log

| Date | Change |
|------|--------|
| 2026-02-25 | Implémentation complète story 12.2 — commande /report tableau de bord performances |
| 2026-02-25 | Code review adversarial — 7 issues corrigées (TimeProvider, cote moyenne, top tipsters min volume, onboarding, pluralisation, File List, test vide) |
