# Story 7.3: Commande /schedule — Configuration Horaire via Telegram

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want configurer l'heure d'exécution quotidienne via `/schedule <HH:mm>` depuis Telegram,
so that je puisse ajuster l'horaire de publication sans modifier de fichier de configuration.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé **When** l'utilisateur envoie `/schedule 10:30` **Then** `ScheduleCommandHandler` met à jour l'heure de scheduling en mémoire et persiste le changement (FR27) **And** le `SchedulerWorker` recalcule le prochain run avec la nouvelle heure **And** le bot répond `"⏰ Heure de publication mise à jour : 10:30. Prochain run : {date/heure}."`

2. **Given** l'utilisateur envoie `/schedule` sans argument **When** le bot reçoit la commande **Then** le bot répond avec l'heure actuelle : `"⏰ Heure actuelle : {HH:mm}. Usage : /schedule HH:mm"`

3. **Given** l'utilisateur envoie `/schedule 25:99` (format invalide) **When** le bot reçoit la commande **Then** le bot répond `"❌ Format invalide. Usage : /schedule HH:mm (ex: /schedule 08:00)"`

## Tasks / Subtasks

- [x] Task 1 : Étendre `ExecutionStateService` pour stocker et persister `ScheduleTime` (AC: #1)
  - [x] 1.1 Ajouter `string ScheduleTime` au record `ExecutionState` (défaut: valeur de `PosterOptions.ScheduleTime`)
  - [x] 1.2 Ajouter `void SetScheduleTime(string scheduleTime)` à `IExecutionStateService`
  - [x] 1.3 Implémenter dans `ExecutionStateService` avec `lock` + persistance dans `scheduling-state.json`
  - [x] 1.4 Charger `scheduleTime` au démarrage depuis `scheduling-state.json` (fallback sur `PosterOptions.ScheduleTime`)
  - [x] 1.5 Ajouter `string GetScheduleTime()` à `IExecutionStateService`

- [x] Task 2 : Modifier `SchedulerWorker` pour utiliser l'heure dynamique (AC: #1)
  - [x] 2.1 Remplacer le champ `readonly TimeOnly _scheduleTime` par une lecture dynamique depuis `_executionStateService.GetScheduleTime()`
  - [x] 2.2 Dans `CalculateNextRun()`, parser `GetScheduleTime()` au lieu du champ fixe
  - [x] 2.3 Logger le changement d'heure détecté avec `Step = "Schedule"`

- [x] Task 3 : Créer `ScheduleCommandHandler` (AC: #1, #2, #3)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Telegram/Commands/ScheduleCommandHandler.cs`
  - [x] 3.2 Implémenter `ICommandHandler`, `CanHandle("/schedule")`
  - [x] 3.3 Parser l'argument après `/schedule` via `message.Text.Split(' ', 2)`
  - [x] 3.4 Si pas d'argument → répondre avec l'heure actuelle (AC #2)
  - [x] 3.5 Valider le format HH:mm via `TimeOnly.TryParseExact(arg, "HH:mm", ...)` (AC #3)
  - [x] 3.6 Si valide → `_stateService.SetScheduleTime(arg)` + répondre avec confirmation (AC #1)
  - [x] 3.7 Injecter `IExecutionStateService` et `ILogger<ScheduleCommandHandler>`

- [x] Task 4 : Enregistrer dans DI et mettre à jour message aide (AC: #1 à #3)
  - [x] 4.1 Dans `Program.cs`, ajouter `builder.Services.AddSingleton<ICommandHandler, ScheduleCommandHandler>()`
  - [x] 4.2 Dans `TelegramBotService.cs`, mettre à jour le message "Commande inconnue" pour inclure `/schedule`

- [x] Task 5 : Tests unitaires (AC: #1 à #3)
  - [x] 5.1 Tests `ScheduleCommandHandler` : cas valide, cas sans argument, cas format invalide
  - [x] 5.2 Tests `ExecutionStateService` : `SetScheduleTime` + persistance + chargement au démarrage
  - [x] 5.3 Tests `SchedulerWorker` : vérifier utilisation de l'heure dynamique
  - [x] 5.4 Build + test réussis, 0 régression (200 tests)

## Dev Notes

### Architecture — Heure de Scheduling Dynamique

**Approche choisie** : Étendre `ExecutionStateService` (Singleton, thread-safe avec `lock`) pour stocker l'heure de scheduling. C'est le même pattern utilisé pour `SchedulingEnabled` (story 7.1). L'heure est persistée dans `scheduling-state.json` (même fichier que `schedulingEnabled`).

**Pourquoi pas `IOptionsMonitor<PosterOptions>` ?** : La modification d'`appsettings.json` à chaud depuis un handler Telegram est possible mais fragile (permissions fichier, format JSON à maintenir, redéploiement risqué). Utiliser `ExecutionStateService` est cohérent avec story 7.1 et plus simple.

### Persistance — Extension de `scheduling-state.json`

Le fichier `scheduling-state.json` actuel (story 7.1) contient :
```json
{
  "schedulingEnabled": true
}
```

Après story 7.3 :
```json
{
  "schedulingEnabled": true,
  "scheduleTime": "10:30"
}
```

**CRITIQUE** : La propriété `scheduleTime` est **optionnelle** dans le JSON. Si absente, fallback sur `PosterOptions.ScheduleTime` (défaut `"08:00"`). Cela assure la rétrocompatibilité avec les installations existantes.

**Pattern d'écriture** : Réutiliser le pattern atomique existant (write-to-temp + rename) dans `PersistSchedulingEnabled`. Refactorer en `PersistSchedulingState()` qui sérialise les deux champs.

### Extension de `ExecutionStateService`

```csharp
// Champ à ajouter
private string _scheduleTime; // Initialisé depuis scheduling-state.json ou PosterOptions.ScheduleTime

// Constructeur : modifier la signature pour accepter l'heure initiale
public ExecutionStateService(string? dataPath = null, string defaultScheduleTime = "08:00")
{
    // ... existing code ...
    _scheduleTime = LoadScheduleTime() ?? defaultScheduleTime;
    _state = new ExecutionState(null, null, null, null, null, schedulingEnabled, _scheduleTime);
}

// Nouvelles méthodes
public string GetScheduleTime()
{
    lock (_lock) return _scheduleTime;
}

public void SetScheduleTime(string scheduleTime)
{
    lock (_lock)
    {
        _scheduleTime = scheduleTime;
        _state = _state with { ScheduleTime = scheduleTime };
    }
    PersistSchedulingState();
}
```

**IMPORTANT** : La méthode `PersistSchedulingEnabled(bool)` existante doit être refactorée en `PersistSchedulingState()` qui écrit les **deux** propriétés (`schedulingEnabled` + `scheduleTime`). De même, `LoadSchedulingEnabled()` doit être étendu ou un `LoadScheduleTime()` ajouté.

### Extension du record `ExecutionState`

```csharp
public record ExecutionState(
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunResult,
    DateTimeOffset? NextRunAt,
    bool? ApiConnected,
    bool SchedulingEnabled = true,
    string ScheduleTime = "08:00"  // NOUVEAU — défaut cohérent avec PosterOptions
);
```

### Modification de `SchedulerWorker`

Le `SchedulerWorker` actuel stocke `_scheduleTime` comme `TimeOnly` readonly dans le constructeur :
```csharp
// ACTUEL (ligne 16 + ligne 34)
private readonly TimeOnly _scheduleTime;
_scheduleTime = TimeOnly.Parse(options.Value.ScheduleTime, CultureInfo.InvariantCulture);
```

**Modification requise** :
```csharp
// APRÈS — plus de champ _scheduleTime
// Dans CalculateNextRun(), lire dynamiquement :
internal DateTimeOffset CalculateNextRun()
{
    var scheduleTimeStr = _executionStateService.GetScheduleTime();
    var scheduleTime = TimeOnly.Parse(scheduleTimeStr, CultureInfo.InvariantCulture);
    var now = _timeProvider.GetUtcNow();
    var todayAtSchedule = new DateTimeOffset(
        now.Year, now.Month, now.Day,
        scheduleTime.Hour, scheduleTime.Minute, 0, TimeSpan.Zero);
    return todayAtSchedule > now ? todayAtSchedule : todayAtSchedule.AddDays(1);
}
```

**IMPACT** : Supprimer le champ `_scheduleTime` et la lecture de `options.Value.ScheduleTime` dans le constructeur. L'heure est désormais toujours lue depuis `ExecutionStateService`.

**ATTENTION au démarrage** : `SchedulerWorker` utilise encore `options.Value.ScheduleTime` pour le log initial. Remplacer par `_executionStateService.GetScheduleTime()`.

### Initialisation dans `Program.cs`

Le factory actuel de `ExecutionStateService` :
```csharp
builder.Services.AddSingleton<IExecutionStateService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PosterOptions>>().Value;
    return new ExecutionStateService(opts.DataPath);
});
```

Doit passer aussi `defaultScheduleTime` :
```csharp
builder.Services.AddSingleton<IExecutionStateService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<PosterOptions>>().Value;
    return new ExecutionStateService(opts.DataPath, opts.ScheduleTime);
});
```

### `ScheduleCommandHandler` — Implémentation

```csharp
public class ScheduleCommandHandler : ICommandHandler
{
    private readonly IExecutionStateService _stateService;
    private readonly ILogger<ScheduleCommandHandler> _logger;

    public ScheduleCommandHandler(
        IExecutionStateService stateService,
        ILogger<ScheduleCommandHandler> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/schedule";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text ?? string.Empty;
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /schedule reçue");
        }

        // Cas sans argument → afficher l'heure actuelle
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            var currentTime = _stateService.GetScheduleTime();
            await bot.SendMessage(chatId,
                $"⏰ Heure actuelle : {currentTime}. Usage : /schedule HH:mm",
                cancellationToken: ct);
            return;
        }

        var arg = parts[1].Trim();

        // Validation du format HH:mm
        if (!TimeOnly.TryParseExact(arg, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            await bot.SendMessage(chatId,
                "❌ Format invalide. Usage : /schedule HH:mm (ex: /schedule 08:00)",
                cancellationToken: ct);
            return;
        }

        // Mise à jour
        _stateService.SetScheduleTime(arg);

        // Le SchedulerWorker lira la nouvelle heure au prochain CalculateNextRun()
        // On peut estimer le prochain run
        var state = _stateService.GetState();
        var nextRunText = state.NextRunAt.HasValue
            ? state.NextRunAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "sera recalculé sous peu";

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Heure de scheduling mise à jour : {ScheduleTime}", arg);
        }

        await bot.SendMessage(chatId,
            $"⏰ Heure de publication mise à jour : {arg}. Prochain run : {nextRunText}.",
            cancellationToken: ct);
    }
}
```

**NOTE** : Après `SetScheduleTime(arg)`, le `NextRunAt` dans `ExecutionState` n'est pas immédiatement recalculé — c'est `SchedulerWorker` qui le fait au prochain cycle (dans les secondes qui suivent). Le message de confirmation utilise donc l'ancien `NextRunAt` ou un placeholder. C'est le même pattern que `/start` (story 7.1).

### Dispatch de Commandes — TelegramBotService

Le dispatch fonctionne via `text.Split(' ')[0].ToLowerInvariant()`. Pour `/schedule 10:30`, la commande extraite sera `/schedule`. Le **message complet** est dans `message.Text`, accessible dans le handler pour parser l'argument. **Aucune modification du dispatch** nécessaire.

### Message "Commande Inconnue" — Mise à Jour

Ligne 89 de `TelegramBotService.cs` :
```csharp
// AVANT :
"Commande inconnue. Commandes disponibles : /run, /status, /start, /stop"
// APRÈS :
"Commande inconnue. Commandes disponibles : /run, /status, /start, /stop, /schedule, /history"
```

**NOTE** : `/history` (story 7.2) n'a pas été ajouté au message d'aide. Profiter de cette modification pour l'inclure aussi.

### Project Structure Notes

Fichiers à créer :
- `src/Bet2InvestPoster/Telegram/Commands/ScheduleCommandHandler.cs`

Fichiers à modifier :
- `src/Bet2InvestPoster/Services/IExecutionStateService.cs` (ajouter `ScheduleTime` au record + `GetScheduleTime` + `SetScheduleTime`)
- `src/Bet2InvestPoster/Services/ExecutionStateService.cs` (implémenter persistance + chargement `scheduleTime`)
- `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` (supprimer `_scheduleTime` readonly, lire dynamiquement)
- `src/Bet2InvestPoster/Program.cs` (passer `opts.ScheduleTime` au constructeur + registration DI handler)
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (message "Commande inconnue" → ajouter `/schedule`, `/history`)

Fichiers de test à créer/modifier :
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/ScheduleCommandHandlerTests.cs` (nouveau)
- `tests/Bet2InvestPoster.Tests/Services/ExecutionStateServiceTests.cs` (ajouter tests `SetScheduleTime` + persistance)
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs` (vérifier heure dynamique)

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement handler | `Telegram/Commands/ScheduleCommandHandler.cs` | [Architecture: Structure Patterns — Telegram/Commands/] |
| Lifetime handler | Singleton (comme StartCommandHandler, StopCommandHandler) | [Program.cs:112-116] |
| Interface | `ICommandHandler` | [Architecture: interface-per-service] |
| État scheduling | `ExecutionStateService` (Singleton, thread-safe avec lock) | [Architecture: DI Pattern] |
| Persistance | JSON atomique (write-to-temp + rename) dans `scheduling-state.json` | [Architecture: NFR4, pattern existant story 7.1] |
| Logging Step | `Notify` (pour handler) | [Architecture: Serilog Template] |
| Validation format | `TimeOnly.TryParseExact` avec `CultureInfo.InvariantCulture` | [SchedulerWorker.cs:34 — même parser] |

### Intelligence Stories Précédentes (7.1 + 7.2)

**Learnings applicables à Story 7.3 :**

1. **Story 7.1** : `ExecutionStateService` étendu avec `SchedulingEnabled` + persistance `scheduling-state.json`. Même pattern à suivre pour `ScheduleTime`. Le refactoring de `PersistSchedulingEnabled` → `PersistSchedulingState` est le point clé.

2. **Story 7.1** : `SchedulerWorker` modifié pour vérifier `SchedulingEnabled` via polling 5s. Le changement d'heure sera pris en compte naturellement au prochain `CalculateNextRun()`.

3. **Story 7.2** : `HistoryCommandHandler` ajouté avec le même pattern handler. 180 tests passent. Les fakes dans les tests existants peuvent nécessiter mise à jour si l'interface `IExecutionStateService` change (ajout de nouvelles méthodes).

4. **Pattern de test** : xUnit + fakes en nested class + `FakeTimeProvider`. Les handlers sont testés avec mock `ITelegramBotClient` vérifiant les messages envoyés via `Received().SendMessage(...)` (NSubstitute dans les tests handlers — vérifier le pattern exact).

5. **ATTENTION aux fakes** : L'ajout de `GetScheduleTime()` et `SetScheduleTime()` à `IExecutionStateService` nécessitera la mise à jour de TOUS les fakes dans les tests existants qui implémentent cette interface.

### Intelligence Git

**Branche actuelle** : `feature/phase2`

**Commits récents pertinents** :
- `b58f18a` fix(history): corriger issues code review story 7.2
- `78ebe87` fix(telegram): corriger commandes /start et /stop conformément aux AC (story 7.1)

### Exigences de Tests

**Framework** : xUnit. Pas de Moq/NSubstitute. Fakes minimaux en nested class.

**Correction** : Les tests handlers 7.2 (`HistoryCommandHandlerTests`) utilisent NSubstitute pour mocker `ITelegramBotClient`. Vérifier le pattern réel dans les tests existants avant de choisir entre NSubstitute et fakes.

**Tests à écrire** :
- `ScheduleCommandHandlerTests` :
  - `Schedule_WithValidTime_UpdatesAndResponds` (AC #1)
  - `Schedule_WithoutArgument_ShowsCurrentTime` (AC #2)
  - `Schedule_WithInvalidFormat_RespondsError` (AC #3)
  - `Schedule_WithEdgeCaseTime_Works` (ex: `"00:00"`, `"23:59"`)
- `ExecutionStateServiceTests` :
  - `SetScheduleTime_PersistsToFile`
  - `Constructor_RestoresPersistedScheduleTime`
  - `Constructor_FallsBackToDefault_WhenNoPersistedScheduleTime`
- `SchedulerWorkerTests` :
  - `CalculateNextRun_UsesExecutionStateServiceScheduleTime`

**Commandes de validation** :
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
```

### Boundaries à Respecter

- `ScheduleCommandHandler` ne contient **que** la logique de commande — pas de logique scheduling
- `SchedulerWorker` ne connaît **pas** les handlers Telegram — il lit l'heure depuis `IExecutionStateService`
- `ExecutionStateService` est le **seul** point de mutation de l'heure de scheduling
- Le submodule `jtdev-bet2invest-scraper/` ne doit **JAMAIS** être modifié
- `PosterOptions.ScheduleTime` reste le **défaut initial** — `ExecutionStateService` le surcharge si une valeur persistée existe

### Fichiers à CRÉER

```
src/Bet2InvestPoster/Telegram/Commands/
└── ScheduleCommandHandler.cs       ← NOUVEAU

tests/Bet2InvestPoster.Tests/Telegram/Commands/
└── ScheduleCommandHandlerTests.cs  ← NOUVEAU
```

### Fichiers à MODIFIER

```
src/Bet2InvestPoster/
├── Services/IExecutionStateService.cs   ← MODIFIER (ajouter ScheduleTime au record + GetScheduleTime + SetScheduleTime)
├── Services/ExecutionStateService.cs    ← MODIFIER (implémenter SetScheduleTime + persistance + chargement)
├── Workers/SchedulerWorker.cs           ← MODIFIER (supprimer _scheduleTime readonly, lire dynamiquement)
├── Program.cs                           ← MODIFIER (passer ScheduleTime au constructeur + enregistrer handler)
└── Telegram/TelegramBotService.cs       ← MODIFIER (message "Commande inconnue" — ajouter /schedule, /history)

tests/Bet2InvestPoster.Tests/
├── Services/ExecutionStateServiceTests.cs ← MODIFIER (tests SetScheduleTime + persistance)
└── Workers/SchedulerWorkerTests.cs        ← MODIFIER (vérifier heure dynamique)
```

### Fichiers à NE PAS TOUCHER

```
jtdev-bet2invest-scraper/                                    ← SUBMODULE — INTERDIT
src/Bet2InvestPoster/Services/PostingCycleService.cs         ← ne pas modifier
src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs  ← ne pas modifier
src/Bet2InvestPoster/Telegram/Commands/StartCommandHandler.cs ← ne pas modifier
src/Bet2InvestPoster/Telegram/Commands/StopCommandHandler.cs  ← ne pas modifier
src/Bet2InvestPoster/Configuration/PosterOptions.cs          ← ne pas modifier (ScheduleTime reste le défaut)
```

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story-7.3] — AC originaux, FR27
- [Source: .bmadOutput/planning-artifacts/architecture.md#DI-Pattern] — Singleton pour handlers
- [Source: src/Bet2InvestPoster/Services/IExecutionStateService.cs] — interface à étendre
- [Source: src/Bet2InvestPoster/Services/ExecutionStateService.cs] — implémentation à étendre (pattern scheduling-state.json)
- [Source: src/Bet2InvestPoster/Workers/SchedulerWorker.cs] — _scheduleTime à rendre dynamique
- [Source: src/Bet2InvestPoster/Telegram/Commands/StartCommandHandler.cs] — pattern handler de référence
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs:88-90] — dispatch commandes + message aide
- [Source: src/Bet2InvestPoster/Program.cs:102-106] — factory ExecutionStateService
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs:7] — ScheduleTime défaut "08:00"
- [Source: .bmadOutput/implementation-artifacts/7-1-commandes-start-et-stop-controle-du-scheduling.md] — pattern persistance scheduling-state.json
- [Source: .bmadOutput/implementation-artifacts/7-2-commande-history-historique-des-publications.md] — learnings story précédente

## Dev Agent Record

### Agent Model Used

claude-opus-4-6

### Debug Log References

### Completion Notes List

- Implémentation complète story 7.3 (2026-02-25)
- Issues code review corrigées : C1 (story update), C2+C3 (pas de race condition sur NextRun — délégué au SchedulerWorker), M1 (messages alignés AC), M2 (defaultScheduleTime depuis PosterOptions.ScheduleTime), M3 (log changement heure dans SchedulerWorker), L1 (test heure dynamique), L2 (ILogger dans ExecutionStateService)
- 200 tests passent (3 nouveaux)

### File List

- src/Bet2InvestPoster/Telegram/Commands/ScheduleCommandHandler.cs (créé)
- src/Bet2InvestPoster/Services/IExecutionStateService.cs (modifié)
- src/Bet2InvestPoster/Services/ExecutionStateService.cs (modifié)
- src/Bet2InvestPoster/Workers/SchedulerWorker.cs (modifié)
- src/Bet2InvestPoster/Program.cs (modifié)
- src/Bet2InvestPoster/Telegram/TelegramBotService.cs (modifié)
- tests/Bet2InvestPoster.Tests/Telegram/Commands/ScheduleCommandHandlerTests.cs (créé)
- tests/Bet2InvestPoster.Tests/Services/ExecutionStateServiceTests.cs (modifié)
- tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs (modifié)
