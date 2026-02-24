# Story 4.3 : NotificationService — Notifications Automatiques

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want recevoir une notification Telegram après chaque exécution automatique,
so that je sois informé du résultat sans devoir vérifier manuellement.

## Acceptance Criteria

1. **Given** un cycle de publication terminé avec succès
   **When** `NotificationService` envoie la notification
   **Then** le message est `"✅ {count} pronostics publiés avec succès."` (FR16)

2. **Given** un cycle de publication terminé en échec
   **When** `NotificationService` envoie la notification
   **Then** le message est `"❌ Échec — {raison}. {détails retry}."` avec le détail de l'erreur (FR17)

3. **Given** toutes les tentatives de retry Polly ont échoué
   **When** `NotificationService` envoie la notification finale
   **Then** le message inclut le nombre de tentatives et l'erreur finale (FR18)

4. **Given** `PostingCycleService.RunCycleAsync()` termine (succès ou échec)
   **When** le cycle se termine
   **Then** `NotificationService` envoie la notification appropriée
   **And** `IExecutionStateService.RecordSuccess()` ou `RecordFailure()` est appelé pour mettre à jour l'état

5. **Given** `NotificationService` envoie un message
   **When** l'envoi se produit
   **Then** la notification est envoyée dans les 5 minutes suivant l'événement (NFR3)
   **And** chaque envoi est logué avec le Step `Notify`

6. **Given** `NotificationService` est enregistré en DI
   **When** l'application démarre
   **Then** `INotificationService` est injectable et `NotificationService` implémente `INotificationService`
   **And** `ITelegramBotClient` est enregistré en Singleton dans DI (partagé entre `TelegramBotService` et `NotificationService`)

## Tasks / Subtasks

- [x] Task 1 : Enregistrer `ITelegramBotClient` en Singleton dans DI (AC: #6)
  - [x] 1.1 Dans `Program.cs`, ajouter : `builder.Services.AddSingleton<ITelegramBotClient>(sp => { var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value; return new TelegramBotClient(opts.BotToken); });`
  - [x] 1.2 Modifier `TelegramBotService` pour injecter `ITelegramBotClient` depuis DI (au lieu de le créer en interne)
  - [x] 1.3 Vérifier que `TelegramBotService` utilise l'instance injectée pour le polling

- [x] Task 2 : Créer `INotificationService` et `NotificationService` (AC: #1, #2, #3, #5)
  - [x] 2.1 Créer `src/Bet2InvestPoster/Services/INotificationService.cs`
  - [x] 2.2 Interface avec `Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default)` et `Task NotifyFailureAsync(string reason, CancellationToken ct = default)`
  - [x] 2.3 Créer `src/Bet2InvestPoster/Services/NotificationService.cs`
  - [x] 2.4 Injecter `ITelegramBotClient`, `IOptions<TelegramOptions>`, `ILogger<NotificationService>`
  - [x] 2.5 `NotifySuccessAsync(int count)` : envoyer `"✅ {count} pronostics publiés avec succès."` au `AuthorizedChatId`
  - [x] 2.6 `NotifyFailureAsync(string reason)` : envoyer `"❌ Échec — {reason}."` au `AuthorizedChatId`
  - [x] 2.7 Logger chaque envoi avec `LogContext.PushProperty("Step", "Notify")`
  - [x] 2.8 Enregistrer en Singleton dans `Program.cs` : `builder.Services.AddSingleton<INotificationService, NotificationService>()`

- [x] Task 3 : Modifier `PostingCycleService` pour appeler `NotificationService` et mettre à jour `ExecutionStateService` (AC: #4)
  - [x] 3.1 Ajouter `INotificationService` et `IExecutionStateService` dans le constructeur de `PostingCycleService`
  - [x] 3.2 En cas de succès : appeler `_executionState.RecordSuccess(published)` puis `await _notificationService.NotifySuccessAsync(published, ct)`
  - [x] 3.3 En cas d'exception : appeler `_executionState.RecordFailure(ex.GetType().Name)` puis `await _notificationService.NotifyFailureAsync(ex.Message, ct)` puis re-throw
  - [x] 3.4 **IMPORTANT** : `PostingCycleService` est Scoped — `INotificationService` doit être Singleton ou Scoped compatible

- [x] Task 4 : Tests unitaires (AC: #1 à #6)
  - [x] 4.1 Créer `tests/Bet2InvestPoster.Tests/Services/NotificationServiceTests.cs`
  - [x] 4.2 Tests `NotificationService` :
    - `NotifySuccessAsync_SendsSuccessMessage` ✅
    - `NotifySuccessAsync_LogsWithNotifyStep` ✅
    - `NotifyFailureAsync_SendsFailureMessage` ✅
    - `NotifyFailureAsync_LogsWithNotifyStep` ✅
  - [x] 4.3 Créer `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs`
  - [x] 4.4 Tests `PostingCycleService` avec notifications :
    - `RunCycleAsync_Success_CallsRecordSuccessAndNotifySuccess` ✅
    - `RunCycleAsync_Failure_CallsRecordFailureAndNotifyFailure` ✅
    - `RunCycleAsync_Failure_RethrowsException` ✅
  - [x] 4.5 Build + test : `dotnet build Bet2InvestPoster.sln` + `dotnet test tests/Bet2InvestPoster.Tests`
  - [x] 4.6 Résultat attendu : 103 existants + ≥7 nouveaux = ≥110 tests, 0 échec

## Dev Notes

### Architecture — NotificationService et ITelegramBotClient DI

**Décision clé** : L'architecture impose que `NotificationService` soit le seul service autorisé à envoyer des messages sortants. Pour que `NotificationService` puisse envoyer via l'API Telegram, il doit accéder à `ITelegramBotClient`.

**Problème** : `TelegramBotService` crée actuellement son propre `TelegramBotClient` en interne. Pour partager le client entre `TelegramBotService` (polling) et `NotificationService` (envoi sortant), il faut enregistrer `ITelegramBotClient` en Singleton dans DI.

**Solution** :
```csharp
// Program.cs — enregistrer ITelegramBotClient en Singleton
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramBotClient(opts.BotToken);
});
```

Puis modifier `TelegramBotService` pour injecter `ITelegramBotClient` :
```csharp
public TelegramBotService(
    IOptions<TelegramOptions> options,
    AuthorizationFilter authFilter,
    IEnumerable<ICommandHandler> handlers,
    ITelegramBotClient botClient,       // ← injecté depuis DI
    ILogger<TelegramBotService> logger)
```

### INotificationService — Interface minimale

```csharp
// src/Bet2InvestPoster/Services/INotificationService.cs
namespace Bet2InvestPoster.Services;

public interface INotificationService
{
    Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default);
    Task NotifyFailureAsync(string reason, CancellationToken ct = default);
}
```

### NotificationService — Implémentation

```csharp
// src/Bet2InvestPoster/Services/NotificationService.cs
using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Telegram.Bot;

namespace Bet2InvestPoster.Services;

public class NotificationService : INotificationService
{
    private readonly ITelegramBotClient _botClient;
    private readonly long _chatId;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ITelegramBotClient botClient,
        IOptions<TelegramOptions> options,
        ILogger<NotificationService> logger)
    {
        _botClient = botClient;
        _chatId = options.Value.AuthorizedChatId;
        _logger = logger;
    }

    public async Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default)
    {
        var text = $"✅ {publishedCount} pronostics publiés avec succès.";
        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Envoi notification succès — {Count} pronostics", publishedCount);
        }
        await _botClient.SendMessage(_chatId, text, cancellationToken: ct);
    }

    public async Task NotifyFailureAsync(string reason, CancellationToken ct = default)
    {
        var text = $"❌ Échec — {reason}.";
        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogWarning("Envoi notification échec — {Reason}", reason);
        }
        await _botClient.SendMessage(_chatId, text, cancellationToken: ct);
    }
}
```

### Modification PostingCycleService

**IMPORTANT** : `INotificationService` est Singleton mais `PostingCycleService` est Scoped. Un Singleton peut être injecté dans un Scoped sans problème (contrairement au sens inverse).

```csharp
// src/Bet2InvestPoster/Services/PostingCycleService.cs — constructeur modifié
public PostingCycleService(
    IHistoryManager historyManager,
    ITipsterService tipsterService,
    IUpcomingBetsFetcher upcomingBetsFetcher,
    IBetSelector betSelector,
    IBetPublisher betPublisher,
    INotificationService notificationService,     // ← AJOUTER
    IExecutionStateService executionStateService, // ← AJOUTER
    ILogger<PostingCycleService> logger)
```

**Pattern try/catch dans `RunCycleAsync`** :
```csharp
public async Task RunCycleAsync(CancellationToken ct = default)
{
    using (LogContext.PushProperty("Step", "Cycle"))
    {
        _logger.LogInformation("Cycle de publication démarré");

        try
        {
            // Étapes existantes : purge → fetch → select → publish
            await _historyManager.PurgeOldEntriesAsync(ct);
            var tipsters = await _tipsterService.LoadTipstersAsync(ct);
            var candidates = await _upcomingBetsFetcher.FetchAllAsync(tipsters, ct);
            var selected = await _betSelector.SelectAsync(candidates, ct);
            var published = await _betPublisher.PublishAllAsync(selected, ct);

            _logger.LogInformation(
                "Cycle terminé — {Published} pronostics publiés sur {Candidates} candidats",
                published, candidates.Count);

            // Notifications (Story 4.3) ↓
            _executionStateService.RecordSuccess(published);
            await _notificationService.NotifySuccessAsync(published, ct);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("Step", "Cycle"))
            {
                _logger.LogError(ex, "Cycle échoué — {ExceptionType}: {Message}",
                    ex.GetType().Name, ex.Message);
            }

            // Sanitize : ne jamais exposer credentials dans les notifications
            var sanitizedReason = ex.GetType().Name;
            _executionStateService.RecordFailure(sanitizedReason);
            await _notificationService.NotifyFailureAsync(sanitizedReason, ct);

            throw; // Re-throw pour que Polly (Epic 5) puisse retenter
        }
    }
}
```

**CRITIQUE** : `ex.Message` peut contenir des credentials si l'exception est levée avant masquage. Utiliser `ex.GetType().Name` comme raison sanitisée pour les notifications (cohérent avec la correction H2 apportée en review story 4.2).

### Lifetime DI — Récapitulatif

| Service | Lifetime | Raison |
|---|---|---|
| `ITelegramBotClient` | **Singleton** | Connexion partagée TCP, thread-safe pour envois concurrents |
| `INotificationService` | **Singleton** | Stateless, injecte un Singleton |
| `IPostingCycleService` | **Scoped** | Un scope par exécution de cycle |

**Note** : `NotificationService` (Singleton) injecte `ITelegramBotClient` (Singleton) — OK. `PostingCycleService` (Scoped) injecte `INotificationService` (Singleton) — OK (Scoped peut injecter Singleton).

### Enregistrement DI dans Program.cs

```csharp
// Ajouter AVANT AddHostedService<TelegramBotService>()
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramBotClient(opts.BotToken);
});
builder.Services.AddSingleton<INotificationService, NotificationService>();
```

**Et mettre à jour la registration de `PostingCycleService`** — aucune modification nécessaire si déjà en `AddScoped<IPostingCycleService, PostingCycleService>()` : DI résoudra automatiquement les nouvelles dépendances.

### Telegram.Bot 22.9.0 — Envoi de Messages

```csharp
// Extension method à utiliser (identique à Story 4.2) :
await _botClient.SendMessage(
    chatId: _chatId,
    text: "votre message",
    cancellationToken: ct);
```

**NE PAS utiliser** `SendTextMessageAsync()` — déprécié en 22.x.

### Tests — FakeTelegramBotClient réutilisable

Le fichier `tests/Bet2InvestPoster.Tests/Telegram/Commands/FakeTelegramBotClient.cs` a été créé en story 4.2. **Le réutiliser** (ne pas dupliquer) pour les tests `NotificationServiceTests`.

```csharp
// Dans NotificationServiceTests.cs
using Bet2InvestPoster.Tests.Telegram.Commands; // ← import du FakeTelegramBotClient existant
```

### Boundaries à Respecter

- `NotificationService` **est le seul** service autorisé à envoyer des messages sortants (architecture)
- `TelegramBotService` reste le seul point de contact pour le **polling entrant**
- `RunCommandHandler` envoie des **réponses directes aux commandes** (déjà implémenté, ne pas modifier)
- `PostingCycleService` doit **re-throw** l'exception après notification pour permettre le retry Polly (Epic 5)
- `NotificationService` ne doit **jamais** envoyer de credentials ou tokens dans ses messages

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
└── Services/
    ├── INotificationService.cs       ← NOUVEAU
    └── NotificationService.cs        ← NOUVEAU

tests/Bet2InvestPoster.Tests/
└── Services/
    ├── NotificationServiceTests.cs              ← NOUVEAU
    └── PostingCycleServiceNotificationTests.cs  ← NOUVEAU
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
├── Services/
│   └── PostingCycleService.cs       ← MODIFIER (ajouter INotificationService + IExecutionStateService)
├── Telegram/
│   └── TelegramBotService.cs        ← MODIFIER (injecter ITelegramBotClient depuis DI)
└── Program.cs                       ← MODIFIER (ajouter ITelegramBotClient Singleton + INotificationService)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/            ← SUBMODULE — INTERDIT
src/Bet2InvestPoster/Telegram/Commands/   ← ne pas modifier (déjà correct)
src/Bet2InvestPoster/Telegram/Formatters/ ← ne pas modifier
src/Bet2InvestPoster/Services/IExecutionStateService.cs  ← ne pas modifier
src/Bet2InvestPoster/Services/ExecutionStateService.cs   ← ne pas modifier
```

### Exigences de Tests

**Framework** : xUnit. Pas de Moq/NSubstitute. Fakes minimaux en nested class ou réutilisation de `FakeTelegramBotClient` (créé en story 4.2).

**Fake INotificationService pour tests PostingCycleService** :
```csharp
private class FakeNotificationService : INotificationService
{
    public int SuccessCount { get; private set; }
    public int FailureCount { get; private set; }
    public string? LastFailureReason { get; private set; }

    public Task NotifySuccessAsync(int publishedCount, CancellationToken ct = default)
    {
        SuccessCount++;
        return Task.CompletedTask;
    }

    public Task NotifyFailureAsync(string reason, CancellationToken ct = default)
    {
        FailureCount++;
        LastFailureReason = reason;
        return Task.CompletedTask;
    }
}
```

**Fake IExecutionStateService pour tests** :
```csharp
private class FakeExecutionStateService : IExecutionStateService
{
    public int? LastSuccessCount { get; private set; }
    public string? LastFailureReason { get; private set; }

    public ExecutionState GetState() => new(null, null, null, null);
    public void RecordSuccess(int publishedCount) => LastSuccessCount = publishedCount;
    public void RecordFailure(string reason) => LastFailureReason = reason;
    public void SetNextRun(DateTimeOffset nextRunAt) { }
}
```

**Commandes de validation** :
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 103 existants + ≥7 nouveaux = ≥110 tests, 0 échec
```

### Intelligence Story Précédente (Story 4.2)

**Learnings critiques applicables à Story 4.3 :**

1. **`FakeTelegramBotClient` est dans `Bet2InvestPoster.Tests.Telegram.Commands`** — namespace spécifique à importer.

2. **Thread safety `ExecutionStateService`** : utilise un `lock` interne (correction M1 review 4.2). Pas besoin de modifier.

3. **Sanitize error messages** : utiliser `ex.GetType().Name` (pas `ex.Message`) dans les notifications pour éviter d'exposer des credentials — correction H2 review 4.2.

4. **`IMessageFormatter` interface** a été créée en review 4.2 (correction L1). Pattern à reproduire : toujours créer une interface pour les services.

5. **`Telegram.Bot 22.9.0`** : `SendMessage()` (extension method), pas `SendTextMessageAsync()`. Voir Dev Notes story 4.1.

6. **`volatile` ne fonctionne pas sur nullable value types** — ne pas l'utiliser. Les champs simples suffisent pour mono-writer.

7. **Pattern `LogContext.PushProperty("Step", "Notify")`** : wrapper `using` autour de tous les logs.

8. **`ITelegramBotClient` interface en 22.9.0** : le fake a besoin de `SendRequest<TResponse>`, `IExceptionParser`, `TGFile`. Réutiliser le `FakeTelegramBotClient` existant plutôt que d'en créer un nouveau.

### Intelligence Git

**Branche actuelle** : `epic-2/connexion-api` (nom historique conservé)

**Pattern de commit attendu** :
```
feat(telegram): NotificationService notifications automatiques - story 4.3
```

**Commits récents** :
```
f57df0e feat(telegram): RunCommandHandler StatusCommandHandler et MessageFormatter - story 4.2
bc29d84 feat(telegram): TelegramBotService polling et AuthorizationFilter sécurité - story 4.1
```

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement NotificationService | `Services/INotificationService.cs`, `NotificationService.cs` | [Architecture: Structure Patterns] |
| Lifetime NotificationService | Singleton (stateless) | [Architecture: DI Pattern] |
| Lifetime ITelegramBotClient | Singleton (connexion partagée) | [Architecture: DI Pattern] |
| Step logging notifications | `Notify` | [Architecture: Serilog Template] |
| Seul service sortant autorisé | `NotificationService` | [Architecture: Telegram Boundary] |
| Sanitize reason dans notifications | `ex.GetType().Name` (jamais `ex.Message`) | [Architecture: NFR5 — credentials jamais dans logs/messages] |

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-4.3] — AC originaux, FR16, FR17, FR18
- [Source: .bmadOutput/planning-artifacts/architecture.md#Telegram-Boundary] — `NotificationService` seul service sortant
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — `Services/` pour NotificationService
- [Source: .bmadOutput/planning-artifacts/architecture.md#DI-Pattern] — Singleton vs Scoped
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — NFR5, Step=Notify
- [Source: .bmadOutput/implementation-artifacts/4-2-commandes-run-et-status.md] — FakeTelegramBotClient, thread safety, sanitize errors, Telegram.Bot 22.9.0
- [Source: src/Bet2InvestPoster/Services/PostingCycleService.cs] — implémentation actuelle à modifier
- [Source: src/Bet2InvestPoster/Services/IExecutionStateService.cs] — interface RecordSuccess/RecordFailure
- [Source: src/Bet2InvestPoster/Program.cs] — pattern DI registration

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `AuthorizationFilterTests.TelegramBotService_RegisteredAsHostedService` : échoue après l'injection de `ITelegramBotClient` dans `TelegramBotService` (test DI ne l'enregistrait pas). Résolu : ajout de `FakeTelegramBotClient` dans le test.
- `PostingCycleServiceTests` : helper `CreateService` ne prenait pas `INotificationService`/`IExecutionStateService`. Mis à jour avec paramètres optionnels et fakes intégrés.

### Completion Notes List

- AC#1 : `NotificationService.NotifySuccessAsync(10)` → envoie `"✅ 10 pronostics publiés avec succès."` au `AuthorizedChatId` via `ITelegramBotClient`.
- AC#2 : `NotificationService.NotifyFailureAsync("InvalidOperationException")` → envoie `"❌ Échec — InvalidOperationException."`.
- AC#3 : PARTIEL via AC#2 — raison sanitisée = `ex.GetType().Name`. Le nombre de tentatives retry (FR18) sera complété en Epic 5 (Polly). TODO ajouté dans `INotificationService`.
- AC#4 : `PostingCycleService` appelle `_executionStateService.RecordSuccess(published)` puis `NotifySuccessAsync` en cas de succès, `RecordFailure` + `NotifyFailureAsync` en cas d'exception, puis re-throw.
- AC#5 : Chaque envoi logué avec `LogContext.PushProperty("Step", "Notify")`.
- AC#6 : `ITelegramBotClient` Singleton enregistré dans `Program.cs` ; `INotificationService` Singleton enregistré. `TelegramBotService` injecte `ITelegramBotClient` depuis DI.
- 116/116 tests passent : 103 existants (0 régression) + 6 `NotificationServiceTests` + 7 `PostingCycleServiceNotificationTests`.

### File List

**Créés :**
- `src/Bet2InvestPoster/Services/INotificationService.cs`
- `src/Bet2InvestPoster/Services/NotificationService.cs`
- `tests/Bet2InvestPoster.Tests/Services/NotificationServiceTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (injection `ITelegramBotClient` depuis DI)
- `src/Bet2InvestPoster/Services/PostingCycleService.cs` (ajout `INotificationService` + `IExecutionStateService`, try/catch, re-throw)
- `src/Bet2InvestPoster/Program.cs` (enregistrement `ITelegramBotClient` Singleton + `INotificationService`)
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs` (helper `CreateService` + test DI mis à jour)
- `tests/Bet2InvestPoster.Tests/Telegram/AuthorizationFilterTests.cs` (enregistrement `ITelegramBotClient` dans test DI)
- `.bmadOutput/implementation-artifacts/4-3-notification-service-notifications-automatiques.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

**Non touchés :**
- `jtdev-bet2invest-scraper/` (submodule — interdit)
- `src/Bet2InvestPoster/Telegram/AuthorizationFilter.cs`
- `src/Bet2InvestPoster/Telegram/Commands/`
- `src/Bet2InvestPoster/Telegram/Formatters/`
- `src/Bet2InvestPoster/Services/IExecutionStateService.cs`
- `src/Bet2InvestPoster/Services/ExecutionStateService.cs`

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 4.3 — analyse exhaustive artifacts |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 2 fichiers créés, 5 modifiés, 115/115 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale : 7 issues (2H/3M/2L) — tous fixés. H2=SendMessage dans using scope, M1=dédup fakes, M2=test chatId, L1=var bot supprimé. 116/116 tests verts |
