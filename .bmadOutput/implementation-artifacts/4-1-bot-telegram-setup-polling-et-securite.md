# Story 4.1 : Bot Telegram — Setup, Polling et Sécurité

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que seul mon chat Telegram puisse interagir avec le bot,
so que personne d'autre ne puisse contrôler ou lire les informations du service.

## Acceptance Criteria

1. **Given** un bot Telegram configuré avec `BotToken` et `AuthorizedChatId` dans `TelegramOptions`
   **When** `TelegramBotService` démarre via `IHostedService`
   **Then** le bot est connecté à l'API Telegram et écoute les messages en long polling

2. **Given** `TelegramBotService` actif
   **When** un message arrive d'un chat ID quelconque
   **Then** `AuthorizationFilter` vérifie le `ChatId` du message reçu
   **And** 100% des messages provenant de chat IDs **non autorisés** sont ignorés silencieusement (aucune réponse envoyée) (FR19, FR20, NFR7)
   **And** les messages provenant du chat ID autorisé sont traités normalement

3. **Given** `TelegramBotService` actif
   **When** une interruption temporaire de l'API Telegram se produit (timeout, erreur réseau)
   **Then** le bot retry la connexion polling avec backoff exponentiel (NFR10)
   **And** le service ne crash pas — il continue de tourner en arrière-plan

4. **Given** `TelegramBotService` démarre
   **When** la connexion est établie avec succès
   **Then** un log informatif est émis avec Step `Notify` indiquant que le bot est démarré

5. **Given** `TelegramBotService` rencontre une erreur de connexion récupérable
   **When** l'erreur est loguée
   **Then** les credentials (BotToken) ne doivent jamais apparaître dans les logs (NFR5)
   **And** le Step `Notify` est utilisé pour tous les logs de ce service

6. **Given** `TelegramBotService` est enregistré dans DI
   **When** le host démarre
   **Then** `TelegramBotService` est enregistré en tant que `IHostedService` (singleton lifecycle via `AddHostedService`)
   **And** `AuthorizationFilter` est enregistré en Singleton dans DI

## Tasks / Subtasks

- [x] Task 1 : Créer `AuthorizationFilter` (AC: #2, #5)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Telegram/AuthorizationFilter.cs`
  - [x] 1.2 Injecter `IOptions<TelegramOptions>`, `ILogger<AuthorizationFilter>`
  - [x] 1.3 Méthode `bool IsAuthorized(long chatId)` : compare `chatId` avec `TelegramOptions.AuthorizedChatId`
  - [x] 1.4 Si non autorisé : log Debug `"Message ignoré — chat ID {ChatId} non autorisé"` avec Step `Notify` (pas d'info sur l'identité)

- [x] Task 2 : Créer `TelegramBotService` (AC: #1, #3, #4, #5)
  - [x] 2.1 Créer `src/Bet2InvestPoster/Telegram/TelegramBotService.cs`
  - [x] 2.2 Implémenter `BackgroundService` (hérite de `BackgroundService` — c'est un `IHostedService`)
  - [x] 2.3 Injecter `IOptions<TelegramOptions>`, `AuthorizationFilter`, `ILogger<TelegramBotService>`
  - [x] 2.4 Dans `ExecuteAsync` : créer `TelegramBotClient` avec le `BotToken` de `TelegramOptions`
  - [x] 2.5 Lancer le polling via `bot.StartReceiving(...)` avec `ReceiverOptions` (offset, timeout)
  - [x] 2.6 Implémenter `HandleUpdateAsync` : appeler `AuthorizationFilter.IsAuthorized(chatId)` en premier, ignorer si non autorisé
  - [x] 2.7 Implémenter `HandleErrorAsync` : log avec Step `Notify`, retry backoff via `Task.Delay` exponentiel (1s, 2s, 4s, 8s, max 60s)
  - [x] 2.8 Log de démarrage : `"Bot Telegram démarré — polling actif"` avec Step `Notify`
  - [x] 2.9 Arrêt propre : géré via `CancellationToken` dans `Task.Delay(Timeout.Infinite, stoppingToken)`
  - [x] 2.10 Ne jamais loguer le `BotToken` — masqué avec `[REDACTED]` dans `HandleErrorAsync`

- [x] Task 3 : Enregistrement DI (AC: #6)
  - [x] 3.1 Enregistrer `AuthorizationFilter` en **Singleton** dans `Program.cs`
  - [x] 3.2 Enregistrer `TelegramBotService` via `builder.Services.AddHostedService<TelegramBotService>()`
  - [x] 3.3 Placement : après les services existants (`IPostingCycleService`), avant `var host = builder.Build()`

- [x] Task 4 : Tests unitaires (AC: #1 à #6)
  - [x] 4.1 Créer `tests/Bet2InvestPoster.Tests/Telegram/AuthorizationFilterTests.cs`
  - [x] 4.2 Tests `AuthorizationFilter` :
    - `IsAuthorized_WithMatchingChatId_ReturnsTrue` ✅
    - `IsAuthorized_WithDifferentChatId_ReturnsFalse` ✅
    - `IsAuthorized_WithZeroChatId_ReturnsFalse` ✅
    - `AuthorizationFilter_RegisteredAsSingleton` ✅
  - [x] 4.3 Build + test : `dotnet build Bet2InvestPoster.sln` + `dotnet test tests/Bet2InvestPoster.Tests`
  - [x] 4.4 Résultat : 79 existants + 4 nouveaux = **83 tests, 0 échec** ✅

## Dev Notes

### Telegram.Bot 22.9.0 — API Long Polling

**Package déjà installé** : `Telegram.Bot 22.9.0` (installé en Story 1.1).

**Pattern de polling recommandé avec Telegram.Bot 22.x :**

```csharp
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

// Dans TelegramBotService.ExecuteAsync :
var bot = new TelegramBotClient(options.BotToken);

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates = [UpdateType.Message],
    DropPendingUpdates = true  // Ignore messages reçus pendant le downtime
};

bot.StartReceiving(
    updateHandler: HandleUpdateAsync,
    errorHandler: HandleErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: stoppingToken
);

// Attendre l'arrêt
await Task.Delay(Timeout.Infinite, stoppingToken);
```

**IMPORTANT — Telegram.Bot 22.x breaking changes vs 21.x :**
- `StartReceiving` est non-bloquant → utiliser `await Task.Delay(Timeout.Infinite, ct)`
- `HandleUpdateAsync` signature : `async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)`
- `HandleErrorAsync` signature : `async Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)`
- `Update.Message?.Chat.Id` pour obtenir le chat ID
- `ReceiverOptions` dans namespace `Telegram.Bot.Polling`

**Backoff exponentiel pour HandleErrorAsync :**

```csharp
private int _retryDelaySeconds = 1;

private async Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)
{
    using (LogContext.PushProperty("Step", "Notify"))
    {
        // Masquer le token si présent dans le message d'erreur
        var safeMessage = ex.Message.Replace(_options.BotToken, "[REDACTED]");
        _logger.LogWarning("Erreur polling Telegram ({Source}): {Message} — retry dans {Delay}s",
            source, safeMessage, _retryDelaySeconds);
    }

    await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds), ct);
    _retryDelaySeconds = Math.Min(_retryDelaySeconds * 2, 60); // max 60s
}

// Remettre à 0 en cas de succès (dans HandleUpdateAsync quand un message est traité) :
private void ResetRetryDelay() => _retryDelaySeconds = 1;
```

### Interfaces et Structure

**`AuthorizationFilter` — implémentation complète :**

```csharp
// src/Bet2InvestPoster/Telegram/AuthorizationFilter.cs
using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Telegram;

public class AuthorizationFilter
{
    private readonly long _authorizedChatId;
    private readonly ILogger<AuthorizationFilter> _logger;

    public AuthorizationFilter(IOptions<TelegramOptions> options, ILogger<AuthorizationFilter> logger)
    {
        _authorizedChatId = options.Value.AuthorizedChatId;
        _logger = logger;
    }

    public bool IsAuthorized(long chatId)
    {
        if (chatId == _authorizedChatId)
            return true;

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogDebug("Message ignoré — chat ID {ChatId} non autorisé", chatId);
        }

        return false;
    }
}
```

**Pattern `TelegramBotService` :**

```csharp
// src/Bet2InvestPoster/Telegram/TelegramBotService.cs
using Bet2InvestPoster.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bet2InvestPoster.Telegram;

public class TelegramBotService : BackgroundService
{
    private readonly TelegramOptions _options;
    private readonly AuthorizationFilter _authFilter;
    private readonly ILogger<TelegramBotService> _logger;
    private int _retryDelaySeconds = 1;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        AuthorizationFilter authFilter,
        ILogger<TelegramBotService> logger)
    {
        _options = options.Value;
        _authFilter = authFilter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bot = new TelegramBotClient(_options.BotToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
            DropPendingUpdates = true
        };

        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Bot Telegram démarré — polling actif");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt propre — normal
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var chatId = update.Message?.Chat.Id ?? 0;
        if (chatId == 0 || !_authFilter.IsAuthorized(chatId))
            return;

        _retryDelaySeconds = 1; // Reset backoff on successful update

        using (LogContext.PushProperty("Step", "Notify"))
        {
            var text = update.Message?.Text ?? "(no text)";
            _logger.LogInformation("Message reçu — commande: {Command}", text);
        }

        // Story 4.2 ajoutera le dispatch des commandes /run et /status
    }

    private async Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        using (LogContext.PushProperty("Step", "Notify"))
        {
            var safeMessage = ex.Message.Replace(_options.BotToken, "[REDACTED]");
            _logger.LogWarning("Erreur polling Telegram ({Source}): {Message} — retry dans {Delay}s",
                source, safeMessage, _retryDelaySeconds);
        }

        if (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds), ct);
            _retryDelaySeconds = Math.Min(_retryDelaySeconds * 2, 60);
        }
    }
}
```

### Enregistrement DI dans Program.cs

```csharp
// Après IPostingCycleService, avant var host = builder.Build()

// AuthorizationFilter: Singleton — filtre chat ID autorisé
builder.Services.AddSingleton<AuthorizationFilter>();

// TelegramBotService: HostedService — bot polling en arrière-plan
builder.Services.AddHostedService<TelegramBotService>();
```

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement `TelegramBotService` | `Telegram/TelegramBotService.cs` | [Architecture: Structure Patterns] |
| Emplacement `AuthorizationFilter` | `Telegram/AuthorizationFilter.cs` | [Architecture: Structure Patterns] |
| Lifetime `TelegramBotService` | `AddHostedService<>` (Singleton géré par le host) | [Architecture: Telegram Boundary] |
| Lifetime `AuthorizationFilter` | Singleton | [Architecture: DI Pattern] |
| Step logging | `Notify` pour tout le module Telegram | [Architecture: Serilog Template] |
| Sécurité chat ID | `AuthorizationFilter` = gate d'entrée unique — 100% filtrage (NFR7) | [Architecture: Telegram Boundary] |
| Retry backoff | Exponentiel dans `HandleErrorAsync` (NFR10) | [Architecture: API & Communication Patterns] |
| Credentials dans logs | JAMAIS — masquer `BotToken` avec `[REDACTED]` (NFR5) | [Architecture: Enforcement Guidelines] |

### Boundaries à NE PAS Violer

- `TelegramBotService` est le **seul point de contact** avec l'API Telegram (long polling)
- `AuthorizationFilter` est le **seul gate** — tout message non autorisé est rejeté avant tout traitement
- Le `BotToken` ne doit **jamais** apparaître dans les logs (remplacer dans les messages d'exception)
- La logique métier (réponse aux commandes) sera dans `Commands/` (Stories 4.2) — `TelegramBotService` ne contient que le setup et le dispatch
- `NotificationService` sera le **seul service** autorisé à envoyer des messages sortants (Story 4.3)

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
└── Telegram/
    ├── AuthorizationFilter.cs         ← NOUVEAU
    └── TelegramBotService.cs          ← NOUVEAU

tests/Bet2InvestPoster.Tests/
└── Telegram/
    └── AuthorizationFilterTests.cs   ← NOUVEAU
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
└── Program.cs                        ← MODIFIER (DI registration)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/             ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Services/                         ← NE PAS modifier (tous les services existants)
├── Configuration/TelegramOptions.cs  ← NE PAS modifier (déjà correct : BotToken + AuthorizedChatId)
├── Configuration/PosterOptions.cs    ← NE PAS modifier
├── Worker.cs                         ← NE PAS modifier
└── appsettings.json                  ← NE PAS modifier
```

### Exigences de Tests

**Framework :** xUnit (déjà configuré). Pas de Moq/NSubstitute — fakes minimaux en nested class.

**Répertoire de test à créer :** `tests/Bet2InvestPoster.Tests/Telegram/` (nouveau)

**Tests existants :** 79 tests, 0 régression tolérée.

**Pattern pour `AuthorizationFilterTests` :**

```csharp
// tests/Bet2InvestPoster.Tests/Telegram/AuthorizationFilterTests.cs
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Telegram;

public class AuthorizationFilterTests
{
    private static AuthorizationFilter CreateFilter(long authorizedChatId)
    {
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            AuthorizedChatId = authorizedChatId
        });
        return new AuthorizationFilter(options, NullLogger<AuthorizationFilter>.Instance);
    }

    [Fact]
    public void IsAuthorized_WithMatchingChatId_ReturnsTrue()
    {
        var filter = CreateFilter(12345L);
        Assert.True(filter.IsAuthorized(12345L));
    }

    [Fact]
    public void IsAuthorized_WithDifferentChatId_ReturnsFalse()
    {
        var filter = CreateFilter(12345L);
        Assert.False(filter.IsAuthorized(99999L));
    }

    [Fact]
    public void IsAuthorized_WithZeroChatId_ReturnsFalse()
    {
        var filter = CreateFilter(12345L);
        Assert.False(filter.IsAuthorized(0L));
    }

    [Fact]
    public void AuthorizationFilter_RegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.Configure<TelegramOptions>(o =>
        {
            o.BotToken = "test";
            o.AuthorizedChatId = 1;
        });
        services.AddSingleton<AuthorizationFilter>();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var f1 = sp.GetRequiredService<AuthorizationFilter>();
        var f2 = sp.GetRequiredService<AuthorizationFilter>();

        Assert.Same(f1, f2);
    }
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 79 existants + ≥4 nouveaux = ≥83 tests, 0 échec
```

### Intelligence Story Précédente (Story 3.3)

**Learnings applicables à Story 4.1 :**

1. **`LogContext.PushProperty("Step", "Notify")` scope = méthode entière** : `using` wrapper — même pattern que tous les services précédents.

2. **`CancellationToken ct = default` en dernier paramètre** : cohérent avec tout le codebase.

3. **DI pattern validé** : `AddHostedService<TelegramBotService>()` pour les BackgroundService. `AddSingleton<AuthorizationFilter>()` sans interface (utilisé directement dans `TelegramBotService`).

4. **Pas de Moq/NSubstitute** : fakes minimaux en nested class. `NullLogger<T>.Instance` pour les tests qui n'ont pas besoin de vérifier les logs.

5. **`Worker.cs` actuel** : exécute le cycle une seule fois au démarrage. Ne pas y toucher — le scheduling quotidien viendra en Epic 5 avec `SchedulerWorker`.

6. **79 tests actuellement** : 0 régression tolérée.

7. **`TelegramOptions` déjà correctement configurée** : `BotToken` (string) + `AuthorizedChatId` (long). Fast-fail déjà en place dans `Program.cs` pour ces deux valeurs.

8. **`Telegram/` directory existe mais est vide** — c'est normal, cette story le remplit.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api` (nom historique, on reste dessus)

**Pattern de commit attendu :**
```
feat(telegram): TelegramBotService polling et AuthorizationFilter sécurité - story 4.1
```

**Commits récents :**
```
8e04be6 docs(retro): rétrospective épique 3 — sélection publication historique terminée
a72a704 feat(publisher): BetPublisher et PostingCycleService publication et orchestration - story 3.3
5f78316 feat(selector): BetSelector sélection aléatoire 5/10/15 - story 3.2
```

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-4.1] — AC originaux, FR19, FR20, NFR7, NFR10
- [Source: .bmadOutput/planning-artifacts/architecture.md#Telegram-Boundary] — TelegramBotService point de contact unique
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Telegram/ folder, Singleton pattern
- [Source: .bmadOutput/planning-artifacts/architecture.md#Process-Patterns] — Error handling, retry backoff
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — NFR5 credentials jamais dans logs
- [Source: src/Bet2InvestPoster/Configuration/TelegramOptions.cs] — BotToken (string), AuthorizedChatId (long)
- [Source: src/Bet2InvestPoster/Program.cs] — Pattern DI registration, fast-fail existant
- [Source: .bmadOutput/implementation-artifacts/3-3-bet-publisher-et-posting-cycle-service.md] — Patterns tests fake, LogContext, DI Singleton

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

(aucun — implémentation directe sans corrections requises)

### Completion Notes List

- AC#1 : `TelegramBotService` démarre via `BackgroundService.ExecuteAsync`, crée `TelegramBotClient` et lance `StartReceiving`. Bot actif en long polling.
- AC#2 : `AuthorizationFilter.IsAuthorized(chatId)` appelé en premier dans `HandleUpdateAsync`. Si non autorisé → `return Task.CompletedTask` immédiat (aucune réponse). NFR7 respecté.
- AC#3 : `HandleErrorAsync` implémente backoff exponentiel (1s → 2s → 4s → max 60s) via `Task.Delay`. Le service ne crash pas. NFR10 respecté.
- AC#4 : Log `"Bot Telegram démarré — polling actif"` avec `Step="Notify"` émis dans `ExecuteAsync`. NFR12 respecté.
- AC#5 : `BotToken` masqué via `.Replace(_options.BotToken, "[REDACTED]")` dans `HandleErrorAsync`. Jamais de token dans les logs. NFR5 respecté.
- AC#6 : `AuthorizationFilter` enregistré `AddSingleton<AuthorizationFilter>()`, `TelegramBotService` enregistré `AddHostedService<TelegramBotService>()` dans `Program.cs`.
- 85/85 tests passent : 79 existants (0 régression) + 6 nouveaux (`AuthorizationFilterTests` + DI test `TelegramBotService`).

### File List

**Créés :**
- `src/Bet2InvestPoster/Telegram/AuthorizationFilter.cs`
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/AuthorizationFilterTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Program.cs` (ajout using + DI AuthorizationFilter + TelegramBotService)
- `.bmadOutput/implementation-artifacts/4-1-bot-telegram-setup-polling-et-securite.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 4.1 — analyse exhaustive artifacts |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 3 fichiers créés, 1 modifié, 83/83 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale : 6 issues (3M+3L) corrigées — volatile backoff, null-safe token, reset connectivity, negative chatId test, DI hosted test, Dev Notes sync — 85/85 tests verts |
