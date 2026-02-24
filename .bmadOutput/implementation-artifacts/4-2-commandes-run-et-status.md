# Story 4.2 : Commandes /run et /status

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want envoyer /run pour déclencher une publication manuelle et /status pour voir l'état du système,
so that je puisse contrôler et surveiller le service à tout moment.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé
   **When** l'utilisateur envoie `/run`
   **Then** `RunCommandHandler` déclenche `PostingCycleService.RunCycleAsync()` immédiatement (FR14)
   **And** le résultat (succès ou échec) est envoyé en réponse dans le chat

2. **Given** `PostingCycleService.RunCycleAsync()` termine avec succès
   **When** `RunCommandHandler` reçoit la confirmation
   **Then** le message de réponse indique le succès avec le nombre de pronostics publiés (si disponible)

3. **Given** `PostingCycleService.RunCycleAsync()` lève une exception
   **When** `RunCommandHandler` intercepte l'erreur
   **Then** le message de réponse indique l'échec avec un résumé de l'erreur (sans credentials)

4. **Given** le bot Telegram actif et l'utilisateur autorisé
   **When** l'utilisateur envoie `/status`
   **Then** `StatusCommandHandler` répond avec l'état du système formaté via `MessageFormatter` (FR15)
   **And** le message inclut : dernière exécution (date/heure + résultat), prochain run planifié, état de connexion API

5. **Given** `TelegramBotService.HandleUpdateAsync` reçoit un message autorisé
   **When** le texte du message commence par `/run` ou `/status`
   **Then** le handler approprié (`RunCommandHandler` ou `StatusCommandHandler`) est invoqué
   **And** les commandes inconnues reçoivent une réponse explicite (ex : `"Commande inconnue. Commandes disponibles : /run, /status"`)

6. **Given** les handlers de commandes sont enregistrés dans DI
   **When** `TelegramBotService` dispatche une commande
   **Then** `RunCommandHandler` et `StatusCommandHandler` implémentent `ICommandHandler`
   **And** `ICommandHandler` définit `bool CanHandle(string command)` et `Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)`
   **And** les handlers sont enregistrés en Singleton dans DI

## Tasks / Subtasks

- [x] Task 1 : Créer `ICommandHandler` (AC: #6)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Telegram/Commands/ICommandHandler.cs`
  - [x] 1.2 Interface avec `bool CanHandle(string command)` et `Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)`

- [x] Task 2 : Créer `RunCommandHandler` (AC: #1, #2, #3)
  - [x] 2.1 Créer `src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs`
  - [x] 2.2 Implémenter `ICommandHandler`
  - [x] 2.3 Injecter `IServiceScopeFactory`, `ILogger<RunCommandHandler>`
  - [x] 2.4 `CanHandle` retourne `true` pour `"/run"`
  - [x] 2.5 `HandleAsync` : créer un scope DI, résoudre `IPostingCycleService`, appeler `RunCycleAsync(ct)`
  - [x] 2.6 En cas de succès : `bot.SendMessage(chatId, "✅ Cycle exécuté avec succès.", ct: ct)`
  - [x] 2.7 En cas d'exception : `bot.SendMessage(chatId, $"❌ Échec — {ex.Message}", ct: ct)` (masquer credentials)
  - [x] 2.8 Logger chaque invocation avec Step `Notify`

- [x] Task 3 : Créer `ExecutionStateService` (AC: #4)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Services/IExecutionStateService.cs`
  - [x] 3.2 Créer `src/Bet2InvestPoster/Services/ExecutionStateService.cs`
  - [x] 3.3 Singleton — stocke en mémoire : `LastRunAt (DateTimeOffset?)`, `LastRunResult (string?)`, `LastRunSuccess (bool?)`, `NextRunAt (DateTimeOffset?)`
  - [x] 3.4 Méthodes : `RecordSuccess(int count)`, `RecordFailure(string reason)`, `SetNextRun(DateTimeOffset nextRun)`
  - [x] 3.5 Enregistrer en Singleton dans `Program.cs`
  - [x] 3.6 **Note** : Epic 5 (SchedulerWorker) utilisera `SetNextRun` — pour l'instant `NextRunAt` peut rester `null`

- [x] Task 4 : Créer `StatusCommandHandler` (AC: #4)
  - [x] 4.1 Créer `src/Bet2InvestPoster/Telegram/Commands/StatusCommandHandler.cs`
  - [x] 4.2 Implémenter `ICommandHandler`
  - [x] 4.3 Injecter `IExecutionStateService`, `MessageFormatter`, `ILogger<StatusCommandHandler>`
  - [x] 4.4 `CanHandle` retourne `true` pour `"/status"`
  - [x] 4.5 `HandleAsync` : obtenir l'état via `IExecutionStateService`, formater via `MessageFormatter.FormatStatus(state)`, envoyer le message
  - [x] 4.6 Logger avec Step `Notify`

- [x] Task 5 : Créer `MessageFormatter` (AC: #4)
  - [x] 5.1 Créer `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs`
  - [x] 5.2 Méthode `string FormatStatus(ExecutionState state)` : format multi-ligne lisible
  - [x] 5.3 Format :
    ```
    📊 État du système
    • Dernière exécution : {date/heure ou "Aucune"}
    • Résultat : {✅ Succès / ❌ Échec — {raison} ou "—"}
    • Prochain run : {date/heure ou "Non planifié"}
    ```

- [x] Task 6 : Modifier `TelegramBotService` pour dispatcher les commandes (AC: #5)
  - [x] 6.1 Injecter `IEnumerable<ICommandHandler> _handlers` dans `TelegramBotService`
  - [x] 6.2 Dans `HandleUpdateAsync` : extraire le texte de la commande (premier mot, lowercase)
  - [x] 6.3 Trouver le handler via `_handlers.FirstOrDefault(h => h.CanHandle(command))`
  - [x] 6.4 Si handler trouvé : appeler `await handler.HandleAsync(bot, update.Message!, ct)`
  - [x] 6.5 Si aucun handler : `bot.SendMessage(chatId, "Commande inconnue. Commandes disponibles : /run, /status", ct: ct)`
  - [x] 6.6 Logger la commande reçue avec Step `Notify`

- [x] Task 7 : Enregistrement DI (AC: #6)
  - [x] 7.1 Dans `Program.cs`, enregistrer `ICommandHandler` pour chaque handler en Singleton
  - [x] 7.2 `builder.Services.AddSingleton<ICommandHandler, RunCommandHandler>()`
  - [x] 7.3 `builder.Services.AddSingleton<ICommandHandler, StatusCommandHandler>()`
  - [x] 7.4 `builder.Services.AddSingleton<MessageFormatter>()`
  - [x] 7.5 `builder.Services.AddSingleton<IExecutionStateService, ExecutionStateService>()`
  - [x] 7.6 Placement : avant `AddHostedService<TelegramBotService>()`

- [x] Task 8 : Tests unitaires (AC: #1 à #6)
  - [x] 8.1 Créer `tests/Bet2InvestPoster.Tests/Telegram/Commands/RunCommandHandlerTests.cs`
  - [x] 8.2 Créer `tests/Bet2InvestPoster.Tests/Telegram/Commands/StatusCommandHandlerTests.cs`
  - [x] 8.3 Créer `tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterTests.cs`
  - [x] 8.4 Créer `tests/Bet2InvestPoster.Tests/Services/ExecutionStateServiceTests.cs`
  - [x] 8.5 Tests `RunCommandHandler` :
    - `CanHandle_Run_ReturnsTrue` ✅
    - `CanHandle_Status_ReturnsFalse` ✅
    - `HandleAsync_Success_CallsCycleServiceAndSendsSuccessMessage` ✅
    - `HandleAsync_Failure_SendsErrorMessage` ✅
  - [x] 8.6 Tests `StatusCommandHandler` :
    - `CanHandle_Status_ReturnsTrue` ✅
    - `CanHandle_Run_ReturnsFalse` ✅
    - `HandleAsync_NoHistory_SendsNoRunMessage` ✅
    - `HandleAsync_WithSuccessHistory_SendsSuccessMessage` ✅
  - [x] 8.7 Tests `MessageFormatter` :
    - `FormatStatus_NoRun_ContainsAucune` ✅
    - `FormatStatus_WithSuccess_ContainsSucces` ✅
    - `FormatStatus_WithFailure_ContainsEchec` ✅
    - `FormatStatus_WithNextRun_ContainsNextRunDate` ✅
    - `FormatStatus_ContainsSystemHeader` ✅
  - [x] 8.8 Tests `ExecutionStateService` :
    - `InitialState_AllPropertiesAreNull` ✅
    - `RecordSuccess_SetsLastRunAtAndResult` ✅
    - `RecordFailure_SetsLastRunSuccess_False` ✅
    - `SetNextRun_UpdatesNextRunAt` ✅
    - `RecordSuccess_AfterFailure_OverwritesState` ✅
  - [x] 8.9 Build + test : `dotnet build Bet2InvestPoster.sln` + `dotnet test tests/Bet2InvestPoster.Tests`
  - [x] 8.10 Résultat : 85 existants + 18 nouveaux = **103 tests, 0 échec** ✅

## Dev Notes

### Architecture — Dispatch des Commandes

**Pattern choisi : `IEnumerable<ICommandHandler>` injecté dans `TelegramBotService`**

Le pattern correct en .NET DI pour enregistrer plusieurs implémentations d'une interface :

```csharp
// Program.cs — enregistrement
builder.Services.AddSingleton<ICommandHandler, RunCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, StatusCommandHandler>();

// TelegramBotService — injection
public TelegramBotService(
    IOptions<TelegramOptions> options,
    AuthorizationFilter authFilter,
    IEnumerable<ICommandHandler> handlers,
    ILogger<TelegramBotService> logger)
{
    _options = options.Value;
    _authFilter = authFilter;
    _handlers = handlers;
    _logger = logger;
}
```

**Extraction de la commande dans `HandleUpdateAsync` :**

```csharp
var text = update.Message?.Text ?? string.Empty;
var command = text.Split(' ')[0].ToLowerInvariant(); // ex: "/run" ou "/status"

var handler = _handlers.FirstOrDefault(h => h.CanHandle(command));
if (handler is not null)
{
    await handler.HandleAsync(bot, update.Message!, ct);
}
else
{
    await bot.SendMessage(update.Message!.Chat.Id,
        "Commande inconnue. Commandes disponibles : /run, /status",
        cancellationToken: ct);
}
```

### Scope DI dans RunCommandHandler

`PostingCycleService` est enregistré **Scoped** (un scope par cycle d'exécution). `RunCommandHandler` est Singleton → il ne peut pas injecter `IPostingCycleService` directement (Captive Dependency anti-pattern).

**Solution correcte : `IServiceScopeFactory`**

```csharp
// src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs
using Bet2InvestPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public class RunCommandHandler : ICommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunCommandHandler> _logger;

    public RunCommandHandler(IServiceScopeFactory scopeFactory, ILogger<RunCommandHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool CanHandle(string command) => command == "/run";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /run reçue — déclenchement cycle");
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cycleService = scope.ServiceProvider.GetRequiredService<IPostingCycleService>();
            await cycleService.RunCycleAsync(ct);

            await bot.SendMessage(chatId, "✅ Cycle exécuté avec succès.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("Step", "Notify"))
            {
                _logger.LogError(ex, "Erreur lors de l'exécution du cycle via /run");
            }

            await bot.SendMessage(chatId, $"❌ Échec — {ex.Message}", cancellationToken: ct);
        }
    }
}
```

**IMPORTANT** : `CreateAsyncScope()` (avec `await using`) est préféré à `CreateScope()` pour les scénarios async (.NET 6+).

### ExecutionStateService — État en Mémoire

`ExecutionStateService` est un Singleton thread-safe qui stocke l'état de la dernière exécution. Il est conçu pour être mis à jour par `PostingCycleService` (Story 4.3 pour les notifications) et `SchedulerWorker` (Epic 5 pour `NextRunAt`).

```csharp
// src/Bet2InvestPoster/Services/IExecutionStateService.cs
namespace Bet2InvestPoster.Services;

public interface IExecutionStateService
{
    ExecutionState GetState();
    void RecordSuccess(int publishedCount);
    void RecordFailure(string reason);
    void SetNextRun(DateTimeOffset nextRunAt);
}

public record ExecutionState(
    DateTimeOffset? LastRunAt,
    bool? LastRunSuccess,
    string? LastRunResult,
    DateTimeOffset? NextRunAt
);
```

```csharp
// src/Bet2InvestPoster/Services/ExecutionStateService.cs
namespace Bet2InvestPoster.Services;

public class ExecutionStateService : IExecutionStateService
{
    private DateTimeOffset? _lastRunAt;
    private bool? _lastRunSuccess;
    private string? _lastRunResult;
    private DateTimeOffset? _nextRunAt;

    public ExecutionState GetState() =>
        new(_lastRunAt, _lastRunSuccess, _lastRunResult, _nextRunAt);

    public void RecordSuccess(int publishedCount)
    {
        _lastRunAt = DateTimeOffset.UtcNow;
        _lastRunSuccess = true;
        _lastRunResult = $"{publishedCount} pronostic(s) publiés";
    }

    public void RecordFailure(string reason)
    {
        _lastRunAt = DateTimeOffset.UtcNow;
        _lastRunSuccess = false;
        _lastRunResult = reason;
    }

    public void SetNextRun(DateTimeOffset nextRunAt) => _nextRunAt = nextRunAt;
}
```

**Thread safety** : `ExecutionStateService` utilise des assignations simples de primitives/records. En .NET, les assignations de références sont atomiques sur les plateformes 64-bit. Pour ce use-case mono-writer (un seul cycle à la fois), c'est suffisant. Si la concurrence devient un enjeu, utiliser `Interlocked` ou `lock`.

### MessageFormatter

```csharp
// src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs
using Bet2InvestPoster.Services;

namespace Bet2InvestPoster.Telegram.Formatters;

public class MessageFormatter
{
    public string FormatStatus(ExecutionState state)
    {
        var lastRun = state.LastRunAt.HasValue
            ? state.LastRunAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            : "Aucune";

        string result;
        if (!state.LastRunSuccess.HasValue)
            result = "—";
        else if (state.LastRunSuccess.Value)
            result = $"✅ Succès — {state.LastRunResult}";
        else
            result = $"❌ Échec — {state.LastRunResult}";

        var nextRun = state.NextRunAt.HasValue
            ? state.NextRunAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            : "Non planifié";

        return $"""
            📊 État du système
            • Dernière exécution : {lastRun}
            • Résultat : {result}
            • Prochain run : {nextRun}
            """;
    }
}
```

### ICommandHandler Interface

```csharp
// src/Bet2InvestPoster/Telegram/Commands/ICommandHandler.cs
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bet2InvestPoster.Telegram.Commands;

public interface ICommandHandler
{
    bool CanHandle(string command);
    Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct);
}
```

### Telegram.Bot 22.9.0 — Envoi de Messages

**Méthode correcte pour envoyer des messages en 22.x :**

```csharp
// Extension method disponible dans Telegram.Bot 22.x
await bot.SendMessage(
    chatId: message.Chat.Id,
    text: "votre message",
    cancellationToken: ct);
```

**ATTENTION** : Ne pas utiliser `bot.SendTextMessageAsync()` — déprécié en 22.x. Utiliser `bot.SendMessage()` (extension method).

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement handlers | `Telegram/Commands/RunCommandHandler.cs`, `StatusCommandHandler.cs` | [Architecture: Structure Patterns] |
| Emplacement formatter | `Telegram/Formatters/MessageFormatter.cs` | [Architecture: Structure Patterns] |
| Emplacement state service | `Services/IExecutionStateService.cs`, `ExecutionStateService.cs` | [Architecture: Structure Patterns] |
| Lifetime handlers | Singleton (stateless) | [Architecture: DI Pattern] |
| Lifetime ExecutionStateService | Singleton (état partagé) | [Architecture: DI Pattern] |
| Lifetime MessageFormatter | Singleton (stateless) | [Architecture: DI Pattern] |
| Step logging | `Notify` pour tout le module Telegram | [Architecture: Serilog Template] |
| Scope pour Scoped services | `IServiceScopeFactory.CreateAsyncScope()` | [Architecture: DI Pattern — Singleton ne peut pas injecter Scoped] |
| Bot messages sortants | via `bot.SendMessage()` dans les handlers | [Architecture: Telegram Boundary — Story 4.3 ajoutera NotificationService pour notifications automatiques] |

### Boundaries à NE PAS Violer

- `TelegramBotService` reste le **seul point de contact** avec l'API Telegram pour le **polling**
- Les `CommandHandlers` envoient des réponses directes aux commandes (one-shot) — c'est différent des notifications automatiques (`NotificationService`, Story 4.3)
- `AuthorizationFilter` reste le **premier gate** dans `HandleUpdateAsync` — aucune commande ne bypass ce filtre
- `PostingCycleService` doit être créé via un nouveau scope Scoped (`IServiceScopeFactory`) — ne jamais l'injecter directement dans un Singleton
- Le `BotToken` ne doit **jamais** apparaître dans les messages d'erreur envoyés à l'utilisateur ou dans les logs

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
├── Services/
│   ├── IExecutionStateService.cs         ← NOUVEAU
│   └── ExecutionStateService.cs          ← NOUVEAU
└── Telegram/
    ├── Commands/
    │   ├── ICommandHandler.cs            ← NOUVEAU
    │   ├── RunCommandHandler.cs          ← NOUVEAU
    │   └── StatusCommandHandler.cs       ← NOUVEAU
    └── Formatters/
        └── MessageFormatter.cs           ← NOUVEAU

tests/Bet2InvestPoster.Tests/
├── Services/
│   └── ExecutionStateServiceTests.cs     ← NOUVEAU
└── Telegram/
    ├── Commands/
    │   ├── RunCommandHandlerTests.cs      ← NOUVEAU
    │   └── StatusCommandHandlerTests.cs   ← NOUVEAU
    └── Formatters/
        └── MessageFormatterTests.cs       ← NOUVEAU
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
├── Telegram/
│   └── TelegramBotService.cs             ← MODIFIER (injection IEnumerable<ICommandHandler>, dispatch)
└── Program.cs                            ← MODIFIER (DI registrations)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/                 ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Telegram/
│   └── AuthorizationFilter.cs            ← NE PAS modifier
├── Services/                             ← NE PAS modifier (sauf ajout nouveaux fichiers)
├── Configuration/                        ← NE PAS modifier
├── Worker.cs                             ← NE PAS modifier
└── appsettings.json                      ← NE PAS modifier
```

### Exigences de Tests

**Framework :** xUnit (déjà configuré). Pas de Moq/NSubstitute — fakes minimaux en nested class ou implémentations fake directes.

**Pattern fake pour `ITelegramBotClient` :** Utiliser `NSubstitute` n'est pas dans le projet. Créer un fake minimal :

```csharp
// Fake ITelegramBotClient pour les tests
// Note : ITelegramBotClient est une interface — mocker avec une nested class
// OU utiliser un vrai TelegramBotClient avec un token de test qui ne sera pas appelé
// Méthode recommandée pour cette story : tester uniquement la logique de sélection/state,
// pas l'envoi Telegram (éviter de dépendre de l'API Telegram dans les tests unitaires)
```

**Alternative pragmatique** : Tester `CanHandle`, `IExecutionStateService`, et `MessageFormatter` en isolation. Pour `RunCommandHandler.HandleAsync` et `StatusCommandHandler.HandleAsync`, créer un fake `ITelegramBotClient` minimal qui capture les messages envoyés.

**Fake ITelegramBotClient minimal :**

```csharp
// Dans le fichier de test
private class FakeTelegramBotClient : ITelegramBotClient
{
    public List<string> SentMessages { get; } = [];

    public Task<Message> SendMessage(ChatId chatId, string text, /* ... */ CancellationToken cancellationToken = default)
    {
        SentMessages.Add(text);
        return Task.FromResult(new Message { Text = text });
    }

    // Implémentation minimale des autres membres d'interface (throw NotImplementedException)
    // ...
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 85 existants + ≥12 nouveaux = ≥97 tests, 0 échec
```

### Intelligence Story Précédente (Story 4.1)

**Learnings applicables à Story 4.2 :**

1. **`TelegramBotService` implémente `BackgroundService`** — la modification pour injecter `IEnumerable<ICommandHandler>` doit ajouter le paramètre sans casser le constructeur existant.

2. **`HandleUpdateAsync` signature exacte** :
   ```csharp
   private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
   ```
   Le commentaire `// Story 4.2 ajoutera le dispatch des commandes /run et /status` marque précisément où insérer le dispatch.

3. **`LogContext.PushProperty("Step", "Notify")` scope = méthode entière** — utiliser `using` wrapper pour tous les logs.

4. **Pas de Moq/NSubstitute** : 85 tests actuellement, 0 régression tolérée. Fakes en nested class.

5. **`TelegramOptions` disponible** : `BotToken` (string) + `AuthorizedChatId` (long) — déjà injecté dans `TelegramBotService`.

6. **`Telegram.Bot 22.9.0` breaking changes** : utiliser `bot.SendMessage()` (pas `SendTextMessageAsync()`). Voir Dev Notes story 4.1.

7. **`Telegram/Commands/` répertoire n'existe pas encore** — créer le dossier.

8. **`Telegram/Formatters/` répertoire n'existe pas encore** — créer le dossier.

9. **Pattern `_retryDelaySeconds` est `volatile` dans `TelegramBotService`** (correction code-review 4.1) — ne pas modifier ce champ.

10. **`ResetRetryDelay()` est appelé dans `HandleUpdateAsync` au début** — le conserver lors de la modification.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api` (nom historique, on reste dessus)

**Pattern de commit attendu :**
```
feat(telegram): RunCommandHandler StatusCommandHandler et MessageFormatter - story 4.2
```

**Commits récents :**
```
bc29d84 feat(telegram): TelegramBotService polling et AuthorizationFilter sécurité - story 4.1
8e04be6 docs(retro): rétrospective épique 3 — sélection publication historique terminée
a72a704 feat(publisher): BetPublisher et PostingCycleService publication et orchestration - story 3.3
```

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-4.2] — AC originaux, FR14, FR15
- [Source: .bmadOutput/planning-artifacts/architecture.md#Telegram-Boundary] — Command handlers dans Telegram/Commands/
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Dossier Telegram/, Services/
- [Source: .bmadOutput/planning-artifacts/architecture.md#DI-Pattern] — Singleton vs Scoped, IServiceScopeFactory
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — NFR5 credentials jamais dans logs
- [Source: .bmadOutput/implementation-artifacts/4-1-bot-telegram-setup-polling-et-securite.md] — Patterns TelegramBotService, AuthorizationFilter, tests, Telegram.Bot 22.9.0
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs] — HandleUpdateAsync commentaire Story 4.2
- [Source: src/Bet2InvestPoster/Services/IPostingCycleService.cs] — `Task RunCycleAsync(CancellationToken ct = default)`
- [Source: src/Bet2InvestPoster/Program.cs] — Pattern DI registration, placement avant `var host = builder.Build()`

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `FakeTelegramBotClient` : interface `ITelegramBotClient` 22.9.0 utilise `SendRequest<TResponse>` (pas `MakeRequestAsync`), `IExceptionParser` dans `Telegram.Bot.Exceptions`, `TGFile` pour `DownloadFile`, events `OnMakingApiRequest`/`OnApiResponseReceived`. Namespace conflict `Bet2InvestPoster.Tests.Telegram` vs `Telegram.Bot` → résolu avec `global::` prefix.
- `volatile` ne peut pas être utilisé sur des nullable value types (`bool?`, `DateTimeOffset?`). Champs simples suffisants pour ce use-case mono-writer.
- `RunCommandHandler` reçoit `IServiceScopeFactory` — ne pas passer `ServiceProvider` directement dans les tests (pas de conversion implicite).

### Completion Notes List

- AC#1 : `RunCommandHandler.CanHandle("/run")` → `true`. `HandleAsync` crée un scope Scoped via `IServiceScopeFactory.CreateAsyncScope()`, résout `IPostingCycleService`, appelle `RunCycleAsync(ct)`.
- AC#2 : Succès → `bot.SendMessage(chatId, "✅ Cycle exécuté avec succès.")`.
- AC#3 : Exception → `bot.SendMessage(chatId, $"❌ Échec — {ex.Message}")`. Logué avec Step `Notify`.
- AC#4 : `StatusCommandHandler.CanHandle("/status")` → `true`. `HandleAsync` récupère `ExecutionState` via `IExecutionStateService`, formate via `MessageFormatter.FormatStatus()`, envoie. `ExecutionStateService` Singleton thread-safe (champ simple, mono-writer). `MessageFormatter` produit format 3 lignes avec emoji et dates locales.
- AC#5 : `TelegramBotService.HandleUpdateAsync` extrait `command = text.Split(' ')[0].ToLowerInvariant()`, dispatch via `IEnumerable<ICommandHandler>`. Commande inconnue → réponse explicite.
- AC#6 : `ICommandHandler`, `RunCommandHandler`, `StatusCommandHandler`, `MessageFormatter`, `IExecutionStateService`, `ExecutionStateService` tous enregistrés en Singleton dans `Program.cs`.
- 103/103 tests passent : 85 existants (0 régression) + 18 nouveaux. `FakeTelegramBotClient` partagé dans `Bet2InvestPoster.Tests.Telegram.Commands`.

### File List

**Créés :**
- `src/Bet2InvestPoster/Telegram/Commands/ICommandHandler.cs`
- `src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs`
- `src/Bet2InvestPoster/Telegram/Commands/StatusCommandHandler.cs`
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs`
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs`
- `src/Bet2InvestPoster/Services/IExecutionStateService.cs`
- `src/Bet2InvestPoster/Services/ExecutionStateService.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/FakeTelegramBotClient.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/RunCommandHandlerTests.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/StatusCommandHandlerTests.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/ExecutionStateServiceTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (injection `IEnumerable<ICommandHandler>`, dispatch async)
- `src/Bet2InvestPoster/Program.cs` (ajout usings + DI registrations)
- `.bmadOutput/implementation-artifacts/4-2-commandes-run-et-status.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

**Non touchés :**
- `jtdev-bet2invest-scraper/` (submodule — interdit)
- `src/Bet2InvestPoster/Telegram/AuthorizationFilter.cs`
- `src/Bet2InvestPoster/Services/*.cs` (existants)
- `src/Bet2InvestPoster/Configuration/`
- `src/Bet2InvestPoster/Worker.cs`

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 4.2 — analyse exhaustive artifacts |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 11 fichiers créés, 2 modifiés, 103/103 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale — 8 issues (2H/4M/2L) trouvées et corrigées : H1+H2 IExecutionStateService branché dans RunCommandHandler, M1 thread safety lock, M2 UTC explicite, M3 sanitize error messages (ex.GetType().Name), M4 assertion NextRun date, L1 IMessageFormatter interface, L2 FakeTelegramBotClient robustesse. 1 fichier créé (IMessageFormatter.cs). 103/103 tests verts. |
