# Story 5.2 : Résilience Polly — Retry du Cycle Complet

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que le système retente automatiquement en cas d'échec,
so that les erreurs temporaires (réseau, API) ne bloquent pas la publication quotidienne.

## Acceptance Criteria

1. **Given** un cycle de publication déclenché (automatique ou `/run`)
   **When** le cycle échoue (erreur réseau, API down, timeout)
   **Then** Polly.Core `ResiliencePipeline` retente le cycle complet jusqu'à 3 fois (FR12)

2. **Given** une tentative de retry configurée
   **When** le délai entre tentatives est appliqué
   **Then** le délai est de 60s par défaut, configurable via `PosterOptions.RetryDelayMs`

3. **Given** le pipeline Polly configuré
   **When** il est appliqué
   **Then** le pipeline wraps le cycle complet (scrape → select → publish), pas chaque appel individuel

4. **Given** chaque tentative de cycle
   **When** elle est exécutée ou échoue
   **Then** le numéro de tentative et l'erreur rencontrée sont loguées (numéro de tentative, `ExceptionType`)

5. **Given** les 3 tentatives ont toutes échoué
   **When** le pipeline Polly épuise toutes ses tentatives
   **Then** `NotificationService.NotifyFinalFailureAsync()` envoie l'alerte finale (FR18) avec le nombre de tentatives et l'erreur finale

6. **Given** le pipeline Polly correctement configuré et en production
   **When** les cycles quotidiens s'exécutent sur l'année
   **Then** le taux de succès quotidien cible est > 95% hors indisponibilité API bet2invest (NFR2)

## Tasks / Subtasks

- [x] Task 1 : Créer `ResiliencePipelineService` dans `Services/` (AC: #1, #2, #3, #4)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Services/IResiliencePipelineService.cs`
  - [x] 1.2 Créer `src/Bet2InvestPoster/Services/ResiliencePipelineService.cs`
  - [x] 1.3 Construire un `ResiliencePipeline` avec `RetryStrategyOptions` via Polly.Core 8.6.5
  - [x] 1.4 Configurer : `MaxRetryAttempts = PosterOptions.MaxRetryCount - 1`, délai fixe `PosterOptions.RetryDelayMs`, `ShouldHandle` → toutes exceptions sauf `OperationCanceledException`
  - [x] 1.5 Dans `OnRetry` callback : logger le numéro de tentative et l'exception avec `Step = "Cycle"`

- [x] Task 2 : Ajouter `NotifyFinalFailureAsync` dans `NotificationService` (AC: #5)
  - [x] 2.1 Ajouter méthode `NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct)` dans `INotificationService`
  - [x] 2.2 Implémenter dans `NotificationService` : message `"❌ Échec définitif — {reason} après {attempts} tentatives."`
  - [x] 2.3 Logger l'envoi avec `Step = "Notify"`

- [x] Task 3 : Intégrer Polly dans `SchedulerWorker` (AC: #1, #3, #4, #5)
  - [x] 3.1 Injecter `IResiliencePipelineService` dans `SchedulerWorker`
  - [x] 3.2 Wrapper l'appel `cycleService.RunCycleAsync(ct)` dans `pipeline.ExecuteAsync()`
  - [x] 3.3 Après épuisement des tentatives (catch finale dans SchedulerWorker) : appeler `NotifyFinalFailureAsync`
  - [x] 3.4 Vérifier que `OperationCanceledException` propagé correctement (pas retried, pas notifié)

- [x] Task 4 : Intégrer Polly dans `RunCommandHandler` (AC: #1, #3)
  - [x] 4.1 Injecter `IResiliencePipelineService` dans `RunCommandHandler`
  - [x] 4.2 Wrapper l'appel `RunCycleAsync()` dans `pipeline.ExecuteAsync()`
  - [x] 4.3 Après épuisement : envoyer réponse d'erreur dans le chat Telegram (ne pas appeler NotifyFinalFailureAsync — réponse directe)

- [x] Task 5 : Enregistrer `ResiliencePipelineService` dans DI (AC: #1)
  - [x] 5.1 Dans `Program.cs`, ajouter `builder.Services.AddSingleton<IResiliencePipelineService, ResiliencePipelineService>()`

- [x] Task 6 : Tests unitaires (AC: #1 à #5)
  - [x] 6.1 Créer `tests/Bet2InvestPoster.Tests/Services/ResiliencePipelineServiceTests.cs`
  - [x] 6.2 Créer `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs`
  - [x] 6.3 Tests pour `ResiliencePipelineService` :
    - `Execute_SuccessOnFirstAttempt_NeverRetries` ✅
    - `Execute_FailsThenSucceeds_RetriesAndSucceeds` ✅
    - `Execute_AllAttemptsExhausted_ThrowsAfterMaxRetries` ✅
    - `Execute_OperationCanceledException_NotRetried` ✅
    - `Execute_MaxRetryCount1_NoRetry` ✅ (bonus)
  - [x] 6.4 Tests pour intégration `SchedulerWorker` + Polly :
    - `SchedulerWorker_AllRetriesExhausted_CallsNotifyFinalFailure` ✅
    - `SchedulerWorker_SuccessAfterRetry_NoFinalFailureNotification` ✅
  - [x] 6.5 Build + test réussis, 0 régression — 123 → 130 tests (7 nouveaux), 0 échec

## Dev Notes

### Architecture — Où placer le pipeline Polly

**CRITIQUE** : Le pipeline Polly **ne doit pas** être dans `PostingCycleService`.

Raison : `PostingCycleService.RunCycleAsync()` contient déjà :
1. `_executionStateService.RecordFailure()`
2. `_notificationService.NotifyFailureAsync()`
3. `throw;` (re-throw explicite pour Polly)

Si Polly était dans `PostingCycleService`, chaque tentative déclencherait `NotifyFailureAsync`, ce qui inonderait Telegram de faux messages d'échec.

**Décision architecturale** :
- Pipeline Polly dans `SchedulerWorker` et `RunCommandHandler` (les appelants de `RunCycleAsync`)
- `PostingCycleService` reste inchangé
- La notification finale (FR18) est envoyée **après** l'épuisement de toutes les tentatives, pas à chaque tentative

### Polly.Core 8.6.5 — API ResiliencePipeline

**IMPORTANT** : Polly v8 (Polly.Core) a une API différente de Polly v7. Ne pas utiliser l'ancienne API.

```csharp
// Polly.Core 8.6.5 — API correcte
using Polly;
using Polly.Retry;

var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = maxRetries,   // ex: 3 → 4 tentatives au total (1 + 3 retries)
        Delay = TimeSpan.FromMilliseconds(retryDelayMs),
        BackoffType = DelayBackoffType.Constant,
        ShouldHandle = new PredicateBuilder()
            .Handle<Exception>(ex => ex is not OperationCanceledException),
        OnRetry = args =>
        {
            logger.LogWarning("Tentative {AttemptNumber}/{MaxRetries} échouée — {ExceptionType}",
                args.AttemptNumber + 1, maxRetries, args.Outcome.Exception?.GetType().Name);
            return ValueTask.CompletedTask;
        }
    })
    .Build();

// Exécution
await pipeline.ExecuteAsync(async ct => await cycleService.RunCycleAsync(ct), cancellationToken);
```

**Attention** : `MaxRetryAttempts = 3` signifie 3 **retries** après la 1ère tentative → 4 tentatives au total. L'AC dit "jusqu'à 3 fois" → interprétation : 3 tentatives au total → `MaxRetryAttempts = 2`. Vérifier l'intention avec le commentaire du code et les tests.

**Clarification sur "3 tentatives"** : L'AC dit "jusqu'à 3 fois" et le PRD dit "jusqu'à 3 tentatives" (FR12). `MaxRetryAttempts` dans Polly v8 = nombre de retries (après la 1ère tentative). Pour 3 tentatives au total : `MaxRetryAttempts = 2`. Pour 3 retries (4 tentatives au total) : `MaxRetryAttempts = 3`. L'architecture précise "3 tentatives, 60s délai" → utiliser `MaxRetryAttempts = PosterOptions.MaxRetryCount - 1` ou documenter clairement la sémantique dans `PosterOptions`.

**Recommandation** : `PosterOptions.MaxRetryCount = 3` représente le nombre de **tentatives totales** (pas les retries). Donc : `MaxRetryAttempts = options.MaxRetryCount - 1` (= 2 retries, 3 tentatives au total).

### IResiliencePipelineService — Interface minimale

```csharp
// src/Bet2InvestPoster/Services/IResiliencePipelineService.cs
namespace Bet2InvestPoster.Services;

public interface IResiliencePipelineService
{
    Task ExecuteCycleWithRetryAsync(
        Func<CancellationToken, Task> cycleAction,
        CancellationToken ct = default);
}
```

### ResiliencePipelineService — Implémentation

```csharp
// src/Bet2InvestPoster/Services/ResiliencePipelineService.cs
using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class ResiliencePipelineService : IResiliencePipelineService
{
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ResiliencePipelineService> _logger;

    public ResiliencePipelineService(
        IOptions<PosterOptions> options,
        ILogger<ResiliencePipelineService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        // MaxRetryCount = 3 tentatives totales → 2 retries
        var maxRetries = Math.Max(0, opts.MaxRetryCount - 1);
        var delay = TimeSpan.FromMilliseconds(opts.RetryDelayMs);

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                Delay = delay,
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    using (LogContext.PushProperty("Step", "Cycle"))
                    {
                        _logger.LogWarning(
                            "Tentative {Attempt}/{MaxRetries} échouée — {ExceptionType}: retente dans {Delay}s",
                            args.AttemptNumber + 1, opts.MaxRetryCount,
                            args.Outcome.Exception?.GetType().Name ?? "Unknown",
                            delay.TotalSeconds);
                    }
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task ExecuteCycleWithRetryAsync(
        Func<CancellationToken, Task> cycleAction,
        CancellationToken ct = default)
    {
        await _pipeline.ExecuteAsync(
            async token => await cycleAction(token),
            ct);
    }
}
```

### Intégration dans SchedulerWorker

**Modification du bloc try/catch existant dans `SchedulerWorker`** :

```csharp
// AVANT (Story 5.1) :
try
{
    using var scope = _serviceProvider.CreateScope();
    var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
    await cycleService.RunCycleAsync(stoppingToken);
}
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
{
    throw;
}
catch (Exception ex)
{
    using (LogContext.PushProperty("Step", "Schedule"))
    {
        _logger.LogError(ex, "Cycle échoué — reprise au prochain run planifié");
    }
}

// APRÈS (Story 5.2 — ajouter IResiliencePipelineService dans constructeur) :
try
{
    await _resiliencePipelineService.ExecuteCycleWithRetryAsync(async ct =>
    {
        using var scope = _serviceProvider.CreateScope();
        var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
        await cycleService.RunCycleAsync(ct);
    }, stoppingToken);
}
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
{
    throw;
}
catch (Exception ex)
{
    // Toutes les tentatives Polly épuisées — notifier l'échec définitif (FR18)
    using (LogContext.PushProperty("Step", "Schedule"))
    {
        _logger.LogError(ex, "Toutes les tentatives épuisées — cycle définitivement échoué");
    }
    await _notificationService.NotifyFinalFailureAsync(
        _posterOptions.MaxRetryCount, ex.GetType().Name, stoppingToken);
}
```

**IMPORTANT** : Un nouveau scope DI doit être créé pour **chaque tentative** Polly, pas un seul scope partagé entre les retries. Sinon les services Scoped (en état d'erreur après un échec) sont réutilisés.

### Intégration dans RunCommandHandler

```csharp
// RunCommandHandler.cs — modifier HandleAsync pour wrapper avec Polly
// Injecter IResiliencePipelineService dans le constructeur

try
{
    await _resiliencePipelineService.ExecuteCycleWithRetryAsync(async ct =>
    {
        using var scope = _serviceProvider.CreateScope();
        var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
        await cycleService.RunCycleAsync(ct);
    }, cancellationToken);
    // Succès déjà notifié par PostingCycleService
}
catch (Exception ex)
{
    // Toutes tentatives épuisées — répondre directement dans le chat
    await _botClient.SendMessage(chatId,
        $"❌ Commande /run — échec définitif après {_posterOptions.MaxRetryCount} tentatives : {ex.GetType().Name}",
        cancellationToken: cancellationToken);
}
```

### NotifyFinalFailureAsync — Nouvelle méthode

```csharp
// Dans INotificationService.cs — ajouter :
Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default);

// Dans NotificationService.cs — implémenter :
public async Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default)
{
    using (LogContext.PushProperty("Step", "Notify"))
    {
        _logger.LogWarning("Notification échec définitif — {Attempts} tentatives, raison: {Reason}",
            attempts, reason);
    }
    await _botClient.SendMessage(
        _authorizedChatId,
        $"❌ Échec définitif — {reason} après {attempts} tentatives.",
        cancellationToken: ct);
}
```

### Comportement NotificationService existant — CRITIQUE

`PostingCycleService` appelle déjà `NotifyFailureAsync` à **chaque** cycle échoué. Avec Polly, chaque tentative déclenchera donc un `NotifyFailureAsync`. C'est le comportement **voulu** pour les retries intermédiaires — l'utilisateur est informé de chaque échec.

La notification finale (FR18) via `NotifyFinalFailureAsync` est une notification **supplémentaire** indiquant que toutes les tentatives ont été épuisées.

Résumé du flux pour 3 tentatives toutes échouées :
1. Tentative 1 échoue → `NotifyFailureAsync("NetworkException")` (par PostingCycleService)
2. Tentative 2 échoue → `NotifyFailureAsync("NetworkException")` (par PostingCycleService)
3. Tentative 3 échoue → `NotifyFailureAsync("NetworkException")` + `NotifyFinalFailureAsync(3, "NetworkException")` (finale)

Si ce doublon n'est pas souhaité, modifier `PostingCycleService` pour ne pas notifier quand Polly est actif — mais cela nécessiterait un couplage non souhaitable. **Conserver le comportement actuel** : la notification finale est un résumé explicite pour FR18.

### DI — Enregistrement ResiliencePipelineService

```csharp
// Dans Program.cs, ajouter après les services existants :
// ResiliencePipelineService: Singleton — builds ResiliencePipeline once from config.
builder.Services.AddSingleton<IResiliencePipelineService, ResiliencePipelineService>();
```

**Pourquoi Singleton** : Le `ResiliencePipeline` est construit une fois à l'initialisation et est thread-safe pour l'exécution parallèle. Pas besoin de le recréer à chaque scope.

### Tests — Pattern recommandé

```csharp
// Pas de Moq/NSubstitute — fakes minimaux en nested class
private class FakePostingCycleService : IPostingCycleService
{
    private int _callCount;
    public int CallCount => _callCount;
    public int FailCount { get; set; } = 0; // Échoue les N premières tentatives

    public Task RunCycleAsync(CancellationToken ct = default)
    {
        _callCount++;
        if (_callCount <= FailCount)
            throw new InvalidOperationException($"Simulated failure #{_callCount}");
        return Task.CompletedTask;
    }
}

private class FakeNotificationService : INotificationService
{
    public int NotifyFailureCount { get; private set; }
    public int NotifyFinalFailureCount { get; private set; }
    public string? LastFinalFailureReason { get; private set; }

    public Task NotifySuccessAsync(int count, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyFailureAsync(string reason, CancellationToken ct = default)
    {
        NotifyFailureCount++;
        return Task.CompletedTask;
    }
    public Task NotifyFinalFailureAsync(int attempts, string reason, CancellationToken ct = default)
    {
        NotifyFinalFailureCount++;
        LastFinalFailureReason = reason;
        return Task.CompletedTask;
    }
}
```

**Tests ResiliencePipelineService avec délai court** :
```csharp
// Pour les tests, configurer RetryDelayMs = 0 pour éviter les délais d'attente
var options = Options.Create(new PosterOptions
{
    MaxRetryCount = 3,
    RetryDelayMs = 0  // Pas de délai dans les tests
});
```

### Project Structure Notes

- `ResiliencePipelineService.cs` → `src/Bet2InvestPoster/Services/` — conforme à l'architecture
- `IResiliencePipelineService.cs` → `src/Bet2InvestPoster/Services/` — interface-per-service
- Modifications : `SchedulerWorker.cs`, `RunCommandHandler.cs`, `INotificationService.cs`, `NotificationService.cs`, `Program.cs`
- Tests : `tests/Bet2InvestPoster.Tests/Services/ResiliencePipelineServiceTests.cs`

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Pipeline Polly | `ResiliencePipeline` de Polly.Core 8.6.5 | [Architecture: API & Communication Patterns] |
| Wrapping | Cycle complet (scrape → select → publish) | [Architecture: Process Patterns — Retry] |
| Tentatives | 3 totales via `PosterOptions.MaxRetryCount` | [Architecture: Process Patterns — Retry] |
| Délai | 60s via `PosterOptions.RetryDelayMs` | [Architecture: Process Patterns — Retry] |
| Notification finale | `NotifyFinalFailureAsync` après épuisement | [Epics: AC5 FR18] |
| Scope DI par tentative | Nouveau scope par retry | [Architecture: DI Pattern — Scoped per cycle] |
| Logging Step | `Cycle` pour retry logs | [Architecture: Serilog Steps] |
| Submodule | Jamais modifié | [Architecture: Boundaries] |

### Intelligence Story Précédente (Story 5.1)

**Learnings applicables à Story 5.2 :**

1. **Scope DI par cycle** : Créer un nouveau scope **pour chaque tentative** Polly, pas un scope partagé. Sinon les services Scoped corrompus après une exception sont réutilisés.

2. **OperationCanceledException** : Toujours propagé sans retry. Le `ShouldHandle` Polly doit exclure `OperationCanceledException`.

3. **SchedulerWorker.cs signatured final** :
   ```
   src/Bet2InvestPoster/Workers/SchedulerWorker.cs
   ```
   Constructeur : `IServiceProvider`, `IExecutionStateService`, `IOptions<PosterOptions>`, `TimeProvider`, `ILogger<SchedulerWorker>`
   → Ajouter `IResiliencePipelineService` et `INotificationService`

4. **123 tests existants** : baseline à ne pas régresser (Story 5.1 code review a corrigé 3 MEDIUM + 2 LOW, résultat 123/123).

5. **`LogContext.PushProperty("Step", ...)`** : Toujours utiliser ce pattern pour les logs.

6. **Pattern commit** :
   ```
   feat(resilience): Polly retry cycle complet - story 5.2
   ```

### Fichiers à CRÉER

```
src/Bet2InvestPoster/
└── Services/
    ├── IResiliencePipelineService.cs    ← NOUVEAU
    └── ResiliencePipelineService.cs     ← NOUVEAU
tests/Bet2InvestPoster.Tests/
└── Services/
    └── ResiliencePipelineServiceTests.cs ← NOUVEAU
```

### Fichiers à MODIFIER

```
src/Bet2InvestPoster/
├── Services/
│   ├── INotificationService.cs          ← MODIFIER (ajouter NotifyFinalFailureAsync)
│   └── NotificationService.cs           ← MODIFIER (implémenter NotifyFinalFailureAsync)
├── Workers/
│   └── SchedulerWorker.cs               ← MODIFIER (intégrer IResiliencePipelineService)
├── Telegram/
│   └── Commands/
│       └── RunCommandHandler.cs         ← MODIFIER (intégrer IResiliencePipelineService)
└── Program.cs                           ← MODIFIER (enregistrer ResiliencePipelineService)
```

### Fichiers à NE PAS TOUCHER

```
jtdev-bet2invest-scraper/                          ← SUBMODULE — INTERDIT
src/Bet2InvestPoster/Services/PostingCycleService.cs ← NE PAS MODIFIER
src/Bet2InvestPoster/Services/IPostingCycleService.cs ← NE PAS MODIFIER
src/Bet2InvestPoster/Configuration/PosterOptions.cs  ← NE PAS MODIFIER (tout est déjà là)
src/Bet2InvestPoster/Workers/SchedulerWorker.cs      ← MODIFIER uniquement le bloc try/catch + constructeur
```

### References

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-5.2] — AC originaux, FR12, FR18, NFR2
- [Source: .bmadOutput/planning-artifacts/architecture.md#API-Communication-Patterns] — Polly.Core 8.6.5, 3 tentatives, 60s
- [Source: .bmadOutput/planning-artifacts/architecture.md#Process-Patterns] — ResiliencePipeline, wraps cycle complet
- [Source: .bmadOutput/planning-artifacts/architecture.md#DI-Pattern] — Scoped per cycle, Singleton pipeline
- [Source: src/Bet2InvestPoster/Services/PostingCycleService.cs:81] — `// Re-throw pour que Polly (Epic 5) puisse retenter`
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs:8-9] — `RetryDelayMs = 60000`, `MaxRetryCount = 3`
- [Source: src/Bet2InvestPoster/Workers/SchedulerWorker.cs:157] — commentaire "Story 5.2 ajoutera le retry Polly ici"
- [Source: .bmadOutput/implementation-artifacts/5-1-scheduler-worker-execution-quotidienne-planifiee.md] — patterns scope DI, OperationCanceledException, 123 tests baseline

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `Polly.Core 8.6.5` : `MaxRetryAttempts` doit être >= 1. Cas `MaxRetryCount=1` (0 retries) géré en skippant le retry strategy — pipeline execute directement.
- `Execute_OperationCanceledException_NotRetried` : token pré-annulé → Polly n'appelle pas l'action (callCount=0). Corrigé : utiliser `CancellationToken.None` + throw depuis l'action.
- Fakes `INotificationService` dans `PostingCycleServiceTests` et `PostingCycleServiceNotificationTests` mis à jour avec `NotifyFinalFailureAsync`.
- `SchedulerWorkerTests.CreateWorker` refactorisé en retournant un tuple pour exposer `FakeNotificationService`.
- `RunCommandHandlerTests` : suppression des assertions sur `stateService.LastRunSuccess` (état désormais géré exclusivement par `PostingCycleService`, pas par `RunCommandHandler`).

### Completion Notes List

- AC#1 : `ResiliencePipelineService` — `ResiliencePipeline` via Polly.Core 8.6.5, `MaxRetryCount=3` → 2 retries (3 tentatives totales), wraps cycle complet.
- AC#2 : délai 60s configurable via `PosterOptions.RetryDelayMs` (default 60000ms).
- AC#3 : pipeline dans `SchedulerWorker` et `RunCommandHandler`, pas dans `PostingCycleService` — wrap du cycle complet.
- AC#4 : `OnRetry` callback logue `AttemptNumber+1 / MaxRetryCount` et `ExceptionType` avec `Step = "Cycle"`.
- AC#5 : `NotifyFinalFailureAsync(int attempts, string reason)` ajoutée à `INotificationService` + `NotificationService`. Appelée dans le catch final de `SchedulerWorker` après épuisement de toutes les tentatives.
- AC#6 : NFR2 → supporté par l'architecture (3 tentatives avec retry 60s).
- 130/130 tests passent : 123 existants (0 régression) + 7 nouveaux.

### File List

**Créés :**
- `src/Bet2InvestPoster/Services/IResiliencePipelineService.cs`
- `src/Bet2InvestPoster/Services/ResiliencePipelineService.cs`
- `tests/Bet2InvestPoster.Tests/Services/ResiliencePipelineServiceTests.cs`
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Services/INotificationService.cs` (ajout `NotifyFinalFailureAsync`)
- `src/Bet2InvestPoster/Services/NotificationService.cs` (implémentation `NotifyFinalFailureAsync`)
- `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` (ajout `IResiliencePipelineService`, `INotificationService`, wrap Polly)
- `src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs` (ajout `IResiliencePipelineService`, `IOptions<PosterOptions>`, wrap Polly)
- `src/Bet2InvestPoster/Program.cs` (enregistrement `ResiliencePipelineService` Singleton)
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs` (ajout `NotifyFinalFailureAsync` dans fake)
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs` (ajout `NotifyFinalFailureAsync` dans fake)
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs` (fakes + `CreateWorker` → tuple)
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/RunCommandHandlerTests.cs` (nouveaux paramètres constructeur)
- `.bmadOutput/implementation-artifacts/5-2-resilience-polly-retry-du-cycle-complet.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

**Non touchés :**
- `jtdev-bet2invest-scraper/` (submodule — interdit)
- `src/Bet2InvestPoster/Services/PostingCycleService.cs`
- `src/Bet2InvestPoster/Services/IPostingCycleService.cs`
- `src/Bet2InvestPoster/Configuration/PosterOptions.cs`

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 5.2 — analyse exhaustive artifacts |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 4 créés, 9 modifiés. 130/130 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale : 1 HIGH, 2 MEDIUM, 1 LOW fixés. 132/132 tests verts. Status → done |
