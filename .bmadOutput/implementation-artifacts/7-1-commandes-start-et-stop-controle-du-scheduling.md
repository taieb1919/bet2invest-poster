# Story 7.1 : Commandes /start et /stop — Contrôle du Scheduling

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want activer ou suspendre le scheduling automatique via `/start` et `/stop` depuis Telegram,
so that je puisse contrôler quand le service publie sans accéder au VPS.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé
   **When** l'utilisateur envoie `/stop`
   **Then** le `SchedulerWorker` suspend le prochain déclenchement automatique (FR25)
   **And** le bot répond `"⏸️ Scheduling suspendu. Utilisez /start pour reprendre."`
   **And** `/run` reste fonctionnel (exécution manuelle non affectée)

2. **Given** le scheduling suspendu
   **When** l'utilisateur envoie `/start`
   **Then** le `SchedulerWorker` reprend le scheduling automatique à l'heure configurée (FR24)
   **And** le bot répond `"▶️ Scheduling activé. Prochain run : {heure}."`

3. **Given** le scheduling déjà actif
   **When** l'utilisateur envoie `/start`
   **Then** le bot répond `"ℹ️ Scheduling déjà actif. Prochain run : {heure}."`

4. **Given** le service redémarré après un `/stop`
   **When** le service démarre
   **Then** l'état du scheduling (actif/suspendu) est persisté et restauré

## Tasks / Subtasks

- [x] Task 1 : Ajouter `SchedulingEnabled` à `IExecutionStateService` (AC: #1, #2, #3, #4)
  - [x] 1.1 Ajouter `bool SchedulingEnabled` au record `ExecutionState` (défaut: `true`)
  - [x] 1.2 Ajouter méthode `void SetSchedulingEnabled(bool enabled)` à `IExecutionStateService`
  - [x] 1.3 Implémenter dans `ExecutionStateService` avec `lock`
  - [x] 1.4 Ajouter persistance de l'état via fichier `scheduling-state.json` dans `DataPath` (AC: #4)
  - [x] 1.5 Charger l'état au démarrage du service (constructeur `ExecutionStateService`)

- [x] Task 2 : Modifier `SchedulerWorker` pour respecter `SchedulingEnabled` (AC: #1)
  - [x] 2.1 Dans la boucle `while`, avant `Task.Delay`, vérifier `_executionStateService.GetState().SchedulingEnabled`
  - [x] 2.2 Si `SchedulingEnabled == false`, faire une boucle d'attente courte (`Task.Delay(5s)`) au lieu du cycle
  - [x] 2.3 Quand `SchedulingEnabled` repasse à `true`, recalculer le prochain run et reprendre le scheduling
  - [x] 2.4 Logger la suspension et la reprise avec `Step = "Schedule"`

- [x] Task 3 : Créer `StartCommandHandler` (AC: #2, #3)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Telegram/Commands/StartCommandHandler.cs`
  - [x] 3.2 Implémenter `ICommandHandler`, `CanHandle("/start")`
  - [x] 3.3 Si déjà actif → répondre `"ℹ️ Scheduling déjà actif. Prochain run : {heure}."`
  - [x] 3.4 Si suspendu → appeler `SetSchedulingEnabled(true)`, répondre `"▶️ Scheduling activé. Prochain run : {heure}."`

- [x] Task 4 : Créer `StopCommandHandler` (AC: #1)
  - [x] 4.1 Créer `src/Bet2InvestPoster/Telegram/Commands/StopCommandHandler.cs`
  - [x] 4.2 Implémenter `ICommandHandler`, `CanHandle("/stop")`
  - [x] 4.3 Si déjà suspendu → répondre `"ℹ️ Scheduling déjà suspendu."`
  - [x] 4.4 Si actif → appeler `SetSchedulingEnabled(false)`, répondre `"⏸️ Scheduling suspendu. Utilisez /start pour reprendre."`

- [x] Task 5 : Enregistrer les nouveaux handlers dans DI (AC: #1 à #3)
  - [x] 5.1 Dans `Program.cs`, ajouter `builder.Services.AddSingleton<ICommandHandler, StartCommandHandler>()`
  - [x] 5.2 Dans `Program.cs`, ajouter `builder.Services.AddSingleton<ICommandHandler, StopCommandHandler>()`
  - [x] 5.3 Mettre à jour le message "Commande inconnue" dans `TelegramBotService` pour inclure `/start`, `/stop`

- [x] Task 6 : Tests unitaires (AC: #1 à #4)
  - [x] 6.1 Créer `tests/Bet2InvestPoster.Tests/Telegram/Commands/StartCommandHandlerTests.cs`
  - [x] 6.2 Créer `tests/Bet2InvestPoster.Tests/Telegram/Commands/StopCommandHandlerTests.cs`
  - [x] 6.3 Tests `ExecutionStateService` : `SetSchedulingEnabled` + persistance
  - [x] 6.4 Tests `SchedulerWorker` : comportement quand `SchedulingEnabled` est false
  - [x] 6.5 Build + test réussis, 0 régression

## Dev Notes

### Architecture — Contrôle du Scheduling via État Partagé

**Approche choisie** : Utiliser `IExecutionStateService` comme point central de l'état scheduling. C'est un Singleton thread-safe (avec `lock`) déjà partagé entre `SchedulerWorker`, `RunCommandHandler`, et `StatusCommandHandler`. Ajouter un champ `SchedulingEnabled` est la solution la plus simple et cohérente.

**Alternative rejetée** : CancellationTokenSource dans SchedulerWorker — trop complexe, nécessiterait de recréer le worker ou d'exposer un mécanisme de signaling. L'état booléen dans le Singleton est suffisant.

### Persistance de l'État Scheduling (AC #4)

**CRITIQUE** : L'épic exige que l'état soit persisté au redémarrage. `ExecutionStateService` est actuellement en mémoire uniquement. Il faut ajouter une persistance minimale :

```csharp
// scheduling-state.json dans le DataPath configuré
{
  "schedulingEnabled": true
}
```

**Pattern d'écriture** : Utiliser l'écriture atomique (write-to-temp + rename) identique à `HistoryManager` (NFR4). Lire au démarrage, écrire à chaque changement.

**Emplacement** : `Path.Combine(posterOptions.DataPath, "scheduling-state.json")`

### Modification SchedulerWorker — Pattern de Polling

Le `SchedulerWorker` actuel fait un `Task.Delay` long jusqu'à l'heure cible. Quand le scheduling est suspendu, il ne doit **pas** bloquer indéfiniment. Solution : boucle de vérification périodique.

```csharp
// Dans ExecuteAsync, après CalculateNextRun():
while (!stoppingToken.IsCancellationRequested)
{
    if (!_executionStateService.GetState().SchedulingEnabled)
    {
        // Attendre 5s et revérifier
        using (LogContext.PushProperty("Step", "Schedule"))
            _logger.LogDebug("Scheduling suspendu — en attente de reprise");
        await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, stoppingToken);
        continue;
    }

    var nextRun = CalculateNextRun();
    _executionStateService.SetNextRun(nextRun);
    // ... reste du code existant
}
```

**IMPORTANT** : Le `SetNextRun()` doit être appelé même si le scheduling est désactivé temporairement, sinon `/status` afficherait un NextRunAt obsolète. **Correction** : ne PAS appeler `SetNextRun()` quand le scheduling est suspendu — afficher `null` indique clairement que le scheduling est désactivé. Le `MessageFormatter` gère déjà le cas `NextRunAt == null`.

### Commandes Start/Stop — Pattern Identique aux Handlers Existants

Les handlers suivent exactement le même pattern que `RunCommandHandler` et `StatusCommandHandler` :

```csharp
// StartCommandHandler.cs
public class StartCommandHandler : ICommandHandler
{
    private readonly IExecutionStateService _stateService;
    private readonly ILogger<StartCommandHandler> _logger;

    public bool CanHandle(string command) => command == "/start";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var state = _stateService.GetState();
        if (state.SchedulingEnabled)
        {
            var nextRun = state.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "non calculé";
            await bot.SendMessage(message.Chat.Id,
                $"ℹ️ Scheduling déjà actif. Prochain run : {nextRun}.", cancellationToken: ct);
            return;
        }
        _stateService.SetSchedulingEnabled(true);
        // NextRunAt sera recalculé par SchedulerWorker dans les ~5 secondes
        await bot.SendMessage(message.Chat.Id,
            "▶️ Scheduling activé. Le prochain run sera calculé sous peu.", cancellationToken: ct);
    }
}
```

**Note sur le prochain run** : Quand `/start` est envoyé, `SchedulerWorker` est dans sa boucle de polling 5s. Il détectera le changement dans les 5 prochaines secondes et calculera le prochain run. Le message `/start` ne peut pas indiquer l'heure exacte du prochain run immédiatement — il faut un délai de ~5s. Le `SchedulerWorker` calculera et mettra à jour `NextRunAt` dès qu'il voit `SchedulingEnabled == true`.

### Dispatch de Commandes — TelegramBotService

`TelegramBotService` utilise `_handlers.FirstOrDefault(h => h.CanHandle(command))`. L'ajout de `StartCommandHandler` et `StopCommandHandler` dans DI via `AddSingleton<ICommandHandler, ...>()` les rend automatiquement disponibles dans `IEnumerable<ICommandHandler>`. **Aucune modification de `TelegramBotService.HandleUpdateAsync`** n'est nécessaire sauf pour mettre à jour le message d'aide ("Commande inconnue").

### Message "Commande Inconnue" — Mise à Jour

Ligne 89 de `TelegramBotService.cs` :
```csharp
// AVANT :
"Commande inconnue. Commandes disponibles : /run, /status"
// APRÈS :
"Commande inconnue. Commandes disponibles : /run, /status, /start, /stop"
```

### ExecutionState Record — Extension

```csharp
// AVANT :
public record ExecutionState(
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunResult,
    DateTimeOffset? NextRunAt,
    bool? ApiConnected
);

// APRÈS :
public record ExecutionState(
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunResult,
    DateTimeOffset? NextRunAt,
    bool? ApiConnected,
    bool SchedulingEnabled = true  // Défaut: actif
);
```

**IMPORTANT** : Le défaut `= true` maintient la rétrocompatibilité. L'état initial est "scheduling actif" — identique au comportement MVP.

### DI — Injection de `IOptions<PosterOptions>` dans `ExecutionStateService`

Pour la persistance fichier, `ExecutionStateService` a besoin du `DataPath`. Injecter `IOptions<PosterOptions>` dans son constructeur. C'est déjà un Singleton, donc aucun conflit de lifetime.

### Project Structure Notes

- Alignment avec `Telegram/Commands/` existant
- Nouveaux fichiers suivent le pattern exact `{CommandName}CommandHandler.cs`
- Pas de nouveau dossier, pas de nouveau namespace

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement handlers | `Telegram/Commands/StartCommandHandler.cs`, `StopCommandHandler.cs` | [Architecture: Structure Patterns — Telegram/Commands/] |
| Lifetime handlers | Singleton (comme RunCommandHandler, StatusCommandHandler) | [Program.cs:107-108] |
| Interface | `ICommandHandler` | [Architecture: interface-per-service] |
| État scheduling | `ExecutionStateService` (Singleton, thread-safe avec lock) | [Architecture: DI Pattern] |
| Persistance | JSON atomique (write-to-temp + rename) dans `DataPath` | [Architecture: NFR4, HistoryManager pattern] |
| Logging Step | `Schedule` (pour SchedulerWorker), `Notify` (pour handlers) | [Architecture: Serilog Template] |

### Intelligence Story Précédente (Story 5.1 + 5.2)

**Learnings applicables à Story 7.1 :**

1. **`SchedulerWorker.ExecuteAsync`** : Boucle `while(!ct.IsCancellationRequested)` avec `CalculateNextRun()` → `SetNextRun()` → `Task.Delay()` → cycle. Le point d'insertion pour la vérification `SchedulingEnabled` est **avant** le `Task.Delay` du prochain run.

2. **Polly retry** : Le retry est intégré via `_resiliencePipelineService.ExecuteCycleWithRetryAsync()`. Le `/run` continue de fonctionner indépendamment du scheduling (AC #1).

3. **Tests pattern** : xUnit, pas de Moq. Fakes en nested class. `FakeTimeProvider` pour le temps. Vérifier que les tests existants (123+) ne régressent pas.

4. **`IServiceScopeFactory`** : Pattern utilisé dans `RunCommandHandler` pour créer un scope DI. Les nouveaux handlers n'ont **pas** besoin de scope factory — ils accèdent uniquement à `IExecutionStateService` (Singleton).

5. **`TimeProvider`** : `SchedulerWorker` utilise `_timeProvider` pour `Task.Delay`. Le polling 5s doit aussi utiliser `_timeProvider` pour la testabilité.

### Intelligence Git

**Branche actuelle** : `feature/phase2`

**Pattern de commit attendu** :
```
feat(telegram): commandes /start et /stop contrôle du scheduling - story 7.1
```

### Exigences de Tests

**Framework** : xUnit. Pas de Moq/NSubstitute. Fakes minimaux en nested class.

**Tests à écrire** :
- `StartCommandHandlerTests` :
  - `Start_WhenSchedulingSuspended_EnablesAndResponds`
  - `Start_WhenAlreadyActive_RespondsAlreadyActive`
- `StopCommandHandlerTests` :
  - `Stop_WhenActive_SuspendsAndResponds`
  - `Stop_WhenAlreadySuspended_RespondsAlreadySuspended`
- `ExecutionStateServiceTests` :
  - `SetSchedulingEnabled_PersistsToFile`
  - `Constructor_RestoresPersistedState`
  - `SchedulingEnabled_DefaultTrue`
- `SchedulerWorkerTests` (ajout) :
  - `ExecuteAsync_SkipsCycle_WhenSchedulingDisabled`
  - `ExecuteAsync_ResumesCycle_WhenSchedulingReEnabled`

**Commandes de validation** :
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
```

### Boundaries à Respecter

- `StartCommandHandler` et `StopCommandHandler` ne contiennent **que** la logique de commande — pas de logique scheduling
- `SchedulerWorker` ne connaît **pas** les handlers Telegram — il observe uniquement `IExecutionStateService.SchedulingEnabled`
- `ExecutionStateService` est le **seul** point de mutation de l'état scheduling
- Le submodule `jtdev-bet2invest-scraper/` ne doit **JAMAIS** être modifié
- `/run` (exécution manuelle) fonctionne **indépendamment** du scheduling — ne pas toucher à `RunCommandHandler`

### Fichiers à CRÉER

```
src/Bet2InvestPoster/Telegram/Commands/
├── StartCommandHandler.cs       ← NOUVEAU
└── StopCommandHandler.cs        ← NOUVEAU

tests/Bet2InvestPoster.Tests/Telegram/Commands/
├── StartCommandHandlerTests.cs  ← NOUVEAU
└── StopCommandHandlerTests.cs   ← NOUVEAU
```

### Fichiers à MODIFIER

```
src/Bet2InvestPoster/
├── Services/IExecutionStateService.cs   ← MODIFIER (ajouter SchedulingEnabled au record + SetSchedulingEnabled)
├── Services/ExecutionStateService.cs    ← MODIFIER (implémenter SetSchedulingEnabled + persistance)
├── Workers/SchedulerWorker.cs           ← MODIFIER (vérifier SchedulingEnabled dans la boucle)
├── Program.cs                           ← MODIFIER (enregistrer StartCommandHandler + StopCommandHandler)
└── Telegram/TelegramBotService.cs       ← MODIFIER (message "Commande inconnue" — ajouter /start, /stop)

tests/Bet2InvestPoster.Tests/
├── Workers/SchedulerWorkerTests.cs      ← MODIFIER (tests SchedulingEnabled)
└── Services/ExecutionStateServiceTests.cs ← MODIFIER (tests SetSchedulingEnabled + persistance)
```

### Fichiers à NE PAS TOUCHER

```
jtdev-bet2invest-scraper/                         ← SUBMODULE — INTERDIT
src/Bet2InvestPoster/Services/PostingCycleService.cs   ← ne pas modifier
src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs ← ne pas modifier
src/Bet2InvestPoster/Telegram/Commands/StatusCommandHandler.cs ← ne pas modifier
src/Bet2InvestPoster/Configuration/PosterOptions.cs   ← ne pas modifier (DataPath déjà disponible)
```

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story-7.1] — AC originaux, FR24, FR25
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Telegram/Commands/ pour handlers
- [Source: .bmadOutput/planning-artifacts/architecture.md#DI-Pattern] — Singleton pour handlers
- [Source: src/Bet2InvestPoster/Workers/SchedulerWorker.cs] — boucle scheduling actuelle à modifier
- [Source: src/Bet2InvestPoster/Services/IExecutionStateService.cs] — interface à étendre
- [Source: src/Bet2InvestPoster/Services/ExecutionStateService.cs] — implémentation à étendre
- [Source: src/Bet2InvestPoster/Telegram/Commands/ICommandHandler.cs] — interface handler existante
- [Source: src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs] — pattern handler de référence
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs:81-91] — dispatch commandes + message aide
- [Source: src/Bet2InvestPoster/Program.cs:107-108] — pattern DI handlers Singleton
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs:10] — DataPath pour persistance
- [Source: .bmadOutput/implementation-artifacts/5-1-scheduler-worker-execution-quotidienne-planifiee.md] — learnings SchedulerWorker

## Dev Agent Record

### Agent Model Used

claude-opus-4-6 (code review reconstruction)

### Debug Log References

Commit original : 78ebe87 — fix(telegram): corriger commandes /start et /stop conformément aux AC (story 7.1)

### Completion Notes List

- StartCommandHandler et StopCommandHandler créés suivant le pattern ICommandHandler existant
- ExecutionStateService étendu avec SchedulingEnabled + persistance scheduling-state.json (écriture atomique)
- SchedulerWorker modifié avec boucle de polling 5s quand scheduling suspendu
- DI enregistré dans Program.cs + message "Commande inconnue" mis à jour
- Tests unitaires couvrant les deux handlers + comportement SchedulerWorker suspendu

### File List

- src/Bet2InvestPoster/Telegram/Commands/StartCommandHandler.cs (créé)
- src/Bet2InvestPoster/Telegram/Commands/StopCommandHandler.cs (créé)
- src/Bet2InvestPoster/Services/IExecutionStateService.cs (modifié)
- src/Bet2InvestPoster/Services/ExecutionStateService.cs (modifié)
- src/Bet2InvestPoster/Workers/SchedulerWorker.cs (modifié)
- src/Bet2InvestPoster/Program.cs (modifié)
- src/Bet2InvestPoster/Telegram/TelegramBotService.cs (modifié)
- tests/Bet2InvestPoster.Tests/Telegram/Commands/StartCommandHandlerTests.cs (créé)
- tests/Bet2InvestPoster.Tests/Telegram/Commands/StopCommandHandlerTests.cs (créé)
- tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs (modifié)
