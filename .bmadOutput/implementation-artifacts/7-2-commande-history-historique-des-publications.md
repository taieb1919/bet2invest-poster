# Story 7.2: Commande /history — Historique des Publications

Status: review

## Story

As a l'utilisateur,
I want consulter l'historique des dernières publications via `/history`,
so that je puisse vérifier ce qui a été publié récemment sans accéder au VPS.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé **When** l'utilisateur envoie `/history` **Then** `HistoryCommandHandler` lit `history.json` et affiche les 7 dernières publications (FR26) **And** chaque entrée affiche : date et description du match (toutes les entrées sont des succès — pas de champ statut dans le modèle, voir Dev Notes) **And** le message est formaté via `MessageFormatter` en bloc lisible

2. **Given** aucune publication dans l'historique **When** l'utilisateur envoie `/history` **Then** le bot répond `"📭 Aucune publication dans l'historique."`

## Tasks / Subtasks

- [x] Task 1 : Ajouter méthode `GetRecentEntriesAsync` à `IHistoryManager` / `HistoryManager` (AC: #1)
  - [x] 1.1 Ajouter la signature `Task<List<HistoryEntry>> GetRecentEntriesAsync(int count, CancellationToken ct)` à l'interface `IHistoryManager`
  - [x] 1.2 Implémenter dans `HistoryManager` : charger les entrées, trier par `PublishedAt` décroissant, retourner les `count` premières
  - [x] 1.3 Respecter le pattern SemaphoreSlim existant pour la thread-safety
- [x] Task 2 : Ajouter méthode `FormatHistory` à `IMessageFormatter` / `MessageFormatter` (AC: #1)
  - [x] 2.1 Ajouter la signature `string FormatHistory(List<HistoryEntry> entries)` à l'interface
  - [x] 2.2 Implémenter le formatage : regrouper par date de publication, afficher date + nombre de pronostics + description du match
  - [x] 2.3 Format attendu : bloc lisible avec emojis cohérents avec `FormatStatus`
- [x] Task 3 : Créer `HistoryCommandHandler` (AC: #1, #2)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Telegram/Commands/HistoryCommandHandler.cs`
  - [x] 3.2 Implémenter `ICommandHandler` avec `CanHandle("/history")`
  - [x] 3.3 Injecter `IHistoryManager`, `IMessageFormatter`, `ILogger<HistoryCommandHandler>`
  - [x] 3.4 Appeler `GetRecentEntriesAsync(7, ct)` puis `FormatHistory(entries)`
  - [x] 3.5 Gérer le cas liste vide → message `"📭 Aucune publication dans l'historique."`
  - [x] 3.6 Logger avec `LogContext.PushProperty("Step", "Notify")`
- [x] Task 4 : Enregistrer dans le DI (AC: #1)
  - [x] 4.1 Ajouter `builder.Services.AddSingleton<ICommandHandler, HistoryCommandHandler>()` dans `Program.cs`
- [x] Task 5 : Tests unitaires (AC: #1, #2)
  - [x] 5.1 Tests `HistoryManager.GetRecentEntriesAsync` : cas normal (>7 entrées → retourne 7), cas vide, cas <7 entrées
  - [x] 5.2 Tests `MessageFormatter.FormatHistory` : formatage correct, liste vide
  - [x] 5.3 Tests `HistoryCommandHandler` : dispatch correct, cas vide, cas avec données, vérification message envoyé

## Dev Notes

### Pattern Command Handler Existant

Suivre exactement le pattern de `StatusCommandHandler` :

```csharp
public class HistoryCommandHandler : ICommandHandler
{
    // Deps: IHistoryManager, IMessageFormatter, ILogger<HistoryCommandHandler>
    public bool CanHandle(string command) => command == "/history";
    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        // LogContext.PushProperty("Step", "Notify")
        // Appel service → format → bot.SendMessage(chatId, text, cancellationToken: ct)
    }
}
```

### HistoryManager — Extension

Le `HistoryManager` existant (`src/Bet2InvestPoster/Services/HistoryManager.cs`) possède déjà :
- `LoadEntriesAsync(ct)` privé — charge toutes les entrées depuis `history.json`
- Pattern SemaphoreSlim pour thread-safety
- `_historyPath` calculé depuis `PosterOptions.DataPath`
- Enregistré en **Singleton** dans le DI

La nouvelle méthode `GetRecentEntriesAsync` doit :
1. Acquérir le sémaphore
2. Appeler `LoadEntriesAsync(ct)` (méthode privée existante)
3. Trier par `PublishedAt` décroissant
4. Retourner `Take(count).ToList()`

### MessageFormatter — Extension

Le `MessageFormatter` existant (`src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs`) possède `FormatStatus(ExecutionState)`.

La nouvelle méthode `FormatHistory(List<HistoryEntry> entries)` doit :
- Regrouper les entrées par date (`PublishedAt.Date`)
- Pour chaque groupe : afficher la date + nombre de pronostics + descriptions
- Utiliser le même style d'emojis (📋, 📊, etc.)
- Format timestamps : `"yyyy-MM-dd HH:mm"` (cohérent avec `FormatStatus`)

Exemple de sortie attendue :
```
📋 Historique des 7 dernières publications

📅 2026-02-25
  • 14:30 — Arsenal vs Chelsea (betId: 42)
  • 14:30 — Lyon vs PSG (betId: 43)

📅 2026-02-24
  • 08:15 — Real Madrid vs Barcelona (betId: 38)
```

### Modèle HistoryEntry Existant

`src/Bet2InvestPoster/Models/HistoryEntry.cs` :
- `BetId` (int), `MatchupId` (string), `MarketKey` (string), `Designation` (string?)
- `PublishedAt` (DateTime), `MatchDescription` (string?), `TipsterUrl` (string?)
- Pas de champ "succès/échec" dans le modèle actuel

**Note importante :** L'AC mentionne "statut (succès/échec)" par entrée, mais le modèle `HistoryEntry` actuel ne contient PAS de champ statut — seuls les paris publiés avec succès sont enregistrés dans `history.json` (via `RecordAsync`). Donc toutes les entrées dans l'historique sont des succès. Le formatage doit refléter cela : afficher le contenu des publications réussies sans champ statut fictif.

### DI Registration Pattern

Dans `Program.cs`, les handlers sont enregistrés séquentiellement :
```csharp
builder.Services.AddSingleton<ICommandHandler, RunCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StatusCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StartCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StopCommandHandler>();
// Ajouter ici :
builder.Services.AddSingleton<ICommandHandler, HistoryCommandHandler>();
```

### TelegramBotService Dispatch

Le dispatch est automatique via `_handlers.FirstOrDefault(h => h.CanHandle(command))`. Les commandes sont extraites par `text.Split(' ')[0].ToLowerInvariant()`. Aucune modification nécessaire dans `TelegramBotService`.

### Project Structure Notes

Fichiers à créer :
- `src/Bet2InvestPoster/Telegram/Commands/HistoryCommandHandler.cs`

Fichiers à modifier :
- `src/Bet2InvestPoster/Services/IHistoryManager.cs` (ajouter `GetRecentEntriesAsync`)
- `src/Bet2InvestPoster/Services/HistoryManager.cs` (implémenter `GetRecentEntriesAsync`)
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` (ajouter `FormatHistory`)
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` (implémenter `FormatHistory`)
- `src/Bet2InvestPoster/Program.cs` (registration DI)

Fichiers de test à créer/modifier :
- `tests/Bet2InvestPoster.Tests/Services/HistoryManagerTests.cs` (ajouter tests GetRecentEntries)
- `tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterTests.cs` (ajouter tests FormatHistory)
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/HistoryCommandHandlerTests.cs` (nouveau)

### Conventions de Test Existantes

- Framework : xUnit + NSubstitute (mocking) + FluentAssertions
- Pattern : Arrange/Act/Assert
- Nommage : `MethodName_Scenario_ExpectedResult`
- `HistoryManager` est testé avec un `FakeTimeProvider` dans les tests existants
- Les handlers Telegram sont testés avec un mock `ITelegramBotClient`

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 7.2]
- [Source: src/Bet2InvestPoster/Services/HistoryManager.cs]
- [Source: src/Bet2InvestPoster/Services/IHistoryManager.cs]
- [Source: src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs]
- [Source: src/Bet2InvestPoster/Telegram/Commands/StatusCommandHandler.cs]
- [Source: src/Bet2InvestPoster/Models/HistoryEntry.cs]
- [Source: src/Bet2InvestPoster/Program.cs]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

Aucun blocage majeur. Les fakes `IHistoryManager` dans les tests existants (BetSelectorTests, BetPublisherTests, PostingCycleServiceTests, PostingCycleServiceNotificationTests) ont dû être mis à jour pour implémenter la nouvelle méthode `GetRecentEntriesAsync`.

### Completion Notes List

- `GetRecentEntriesAsync` ajouté à `IHistoryManager` et implémenté dans `HistoryManager` avec SemaphoreSlim, tri décroissant par `PublishedAt`, et `Take(count)`.
- `FormatHistory` ajouté à `IMessageFormatter` et implémenté dans `MessageFormatter` : groupement par date, tri décroissant, fallback sur `betId` si `MatchDescription` absent.
- `HistoryCommandHandler` créé suivant le pattern `StatusCommandHandler`.
- DI enregistré dans `Program.cs`.
- 180 tests passent (0 échecs).
- Note : toutes les entrées dans `history.json` sont des succès (pas de champ statut fictif, conformément aux Dev Notes).

### File List

- src/Bet2InvestPoster/Services/IHistoryManager.cs (modifié)
- src/Bet2InvestPoster/Services/HistoryManager.cs (modifié)
- src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs (modifié)
- src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs (modifié)
- src/Bet2InvestPoster/Telegram/Commands/HistoryCommandHandler.cs (créé)
- src/Bet2InvestPoster/Program.cs (modifié)
- tests/Bet2InvestPoster.Tests/Services/HistoryManagerTests.cs (modifié — 4 nouveaux tests)
- tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterTests.cs (modifié — 4 nouveaux tests)
- tests/Bet2InvestPoster.Tests/Telegram/Commands/HistoryCommandHandlerTests.cs (créé — 4 tests)
- tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs (modifié — fake mis à jour)
- tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs (modifié — fake mis à jour)
- tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs (modifié — fake mis à jour)
- tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs (modifié — fake mis à jour)
- tests/Bet2InvestPoster.Tests/Services/ExecutionStateServiceTests.cs (modifié — interface mise à jour)
- tests/Bet2InvestPoster.Tests/Telegram/Commands/StatusCommandHandlerTests.cs (modifié — interface mise à jour)
- tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs (modifié — fake mis à jour)
- .bmadOutput/implementation-artifacts/sprint-status.yaml (modifié — statut story mis à jour)

