# Story 5.1 : SchedulerWorker — Exécution Quotidienne Planifiée

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que le système publie automatiquement des pronostics chaque jour à l'heure que j'ai configurée,
so that ma présence sur bet2invest soit maintenue sans aucune intervention de ma part.

## Acceptance Criteria

1. **Given** une heure d'exécution configurée dans `PosterOptions.ScheduleTime` (ex: `"08:00"`)
   **When** l'heure configurée est atteinte
   **Then** `SchedulerWorker` déclenche automatiquement `PostingCycleService.RunCycleAsync()` (FR11)

2. **Given** l'heure d'exécution configurée dans `appsettings.json` section `Poster:ScheduleTime`
   **When** le service démarre
   **Then** l'heure est lue depuis `PosterOptions.ScheduleTime` et configurable via variable d'environnement `Poster__ScheduleTime` (FR13)

3. **Given** un cycle de publication terminé (succès ou échec)
   **When** `SchedulerWorker` calcule le prochain run
   **Then** le prochain run est planifié pour le lendemain à la même heure configurée
   **And** `IExecutionStateService.SetNextRun()` est appelé avec la date/heure calculée

4. **Given** le service démarre
   **When** `SchedulerWorker` s'initialise
   **Then** le scheduling est interne au service (pas de dépendance à cron externe)
   **And** le prochain run est calculé et enregistré via `SetNextRun()`

5. **Given** `SchedulerWorker` démarre ou déclenche un cycle
   **When** un événement de scheduling se produit
   **Then** le démarrage et chaque déclenchement sont logués avec timestamp et Step approprié

6. **Given** `SchedulerWorker` est enregistré dans DI
   **When** l'application démarre
   **Then** `SchedulerWorker` remplace `Worker` comme `BackgroundService` principal
   **And** `Worker.cs` (exécution unique au démarrage) est supprimé

## Tasks / Subtasks

- [x] Task 1 : Créer `SchedulerWorker` dans `Workers/` (AC: #1, #3, #4, #5)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Workers/SchedulerWorker.cs`
  - [x] 1.2 Injecter `IServiceProvider`, `IOptions<PosterOptions>`, `IExecutionStateService`, `ILogger<SchedulerWorker>`
  - [x] 1.3 Parser `PosterOptions.ScheduleTime` (format `"HH:mm"`) via `TimeOnly.Parse()`
  - [x] 1.4 Calculer `DateTimeOffset` du prochain run : si heure passée aujourd'hui → demain, sinon → aujourd'hui
  - [x] 1.5 Appeler `_executionStateService.SetNextRun(nextRun)` au démarrage et après chaque cycle
  - [x] 1.6 Boucle principale : `Task.Delay(delai, _timeProvider, stoppingToken)` jusqu'à l'heure cible, puis exécuter le cycle
  - [x] 1.7 Créer un scope DI par cycle : `using var scope = _serviceProvider.CreateScope()` → `GetRequiredService<IPostingCycleService>()`
  - [x] 1.8 Après exécution (succès ou échec), calculer prochain run = lendemain même heure
  - [x] 1.9 Logger démarrage, chaque déclenchement, et prochain run avec `LogContext.PushProperty("Step", "Schedule")`

- [x] Task 2 : Remplacer `Worker` par `SchedulerWorker` dans DI (AC: #6)
  - [x] 2.1 Dans `Program.cs`, remplacer `builder.Services.AddHostedService<Worker>()` par `builder.Services.AddHostedService<SchedulerWorker>()`
  - [x] 2.2 Supprimer `src/Bet2InvestPoster/Worker.cs`
  - [x] 2.3 Supprimer le `using Bet2InvestPoster;` dans Program.cs (inutile après suppression Worker)
  - [x] 2.4 Ajouter `using Bet2InvestPoster.Workers;` dans Program.cs

- [x] Task 3 : Tests unitaires (AC: #1 à #6)
  - [x] 3.1 Créer `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs`
  - [x] 3.2 Tests implémentés :
    - `CalculatesNextRun_WhenTimeNotPassedToday_SchedulesToday` ✅
    - `CalculatesNextRun_WhenTimePassed_SchedulesTomorrow` ✅
    - `ExecuteAsync_CallsRunCycleAsync_AtScheduledTime` ✅
    - `ExecuteAsync_SetsNextRun_AfterCycleCompletes` ✅
    - `ExecuteAsync_SetsNextRun_EvenAfterCycleFailure` ✅
    - `ExecuteAsync_StopsOnCancellation` ✅
  - [x] 3.3 `TimeProvider` injecté (abstraction .NET 8+) + `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing 10.3.0)
  - [x] 3.4 Build + test réussis
  - [x] 3.5 Résultat : 122 tests (116 existants + 6 nouveaux), 0 échec

## Dev Notes

### Architecture — SchedulerWorker remplace Worker

**Décision clé** : Le `Worker.cs` actuel (ligne 22) contient un commentaire explicite : _"Epic 5 ajoutera le scheduling quotidien (SchedulerWorker)"_. Le `Worker` actuel fait une exécution unique au démarrage. Il doit être **remplacé** par `SchedulerWorker` qui tourne en boucle infinie avec scheduling quotidien.

**Emplacement** : `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` — le dossier `Workers/` est défini dans l'architecture mais n'existe pas encore. Le créer.

### SchedulerWorker — Implémentation

```csharp
// src/Bet2InvestPoster/Workers/SchedulerWorker.cs
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Workers;

public class SchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IExecutionStateService _executionStateService;
    private readonly TimeOnly _scheduleTime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerWorker> _logger;

    public SchedulerWorker(
        IServiceProvider serviceProvider,
        IExecutionStateService executionStateService,
        IOptions<PosterOptions> options,
        TimeProvider timeProvider,
        ILogger<SchedulerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _executionStateService = executionStateService;
        _scheduleTime = TimeOnly.Parse(options.Value.ScheduleTime);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (LogContext.PushProperty("Step", "Schedule"))
        {
            _logger.LogInformation(
                "SchedulerWorker démarré — exécution planifiée à {ScheduleTime} chaque jour",
                _scheduleTime);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = CalculateNextRun();
            _executionStateService.SetNextRun(nextRun);

            using (LogContext.PushProperty("Step", "Schedule"))
            {
                _logger.LogInformation("Prochain run planifié : {NextRun:yyyy-MM-dd HH:mm:ss} UTC", nextRun);
            }

            var delay = nextRun - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);

            // Exécuter le cycle dans un scope DI dédié
            using (LogContext.PushProperty("Step", "Schedule"))
            {
                _logger.LogInformation("Déclenchement cycle planifié");
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
                await cycleService.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // Propagate cancellation
            }
            catch (Exception ex)
            {
                // Le cycle a échoué — PostingCycleService a déjà notifié et loggé.
                // Story 5.2 ajoutera le retry Polly ici.
                // On continue la boucle pour le prochain run.
                using (LogContext.PushProperty("Step", "Schedule"))
                {
                    _logger.LogError(ex, "Cycle échoué — reprise au prochain run planifié");
                }
            }
        }
    }

    internal DateTimeOffset CalculateNextRun()
    {
        var now = _timeProvider.GetUtcNow();
        var todayAtSchedule = new DateTimeOffset(
            now.Date.Year, now.Date.Month, now.Date.Day,
            _scheduleTime.Hour, _scheduleTime.Minute, 0, TimeSpan.Zero);

        return todayAtSchedule > now ? todayAtSchedule : todayAtSchedule.AddDays(1);
    }
}
```

### TimeProvider — Testabilité du Temps

**CRITIQUE** : Injecter `TimeProvider` (abstraction .NET 8+) au lieu d'utiliser `DateTimeOffset.UtcNow` directement. Cela permet de contrôler le temps dans les tests unitaires.

**Enregistrement DI dans Program.cs** :
```csharp
// TimeProvider: Singleton — default system clock, overridden in tests
builder.Services.AddSingleton(TimeProvider.System);
```

`TimeProvider` est inclus dans `Microsoft.Extensions.TimeProvider.Testing` pour les tests, mais la classe abstraite `TimeProvider` est native dans .NET 8+/9. Les tests utiliseront `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider`.

**Package NuGet pour les tests** :
```bash
dotnet add tests/Bet2InvestPoster.Tests package Microsoft.Extensions.TimeProvider.Testing
```

### Modification Program.cs

```csharp
// REMPLACER :
builder.Services.AddHostedService<Worker>();
// PAR :
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SchedulerWorker>();

// AJOUTER le using :
using Bet2InvestPoster.Workers;
// SUPPRIMER si inutile :
// using Bet2InvestPoster;  (namespace de l'ancien Worker)
```

### Step de Logging — "Schedule"

L'architecture autorise les Steps : `Auth`, `Scrape`, `Select`, `Publish`, `Notify`, `Purge`. Le `SchedulerWorker` introduit un nouveau Step `Schedule` pour les logs de planification. Ce Step est cohérent avec la convention existante et ne modifie pas les Steps existants.

### Pattern Scope DI — Un Scope par Cycle

Le pattern est **identique** à ce que fait `Worker.cs` actuellement et `RunCommandHandler` :
```csharp
using var scope = _serviceProvider.CreateScope();
var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
await cycleService.RunCycleAsync(stoppingToken);
```

**IMPORTANT** : Tous les services Scoped (ExtendedBet2InvestClient, TipsterService, etc.) sont créés frais à chaque cycle. Le scope est disposé après chaque exécution. Ce pattern évite les fuites d'état entre cycles.

### Gestion des Erreurs — Continuer après Échec

**Le cycle peut échouer** (réseau, API down, etc.). `PostingCycleService` gère déjà :
1. Logging de l'erreur
2. `ExecutionStateService.RecordFailure()`
3. `NotificationService.NotifyFailureAsync()`
4. Re-throw de l'exception

`SchedulerWorker` catch l'exception et **continue la boucle** pour le prochain run. Le retry Polly (Story 5.2) viendra encapsuler l'appel `RunCycleAsync()`.

### ExecutionState.NextRunAt — Déjà Prêt

`IExecutionStateService.SetNextRun(DateTimeOffset)` et `ExecutionState.NextRunAt` existent déjà. `MessageFormatter.FormatStatus()` affiche déjà `NextRunAt` dans la réponse `/status` :
```
• Prochain run : 2026-02-25 08:00:00 UTC
```

Aucune modification nécessaire dans `ExecutionStateService`, `IExecutionStateService`, ou `MessageFormatter`.

### Suppression de Worker.cs

Le fichier `src/Bet2InvestPoster/Worker.cs` doit être **supprimé** (pas renommé). Son contenu est entièrement remplacé par `SchedulerWorker`. Le test DI `AuthorizationFilterTests` (si existant) qui vérifie l'enregistrement de `Worker` devra être mis à jour.

### Vérification Tests DI Existants

Vérifier si un test existant valide l'enregistrement de `Worker` en HostedService. Si oui, le modifier pour vérifier `SchedulerWorker` à la place.

### Project Structure Notes

- `Workers/` dossier créé pour la première fois — conforme à l'architecture
- Alignment avec la structure définie : `Workers/SchedulerWorker.cs`
- Pas de conflit avec les dossiers existants

### Tests — FakeTimeProvider

```csharp
// Nécessite le package Microsoft.Extensions.TimeProvider.Testing
using Microsoft.Extensions.Time.Testing;

// Dans les tests :
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 2, 25, 7, 0, 0, TimeSpan.Zero));

// Pour avancer le temps :
fakeTime.SetUtcNow(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero));
```

**Pattern de test pour SchedulerWorker** :
- Injecter `FakeTimeProvider` pour contrôler le temps
- Utiliser `CancellationTokenSource` avec timeout court pour stopper la boucle
- Fake `IPostingCycleService` pour vérifier l'appel `RunCycleAsync`
- Fake `IExecutionStateService` pour vérifier `SetNextRun()`

```csharp
private class FakePostingCycleService : IPostingCycleService
{
    public int RunCount { get; private set; }
    public bool ShouldThrow { get; set; }

    public Task RunCycleAsync(CancellationToken ct = default)
    {
        RunCount++;
        if (ShouldThrow)
            throw new InvalidOperationException("Simulated failure");
        return Task.CompletedTask;
    }
}
```

**IMPORTANT** : `IPostingCycleService` est Scoped → le fake doit être enregistré dans un `ServiceCollection` configuré pour les tests, pas injecté directement dans le constructeur de `SchedulerWorker`.

### Exigences de Tests

**Framework** : xUnit. Pas de Moq/NSubstitute. Fakes minimaux en nested class.

**Commandes de validation** :
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 116 existants + ≥6 nouveaux = ≥122 tests, 0 échec
```

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement SchedulerWorker | `Workers/SchedulerWorker.cs` | [Architecture: Structure Patterns] |
| Lifetime SchedulerWorker | HostedService (BackgroundService) | [Architecture: Scheduling FR11-FR13] |
| Configuration heure | `PosterOptions.ScheduleTime` | [Architecture: Configuration/PosterOptions.cs] |
| DI Scope par cycle | `CreateScope()` + `GetRequiredService<IPostingCycleService>()` | [Architecture: DI Pattern — Scoped] |
| Logging Step | `Schedule` | [Architecture: Serilog Template — nouveau Step] |
| TimeProvider | `TimeProvider.System` (Singleton) | [.NET 8+ abstraction pour testabilité] |
| Pas de cron externe | Timer interne via `Task.Delay` | [Epic 5 AC: scheduling interne au service] |

### Intelligence Story Précédente (Story 4.3)

**Learnings applicables à Story 5.1 :**

1. **Scope DI pattern** : `using var scope = _serviceProvider.CreateScope()` → `GetRequiredService<IPostingCycleService>()` — pattern identique à `Worker.cs` et `RunCommandHandler`.

2. **PostingCycleService re-throw** : L'exception est re-thrown après notification. `SchedulerWorker` doit catch cette exception et continuer la boucle.

3. **ExecutionStateService.SetNextRun()** : Méthode déjà prête, thread-safe avec `lock`.

4. **`LogContext.PushProperty("Step", ...)`** : Pattern de logging à respecter systématiquement.

5. **Tests DI** : `AuthorizationFilterTests` vérifie l'enregistrement des HostedServices. Potentiellement impacté par le remplacement de `Worker` par `SchedulerWorker`.

6. **116 tests existants** : baseline à ne pas régresser.

### Intelligence Git

**Branche actuelle** : `epic-2/connexion-api` (nom historique conservé)

**Pattern de commit attendu** :
```
feat(scheduler): SchedulerWorker exécution quotidienne planifiée - story 5.1
```

### Boundaries à Respecter

- `SchedulerWorker` **orchestre uniquement** — pas de logique métier, délègue à `PostingCycleService`
- `PostingCycleService` gère déjà les notifications (succès/échec) et l'état d'exécution
- `SchedulerWorker` ne doit **jamais** accéder directement à `ITelegramBotClient` ou `INotificationService`
- Le retry Polly (Story 5.2) viendra **encapsuler** l'appel `RunCycleAsync()` — ne pas l'implémenter ici
- Le submodule `jtdev-bet2invest-scraper/` ne doit **JAMAIS** être modifié

### Fichiers à CRÉER

```
src/Bet2InvestPoster/
└── Workers/
    └── SchedulerWorker.cs       ← NOUVEAU (dossier Workers/ à créer)
```

### Fichiers à MODIFIER

```
src/Bet2InvestPoster/
└── Program.cs                   ← MODIFIER (remplacer Worker par SchedulerWorker + TimeProvider)
```

### Fichiers à SUPPRIMER

```
src/Bet2InvestPoster/
└── Worker.cs                    ← SUPPRIMER (remplacé par SchedulerWorker)
```

### Fichiers à NE PAS TOUCHER

```
jtdev-bet2invest-scraper/                        ← SUBMODULE — INTERDIT
src/Bet2InvestPoster/Services/PostingCycleService.cs  ← ne pas modifier
src/Bet2InvestPoster/Services/ExecutionStateService.cs ← ne pas modifier
src/Bet2InvestPoster/Services/IExecutionStateService.cs ← ne pas modifier
src/Bet2InvestPoster/Configuration/PosterOptions.cs   ← ne pas modifier (ScheduleTime déjà défini)
src/Bet2InvestPoster/Telegram/                        ← ne pas modifier
```

### References

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-5.1] — AC originaux, FR11, FR13
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Workers/ pour SchedulerWorker
- [Source: .bmadOutput/planning-artifacts/architecture.md#DI-Pattern] — Scoped per cycle, Singleton client
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — Steps logging, interface-per-service
- [Source: src/Bet2InvestPoster/Worker.cs:22] — commentaire "Epic 5 ajoutera le scheduling quotidien"
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs:7] — ScheduleTime déjà défini
- [Source: src/Bet2InvestPoster/Services/IExecutionStateService.cs:8] — SetNextRun() déjà prêt
- [Source: src/Bet2InvestPoster/Services/ExecutionStateService.cs:39-45] — SetNextRun() implémenté
- [Source: src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs:21-23] — NextRunAt déjà affiché
- [Source: .bmadOutput/implementation-artifacts/4-3-notification-service-notifications-automatiques.md] — pattern DI, scope, re-throw

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `WorkerTests.cs` : supprimé car référençait `Worker` (classe supprimée). Remplacé par `SchedulerWorkerTests.cs`.
- `Task.Delay(delay, _timeProvider, stoppingToken)` : utilisation de l'overload .NET 8+ avec `TimeProvider` pour permettre à `FakeTimeProvider.Advance()` de déclencher les delays dans les tests.
- `CalculateNextRun()` méthode : rendue `public` (au lieu de `internal`) pour accessibilité directe depuis les tests unitaires sans `InternalsVisibleTo`.

### Completion Notes List

- AC#1 : `SchedulerWorker` boucle infinie — déclenche `PostingCycleService.RunCycleAsync()` à l'heure configurée via `Task.Delay(delay, _timeProvider, ct)`.
- AC#2 : `PosterOptions.ScheduleTime` (déjà existant, default "08:00") — configurable via `appsettings.json` ou `Poster__ScheduleTime` env var.
- AC#3 : `SetNextRun(nextRun)` appelé au début de chaque itération de boucle (avant et après cycle).
- AC#4 : Scheduling interne via `Task.Delay` + `TimeProvider`. Pas de cron externe.
- AC#5 : Logs `LogContext.PushProperty("Step", "Schedule")` au démarrage, avant et après chaque cycle.
- AC#6 : `Worker.cs` supprimé. `SchedulerWorker` enregistré via `AddHostedService<SchedulerWorker>()`. `TimeProvider.System` enregistré en Singleton dans `Program.cs`.
- 122/122 tests passent : 116 existants (0 régression) + 6 nouveaux `SchedulerWorkerTests`.
- `Microsoft.Extensions.TimeProvider.Testing 10.3.0` ajouté au projet de tests.

### File List

**Créés :**
- `src/Bet2InvestPoster/Workers/SchedulerWorker.cs`
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Program.cs` (suppression `using Bet2InvestPoster`, ajout `using Bet2InvestPoster.Workers`, remplacement `AddHostedService<Worker>` par `AddSingleton(TimeProvider.System)` + `AddHostedService<SchedulerWorker>`)
- `tests/Bet2InvestPoster.Tests/Bet2InvestPoster.Tests.csproj` (ajout `Microsoft.Extensions.TimeProvider.Testing 10.3.0`)
- `.bmadOutput/implementation-artifacts/5-1-scheduler-worker-execution-quotidienne-planifiee.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

**Supprimés :**
- `src/Bet2InvestPoster/Worker.cs` (remplacé par SchedulerWorker)
- `tests/Bet2InvestPoster.Tests/WorkerTests.cs` (remplacé par SchedulerWorkerTests)

**Non touchés :**
- `jtdev-bet2invest-scraper/` (submodule — interdit)
- `src/Bet2InvestPoster/Services/PostingCycleService.cs`
- `src/Bet2InvestPoster/Services/ExecutionStateService.cs`
- `src/Bet2InvestPoster/Configuration/PosterOptions.cs`
- `src/Bet2InvestPoster/Telegram/`

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-opus-4-6 (create-story) | Création story 5.1 — analyse exhaustive artifacts |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 2 créés, 2 supprimés, 2 modifiés. 122/122 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale — 3 MEDIUM + 2 LOW corrigés. M1: CultureInfo.InvariantCulture ajouté. M2: sync tests via TaskCompletionSource. M3: test boundary CalculateNextRun ajouté. L1: public→internal. L2: catch OperationCanceledException conservé (correct). 123/123 tests verts |
