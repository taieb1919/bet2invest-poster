# Story 10.3: Résilience Polly Avancée et Health Checks

Status: review

## Story

As a l'utilisateur,
I want que le système gère les pannes de manière plus intelligente et expose un endpoint de santé,
so that le service soit plus résilient et monitorable en production.

## Acceptance Criteria

1. **Given** le `ResiliencePipeline` Polly existant **When** le pipeline est configuré **Then** un circuit breaker est ajouté : après 3 échecs consécutifs, le circuit s'ouvre pendant 5 minutes (NFR14) **And** le retry utilise un backoff exponentiel au lieu d'un délai fixe (60s → 60s, 120s, 240s) **And** les paramètres du circuit breaker sont configurables via `PosterOptions`
2. **Given** le circuit breaker ouvert **When** un cycle est déclenché (automatique ou `/run`) **Then** le cycle échoue immédiatement avec `"🔴 Circuit breaker actif — service API indisponible. Réessai automatique dans {minutes} min."` **And** une notification Telegram est envoyée
3. **Given** le service en cours d'exécution **When** une requête HTTP GET arrive sur `/health` **Then** le endpoint retourne `200 OK` avec : statut du service, dernière exécution, état du circuit breaker, connexion API (NFR15)
4. **Given** le service en cours d'exécution **When** une requête HTTP GET arrive sur `/health` et le circuit breaker est ouvert **Then** le endpoint retourne `503 Service Unavailable` avec le détail

## Tasks / Subtasks

- [x] Task 1 : Ajouter les propriétés circuit breaker dans `PosterOptions` (AC: #1)
  - [x] 1.1 Ajouter `CircuitBreakerFailureThreshold` (int, défaut: 3)
  - [x] 1.2 Ajouter `CircuitBreakerDurationSeconds` (int, défaut: 300)
  - [x] 1.3 Ajouter les valeurs dans `appsettings.json` section Poster
- [x] Task 2 : Refactorer `ResiliencePipelineService` — backoff exponentiel + circuit breaker (AC: #1, #2)
  - [x] 2.1 Changer `BackoffType` de `Constant` à `Exponential` dans `RetryStrategyOptions`
  - [x] 2.2 Ajouter `CircuitBreakerStrategyOptions` au pipeline via `builder.AddCircuitBreaker()`
  - [x] 2.3 Exposer l'état du circuit breaker via une propriété/méthode sur l'interface
  - [x] 2.4 Logger les transitions du circuit breaker (ouvert/fermé/half-open) avec Step `Cycle`
- [x] Task 3 : Gérer le circuit breaker ouvert dans `SchedulerWorker` et `RunCommandHandler` (AC: #2)
  - [x] 3.1 Détecter `BrokenCircuitException` dans le catch de `SchedulerWorker`
  - [x] 3.2 Envoyer notification Telegram spécifique circuit breaker
  - [x] 3.3 Détecter `BrokenCircuitException` dans `RunCommandHandler` et répondre avec message approprié
- [x] Task 4 : Ajouter le health check endpoint `/health` (AC: #3, #4)
  - [x] 4.1 SDK changé de `Microsoft.NET.Sdk.Worker` à `Microsoft.NET.Sdk.Web` (inclut ASP.NET Core + Health Checks)
  - [x] 4.2 Créer `Bet2InvestHealthCheck` qui expose statut service, dernière exécution, état circuit breaker, connexion API
  - [x] 4.3 Configurer un endpoint HTTP minimal sur port configurable via `HealthCheckPort` (défaut: 8080)
  - [x] 4.4 Retourner 200 si tout OK, 503 si circuit breaker ouvert
- [x] Task 5 : Tests unitaires (AC: #1, #2, #3, #4)
  - [x] 5.1 Test : état initial circuit breaker = Closed
  - [x] 5.2 Test : circuit breaker s'ouvre après N échecs consécutifs
  - [x] 5.3 Test : `BrokenCircuitException` levée quand circuit ouvert
  - [x] 5.4 Test : health check retourne Healthy quand service OK
  - [x] 5.5 Test : health check retourne Unhealthy quand circuit breaker ouvert
  - [x] 5.6 Test : valeurs par défaut PosterOptions circuit breaker

## Dev Notes

### Ce qui existe déjà — ResiliencePipelineService

`src/Bet2InvestPoster/Services/ResiliencePipelineService.cs` implémente un retry Polly avec :
- `BackoffType.Constant` (délai fixe entre tentatives)
- `MaxRetryAttempts = MaxRetryCount - 1` (3 tentatives totales par défaut)
- Exclusion de `OperationCanceledException`
- Logging de chaque tentative avec Step `Cycle`
- Enregistré en **Singleton** dans `Program.cs:91`

Le pipeline est construit une seule fois au démarrage via le constructeur. Les paramètres viennent de `IOptions<PosterOptions>`.

### Changement 1 : Backoff exponentiel

Changement minimal dans `ResiliencePipelineService.cs` :

```csharp
// AVANT
BackoffType = DelayBackoffType.Constant,

// APRÈS
BackoffType = DelayBackoffType.Exponential,
```

Avec `Delay = 60s` et `BackoffType.Exponential`, Polly.Core produit : 60s, 120s, 240s (facteur 2x par défaut). Conforme à l'AC #1.

### Changement 2 : Circuit Breaker

Polly.Core 8.6.5 supporte `AddCircuitBreaker()` dans le `ResiliencePipelineBuilder`. L'ordre est **important** : le circuit breaker doit être ajouté **AVANT** le retry pour que le retry ne tente pas de contourner un circuit ouvert.

```csharp
// ORDRE CORRECT dans le builder :
builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatio = 1.0,  // 100% — toute séquence d'échecs consécutifs compte
    MinimumThroughput = opts.CircuitBreakerFailureThreshold,  // 3 par défaut
    SamplingDuration = TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds * 2),
    BreakDuration = TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds),
    ShouldHandle = new PredicateBuilder()
        .Handle<Exception>(ex => ex is not OperationCanceledException),
    OnOpened = args => { /* log circuit ouvert */ return ValueTask.CompletedTask; },
    OnClosed = args => { /* log circuit fermé */ return ValueTask.CompletedTask; },
    OnHalfOpened = args => { /* log half-open */ return ValueTask.CompletedTask; }
});

// PUIS le retry
builder.AddRetry(new RetryStrategyOptions { ... });
```

**ATTENTION** : Polly.Core `CircuitBreakerStrategyOptions` utilise `FailureRatio` et `MinimumThroughput`, pas un simple compteur. Pour simuler "3 échecs consécutifs", utiliser `FailureRatio = 1.0` avec `MinimumThroughput = 3`.

**Exception circuit ouvert** : Quand le circuit est ouvert, Polly lève `BrokenCircuitException`. C'est cette exception qu'il faut attraper dans `SchedulerWorker` et `RunCommandHandler`.

### Changement 3 : Exposer l'état du circuit breaker

`IResiliencePipelineService` doit exposer l'état du circuit breaker pour le health check et les messages de notification :

```csharp
public interface IResiliencePipelineService
{
    Task ExecuteCycleWithRetryAsync(Func<CancellationToken, Task> cycleAction, CancellationToken ct = default);
    CircuitBreakerState GetCircuitBreakerState();  // NEW
    TimeSpan? GetCircuitBreakerRemainingDuration(); // NEW (optionnel — pour le message)
}
```

Polly.Core ne fournit pas directement l'état du circuit breaker en dehors du pipeline. **Solution** : tracker l'état manuellement via les callbacks `OnOpened`/`OnClosed`/`OnHalfOpened` dans des champs privés.

```csharp
private volatile CircuitBreakerState _circuitState = CircuitBreakerState.Closed;
private DateTimeOffset? _circuitOpenedAt;

// Dans OnOpened callback:
_circuitState = CircuitBreakerState.Open;
_circuitOpenedAt = DateTimeOffset.UtcNow;

// Dans OnClosed callback:
_circuitState = CircuitBreakerState.Closed;
_circuitOpenedAt = null;

// Dans OnHalfOpened callback:
_circuitState = CircuitBreakerState.HalfOpen;
```

**Enum** à créer (dans le même fichier ou dans Models/) :
```csharp
public enum CircuitBreakerState { Closed, Open, HalfOpen }
```

### Changement 4 : Health Check Endpoint

Le Worker Service utilise `Host.CreateApplicationBuilder()` qui fournit déjà un host compatible ASP.NET Core minimal. Pour ajouter un endpoint HTTP `/health` :

**Option recommandée** : Utiliser `Microsoft.Extensions.Diagnostics.HealthChecks` (déjà inclus dans le SDK .NET 9) + un Kestrel minimal.

```csharp
// Program.cs — ajouter AVANT builder.Build()
builder.Services.AddHealthChecks()
    .AddCheck<Bet2InvestHealthCheck>("bet2invest");

// Ajouter un endpoint HTTP minimal
builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(healthCheckPort);
});
```

**ATTENTION** : `Host.CreateApplicationBuilder()` n'est PAS `WebApplication.CreateBuilder()`. Pour exposer un endpoint HTTP depuis un Worker Service, il faut soit :

1. **Option A** : Migrer vers `WebApplication.CreateBuilder()` et garder les `HostedService` — change `Program.cs` mais tout le reste fonctionne
2. **Option B** : Ajouter un `BackgroundService` dédié qui écoute sur un port TCP avec un `HttpListener` minimal
3. **Option C** : Utiliser `GenericHost` avec `Microsoft.AspNetCore.Server.Kestrel` ajouté manuellement

**Option A recommandée** car elle est la plus propre et compatible .NET 9. Le changement dans `Program.cs` est minimal :

```csharp
// AVANT
var builder = Host.CreateApplicationBuilder(args);

// APRÈS
var builder = WebApplication.CreateBuilder(args);

// ... tout le reste identique ...

var app = builder.Build();  // au lieu de var host = builder.Build()
app.MapHealthChecks("/health");
app.Run();
```

**IMPORTANT** : `WebApplication.CreateBuilder()` inclut tout ce que `Host.CreateApplicationBuilder()` fournit PLUS Kestrel et le routing. Les `AddHostedService`, `AddSingleton`, `Configure<T>` fonctionnent identiquement. Aucun service existant n'est impacté.

**Package à ajouter** : `Microsoft.AspNetCore.Diagnostics.HealthChecks` — disponible dans le métapackage ASP.NET Core 9, donc il suffit de changer le SDK du csproj :

```xml
<!-- AVANT -->
<Project Sdk="Microsoft.NET.Sdk.Worker">

<!-- APRÈS -->
<Project Sdk="Microsoft.NET.Sdk.Web">
```

**ATTENTION** : Changer le SDK de `Worker` à `Web` ajoute automatiquement les références ASP.NET Core. Tous les packages NuGet existants restent compatibles. Le `AddSystemd()` fonctionne aussi avec le SDK Web.

**Port configurable** : Ajouter `HealthCheckPort` dans `PosterOptions` (défaut: 8080). Configurer Kestrel :

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    var port = builder.Configuration.GetValue<int?>("Poster:HealthCheckPort") ?? 8080;
    options.ListenAnyIP(port);
});
```

### Custom Health Check

Créer `Services/Bet2InvestHealthCheck.cs` implémentant `IHealthCheck` :

```csharp
public class Bet2InvestHealthCheck : IHealthCheck
{
    private readonly IExecutionStateService _stateService;
    private readonly IResiliencePipelineService _resilienceService;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var state = _stateService.GetState();
        var circuitState = _resilienceService.GetCircuitBreakerState();

        var data = new Dictionary<string, object>
        {
            ["service"] = "running",
            ["lastExecution"] = state.LastRunTime?.ToString("o") ?? "never",
            ["lastResult"] = state.LastRunResult ?? "none",
            ["circuitBreaker"] = circuitState.ToString(),
            ["apiConnection"] = state.ApiConnectionStatus ? "connected" : "disconnected"
        };

        if (circuitState == CircuitBreakerState.Open)
            return Task.FromResult(HealthCheckResult.Unhealthy("Circuit breaker ouvert", data: data));

        return Task.FromResult(HealthCheckResult.Healthy("Service opérationnel", data: data));
    }
}
```

### Gestion du `BrokenCircuitException`

Dans `SchedulerWorker.ExecuteAsync` et `RunCommandHandler.HandleAsync`, ajouter un catch spécifique AVANT le catch générique :

```csharp
catch (Polly.CircuitBreaker.BrokenCircuitException)
{
    // Circuit breaker ouvert — pas un échec Polly classique
    var remaining = _resiliencePipelineService.GetCircuitBreakerRemainingDuration();
    var minutes = remaining?.TotalMinutes ?? 5;
    await _notificationService.SendMessageAsync(
        $"🔴 Circuit breaker actif — service API indisponible. Réessai automatique dans {minutes:F0} min.",
        CancellationToken.None);
}
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
catch (Exception ex) { /* existing final failure handler */ }
```

### Fichiers à modifier

| Fichier | Modification |
|---------|-------------|
| `src/Bet2InvestPoster/Configuration/PosterOptions.cs` | Ajouter `CircuitBreakerFailureThreshold`, `CircuitBreakerDurationSeconds`, `HealthCheckPort` |
| `src/Bet2InvestPoster/Services/IResiliencePipelineService.cs` | Ajouter `GetCircuitBreakerState()`, `GetCircuitBreakerRemainingDuration()` |
| `src/Bet2InvestPoster/Services/ResiliencePipelineService.cs` | Backoff exponentiel + circuit breaker + état exposé |
| `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` | Catch `BrokenCircuitException` |
| `src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs` | Catch `BrokenCircuitException` |
| `src/Bet2InvestPoster/Program.cs` | `WebApplication.CreateBuilder` + health checks + Kestrel port |
| `src/Bet2InvestPoster/Bet2InvestPoster.csproj` | SDK `Microsoft.NET.Sdk.Web` |
| `src/Bet2InvestPoster/appsettings.json` | Ajouter `CircuitBreakerFailureThreshold`, `CircuitBreakerDurationSeconds`, `HealthCheckPort` |

### Fichiers à créer

| Fichier | Contenu |
|---------|---------|
| `src/Bet2InvestPoster/Services/Bet2InvestHealthCheck.cs` | IHealthCheck custom |
| `src/Bet2InvestPoster/Models/CircuitBreakerState.cs` | Enum Closed/Open/HalfOpen |
| `tests/Bet2InvestPoster.Tests/Services/ResiliencePipelineServiceTests.cs` | Déjà existant — ajouter tests circuit breaker + backoff |
| `tests/Bet2InvestPoster.Tests/Services/Bet2InvestHealthCheckTests.cs` | Tests health check |

### Project Structure Notes

- Le changement SDK Worker → Web est la modification la plus impactante mais est rétrocompatible
- `Bet2InvestHealthCheck` va dans `Services/` (logique métier de monitoring)
- `CircuitBreakerState` enum va dans `Models/` (cohérent avec les autres modèles)
- Le health check endpoint est accessible sans authentification (monitoring externe)

### Testing Standards

- Pattern xUnit existant : Arrange → Act → Assert
- `ResiliencePipelineServiceTests.cs` existe déjà — étendre avec tests circuit breaker
- Pour tester le backoff exponentiel : vérifier que les délais croissent (mocker le temps ou vérifier la config)
- Pour tester le health check : instancier directement `Bet2InvestHealthCheck` avec des fakes
- Ne PAS tester le comportement interne de Polly — tester uniquement l'intégration (callbacks, état exposé)
- 245+ tests existants ne doivent pas casser

### Learnings Stories 10.1 et 10.2

1. Ne pas sur-ingénier : utiliser les fonctionnalités Polly.Core 8.6.5 existantes plutôt que réinventer
2. Pattern lecture anticipée de config dans `Program.cs` pour les options lues avant `Build()`
3. Quand on étend une interface (`IResiliencePipelineService`), mettre à jour TOUS les fakes dans les tests
4. `FakeNotificationService` partagé dans `tests/Helpers/` — l'utiliser pour les nouveaux tests
5. Les fakes doivent implémenter les nouvelles méthodes ajoutées aux interfaces

### Learnings Story 5.2 (Polly original)

La story 5.2 a posé les bases de `ResiliencePipelineService`. Points clés :
- Le service est Singleton (pipeline construit une fois)
- Le pipeline est utilisé par `SchedulerWorker` ET `RunCommandHandler`
- Les deux endpoints (`/run` et scheduling auto) gèrent l'échec final différemment

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 10.3]
- [Source: .bmadOutput/planning-artifacts/architecture.md#API & Communication Patterns]
- [Source: src/Bet2InvestPoster/Services/ResiliencePipelineService.cs — implémentation Polly actuelle]
- [Source: src/Bet2InvestPoster/Services/IResiliencePipelineService.cs — interface actuelle]
- [Source: src/Bet2InvestPoster/Workers/SchedulerWorker.cs — utilisation du pipeline]
- [Source: src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs — utilisation du pipeline]
- [Source: src/Bet2InvestPoster/Program.cs — DI registration et validation]
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs — options existantes]
- [Source: .bmadOutput/implementation-artifacts/10-2-rotation-logs-retention.md — learnings story précédente]
- [Source: .bmadOutput/implementation-artifacts/10-1-onboarding-guide-telegram.md — learnings onboarding]
- [Source: .bmadOutput/implementation-artifacts/5-2-resilience-polly-retry-du-cycle-complet.md — Polly original]
- [Source: https://www.pollydocs.org/strategies/circuit-breaker — Polly.Core 8.x circuit breaker docs]
- [Source: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks — .NET 9 health checks]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

Aucun blocage majeur. Le changement SDK Worker → Web est transparent grâce à la rétrocompatibilité .NET 9.

### Completion Notes List

- Task 1 : `PosterOptions` étendu avec `CircuitBreakerFailureThreshold` (3), `CircuitBreakerDurationSeconds` (300), `HealthCheckPort` (8080)
- Task 2 : `ResiliencePipelineService` refactoré — circuit breaker ajouté AVANT retry dans le pipeline, backoff changé de Constant à Exponential, état circuit breaker tracké via callbacks OnOpened/OnClosed/OnHalfOpened, `IResiliencePipelineService` étendu avec `GetCircuitBreakerState()` et `GetCircuitBreakerRemainingDuration()`
- Task 3 : `SchedulerWorker` et `RunCommandHandler` gèrent `BrokenCircuitException` avec message Telegram "🔴 Circuit breaker actif..."
- Task 4 : SDK changé vers Web, `Bet2InvestHealthCheck` créé, `Program.cs` migré vers `WebApplication`, endpoint `/health` configuré sur port 8080
- Task 5 : 16 nouveaux tests (261 total, 0 régression) — circuit breaker state, health check Healthy/Unhealthy, valeurs par défaut PosterOptions
- Fakes dans RunCommandHandlerTests et SchedulerWorkerTests mis à jour pour implémenter les nouvelles méthodes de `IResiliencePipelineService`

### File List

- `src/Bet2InvestPoster/Bet2InvestPoster.csproj` — SDK Worker → Web
- `src/Bet2InvestPoster/Configuration/PosterOptions.cs` — ajout CircuitBreakerFailureThreshold, CircuitBreakerDurationSeconds, HealthCheckPort
- `src/Bet2InvestPoster/Services/IResiliencePipelineService.cs` — ajout GetCircuitBreakerState(), GetCircuitBreakerRemainingDuration()
- `src/Bet2InvestPoster/Services/ResiliencePipelineService.cs` — circuit breaker + backoff exponentiel + état exposé
- `src/Bet2InvestPoster/Services/Bet2InvestHealthCheck.cs` — nouveau fichier : IHealthCheck custom
- `src/Bet2InvestPoster/Models/CircuitBreakerState.cs` — nouveau fichier : enum Closed/Open/HalfOpen
- `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` — catch BrokenCircuitException
- `src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs` — catch BrokenCircuitException
- `src/Bet2InvestPoster/Program.cs` — WebApplication + health checks + Kestrel port
- `src/Bet2InvestPoster/appsettings.json` — ajout CircuitBreakerFailureThreshold, CircuitBreakerDurationSeconds, HealthCheckPort
- `tests/Bet2InvestPoster.Tests/Services/ResiliencePipelineServiceTests.cs` — tests circuit breaker + valeurs par défaut
- `tests/Bet2InvestPoster.Tests/Services/Bet2InvestHealthCheckTests.cs` — nouveau fichier : tests health check
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/RunCommandHandlerTests.cs` — fake mis à jour
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs` — fake mis à jour

### Change Log

- 2026-02-25 : Story 10.3 implémentée — résilience Polly avancée (circuit breaker + backoff exponentiel) + health check endpoint `/health`

