# Story 3.3 : BetPublisher et PostingCycleService — Publication et Orchestration

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que les pronostics sélectionnés soient publiés sur mon compte bet2invest,
so que ma présence sur la plateforme soit maintenue automatiquement.

## Acceptance Criteria

1. **Given** une liste de pronostics sélectionnés par `BetSelector`
   **When** `BetPublisher.PublishAllAsync()` est appelé
   **Then** chaque pronostic est publié via `IExtendedBet2InvestClient.PublishBetAsync()` (FR9)
   **And** un délai de 500ms est respecté entre chaque publication (NFR8 — déjà géré dans `ExtendedBet2InvestClient`, pas à ré-implémenter)

2. **Given** un pronostic publié avec succès
   **When** `BetPublisher` retourne
   **Then** chaque pronostic publié est enregistré dans `history.json` via `IHistoryManager.RecordAsync()` (FR10)
   **And** le `HistoryEntry` créé contient `BetId`, `PublishedAt = DateTime.UtcNow`, `MatchDescription` (home vs away depuis `SettledBet.Event`), `TipsterUrl = null`

3. **Given** un cycle d'exécution déclenché (automatique ou manual via /run)
   **When** `PostingCycleService.RunCycleAsync()` est appelé
   **Then** le cycle complet est orchestré dans l'ordre : purge historique → fetch bets → select → publish → record (FR9, FR10)
   **And** `HistoryManager.PurgeOldEntriesAsync()` est appelé **en premier** dans le cycle (purge les entrées > 30 jours avant toute sélection)

4. **Given** une erreur de publication sur un pronostic individuel
   **When** `ExtendedBet2InvestClient.PublishBetAsync()` lève une `PublishException`
   **Then** `BetPublisher` logue l'erreur avec Step `Publish` et re-lève la `PublishException` (pas de catch silencieux)

5. **Given** une exécution du cycle complète
   **When** `PostingCycleService.RunCycleAsync()` se termine
   **Then** chaque étape est loguée avec le Step correspondant (`Purge`, `Scrape`, `Select`, `Publish`)
   **And** le log final indique le nombre total de pronostics publiés avec succès

6. **Given** une liste vide retournée par `BetSelector.SelectAsync()`
   **When** `BetPublisher.PublishAllAsync()` est appelé avec liste vide
   **Then** aucune publication n'est effectuée, log informatif avec Step `Publish` indiquant 0 pronostics

## Tasks / Subtasks

- [x] Task 1 : Ajouter `BankrollId` dans `PosterOptions` (AC: #1)
  - [x] 1.1 Ajouter `public string BankrollId { get; set; } = "";` dans `src/Bet2InvestPoster/Configuration/PosterOptions.cs`
  - [x] 1.2 Ajouter la section dans `src/Bet2InvestPoster/appsettings.json` : `"Poster": { ..., "BankrollId": "" }`
  - [x] 1.3 Ajouter la validation fast-fail dans `Program.cs` : si `posterOpts.BankrollId` est vide → ajouter à `missingVars`

- [x] Task 2 : Créer l'interface `IBetPublisher` (AC: #1, #2, #4, #6)
  - [x] 2.1 Créer `src/Bet2InvestPoster/Services/IBetPublisher.cs`
  - [x] 2.2 Méthode unique : `Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default)` — retourne le nombre de paris publiés avec succès

- [x] Task 3 : Implémenter `BetPublisher` (AC: #1, #2, #4, #6)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Services/BetPublisher.cs`
  - [x] 3.2 Injecter `IExtendedBet2InvestClient`, `IHistoryManager`, `IOptions<PosterOptions>`, `ILogger<BetPublisher>`
  - [x] 3.3 Si `selected` est vide : loguer `"0 pronostics sélectionnés — aucune publication"` avec Step `Publish` et retourner 0
  - [x] 3.4 Pour chaque `SettledBet` dans `selected` :
    - Construire un `BetOrderRequest` mappé depuis `SettledBet` (voir mapping détaillé dans Dev Notes)
    - Appeler `await _client.PublishBetAsync(request, ct)` — le délai 500ms est géré par `ExtendedBet2InvestClient`
    - Si succès : appeler `await _historyManager.RecordAsync(new HistoryEntry { BetId = bet.Id, PublishedAt = DateTime.UtcNow, MatchDescription = ... }, ct)`
    - Si `PublishException` : loguer l'erreur et re-lever
  - [x] 3.5 Wrapper toute la méthode dans `using (LogContext.PushProperty("Step", "Publish"))`
  - [x] 3.6 Log final : `"{Published}/{Total} pronostics publiés avec succès"`

- [x] Task 4 : Créer l'interface `IPostingCycleService` (AC: #3, #5)
  - [x] 4.1 Créer `src/Bet2InvestPoster/Services/IPostingCycleService.cs`
  - [x] 4.2 Méthode unique : `Task RunCycleAsync(CancellationToken ct = default)`

- [x] Task 5 : Implémenter `PostingCycleService` (AC: #3, #5)
  - [x] 5.1 Créer `src/Bet2InvestPoster/Services/PostingCycleService.cs`
  - [x] 5.2 Injecter `IHistoryManager`, `ITipsterService`, `IUpcomingBetsFetcher`, `IBetSelector`, `IBetPublisher`, `ILogger<PostingCycleService>`
  - [x] 5.3 Ordre d'exécution dans `RunCycleAsync` :
    1. `await _historyManager.PurgeOldEntriesAsync(ct)` (Step `Purge`)
    2. `var tipsters = await _tipsterService.LoadTipstersAsync(ct)` (Step `Scrape`)
    3. `var candidates = await _upcomingBetsFetcher.FetchAllAsync(tipsters, ct)` (Step `Scrape`)
    4. `var selected = await _betSelector.SelectAsync(candidates, ct)` (Step `Select`)
    5. `var published = await _betPublisher.PublishAllAsync(selected, ct)` (Step `Publish`)
    6. Log cycle terminé avec nombre de pronostics publiés

- [x] Task 6 : Enregistrement DI (AC: #3)
  - [x] 6.1 Enregistrer `IBetPublisher` / `BetPublisher` en **Scoped** dans `Program.cs`
  - [x] 6.2 Enregistrer `IPostingCycleService` / `PostingCycleService` en **Scoped** dans `Program.cs`
  - [x] 6.3 Placement : après `IBetSelector` (ordre logique du cycle)
  - [x] 6.4 Ajouter validation `BankrollId` dans le bloc fast-fail existant

- [x] Task 7 : Connecter `PostingCycleService` au `Worker` existant (AC: #3)
  - [x] 7.1 Lire `src/Bet2InvestPoster/Worker.cs` (BackgroundService existant)
  - [x] 7.2 Créer un scope DI dans `Worker.ExecuteAsync` et résoudre `IPostingCycleService`
  - [x] 7.3 Appeler `await cycleService.RunCycleAsync(stoppingToken)` (le scheduling sera ajouté en Epic 5 — pour l'instant, exécution unique au démarrage pour valider le cycle complet)

- [x] Task 8 : Tests unitaires (AC: #1 à #6)
  - [x] 8.1 Créer `tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs`
  - [x] 8.2 Créer `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs`
  - [x] 8.3 Tests `BetPublisher` :
    - `PublishAllAsync_WithEmptyList_ReturnsZeroAndNoPublish` ✅
    - `PublishAllAsync_PublishesEachBetAndRecordsInHistory` ✅
    - `PublishAllAsync_RecordsMatchDescription_FromEvent` ✅
    - `PublishAllAsync_RecordsMatchDescription_WhenNoEvent` ✅
    - `PublishAllAsync_WhenPublishFails_RethrowsPublishException` ✅
    - `PublishAllAsync_UsesBankrollIdFromOptions` ✅
    - `BetPublisher_RegisteredAsScoped` ✅
  - [x] 8.4 Tests `PostingCycleService` :
    - `RunCycleAsync_CallsPurgeFirst` ✅
    - `RunCycleAsync_CallsAllPipelineStages` ✅
    - `RunCycleAsync_PassesSelectedBetsToPublisher` ✅
    - `PostingCycleService_RegisteredAsScoped` ✅
  - [x] 8.5 0 régression : **79 tests, 0 échec** (68 existants + 11 nouveaux) ✅

## Dev Notes

### Mapping `SettledBet` → `BetOrderRequest`

**Propriétés disponibles sur `SettledBet`** (namespace `JTDev.Bet2InvestScraper.Models`) :

```
Id (int), Type (string), Units (decimal), Price (decimal), Analysis (string?),
IsLive (bool), Team (string?), Side (string?), Handicap (decimal?), PeriodNumber (int),
Sport (BetSport? → Sport.Id: int, Sport.Name: string, Sport.Slug: string),
Event (BetEvent? → Event.Slug: string?, Event.Home: string, Event.Away: string, Event.Starts: DateTime),
League (BetLeague? → League.Name: string, League.Slug: string?, League.Country: string?)
```

**Mapping `SettledBet` → `BetOrderRequest` :**

```csharp
new BetOrderRequest
{
    BankrollId = _options.BankrollId,         // depuis PosterOptions
    SportId    = bet.Sport?.Id ?? 0,           // BetSport.Id
    EventId    = bet.Event?.Slug,              // BetEvent.Slug (peut être null)
    Type       = bet.Type,                     // "MONEYLINE", "SPREAD", etc.
    Team       = bet.Team,                     // "TEAM1", "TEAM2", null
    Side       = bet.Side,                     // "OVER", "UNDER", null
    Handicap   = bet.Handicap,                 // decimal? ligne
    Price      = bet.Price,                    // decimal cote
    Units      = bet.Units,                    // decimal mise
    PeriodNumber = bet.PeriodNumber,           // int période
    Analysis   = bet.Analysis,                 // string? analyse tipster
    IsLive     = bet.IsLive                    // bool
}
```

**`MatchDescription` pour `HistoryEntry` :**
```csharp
var description = bet.Event != null
    ? $"{bet.Event.Home} vs {bet.Event.Away}"
    : $"Bet#{bet.Id}";
```

### Interfaces Requises

**`IBetPublisher` :**

```csharp
// src/Bet2InvestPoster/Services/IBetPublisher.cs
using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Services;

public interface IBetPublisher
{
    /// <summary>
    /// Publishes each selected bet via IExtendedBet2InvestClient and records it in history.
    /// Returns the count of successfully published bets.
    /// Logs with Step="Publish".
    /// </summary>
    Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default);
}
```

**`IPostingCycleService` :**

```csharp
// src/Bet2InvestPoster/Services/IPostingCycleService.cs
namespace Bet2InvestPoster.Services;

public interface IPostingCycleService
{
    /// <summary>
    /// Runs the full posting cycle: purge → fetch → select → publish → record.
    /// </summary>
    Task RunCycleAsync(CancellationToken ct = default);
}
```

### Implémentation `BetPublisher` — Pattern Complet

```csharp
// src/Bet2InvestPoster/Services/BetPublisher.cs
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class BetPublisher : IBetPublisher
{
    private readonly IExtendedBet2InvestClient _client;
    private readonly IHistoryManager _historyManager;
    private readonly PosterOptions _options;
    private readonly ILogger<BetPublisher> _logger;

    public BetPublisher(
        IExtendedBet2InvestClient client,
        IHistoryManager historyManager,
        IOptions<PosterOptions> options,
        ILogger<BetPublisher> logger)
    {
        _client = client;
        _historyManager = historyManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Publish"))
        {
            if (selected.Count == 0)
            {
                _logger.LogInformation("0 pronostics sélectionnés — aucune publication");
                return 0;
            }

            int published = 0;
            foreach (var bet in selected)
            {
                var request = new BetOrderRequest
                {
                    BankrollId   = _options.BankrollId,
                    SportId      = bet.Sport?.Id ?? 0,
                    EventId      = bet.Event?.Slug,
                    Type         = bet.Type,
                    Team         = bet.Team,
                    Side         = bet.Side,
                    Handicap     = bet.Handicap,
                    Price        = bet.Price,
                    Units        = bet.Units,
                    PeriodNumber = bet.PeriodNumber,
                    Analysis     = bet.Analysis,
                    IsLive       = bet.IsLive
                };

                // PublishBetAsync gère le délai 500ms (NFR8) — ne pas rajouter Task.Delay ici
                await _client.PublishBetAsync(request, ct);

                var description = bet.Event != null
                    ? $"{bet.Event.Home} vs {bet.Event.Away}"
                    : $"Bet#{bet.Id}";

                await _historyManager.RecordAsync(new HistoryEntry
                {
                    BetId            = bet.Id,
                    PublishedAt      = DateTime.UtcNow,
                    MatchDescription = description,
                    TipsterUrl       = null
                }, ct);

                published++;
            }

            _logger.LogInformation(
                "{Published}/{Total} pronostics publiés avec succès",
                published, selected.Count);

            return published;
        }
    }
}
```

### Implémentation `PostingCycleService` — Pattern Complet

```csharp
// src/Bet2InvestPoster/Services/PostingCycleService.cs
using Microsoft.Extensions.Logging;

namespace Bet2InvestPoster.Services;

public class PostingCycleService : IPostingCycleService
{
    private readonly IHistoryManager _historyManager;
    private readonly ITipsterService _tipsterService;
    private readonly IUpcomingBetsFetcher _upcomingBetsFetcher;
    private readonly IBetSelector _betSelector;
    private readonly IBetPublisher _betPublisher;
    private readonly ILogger<PostingCycleService> _logger;

    public PostingCycleService(
        IHistoryManager historyManager,
        ITipsterService tipsterService,
        IUpcomingBetsFetcher upcomingBetsFetcher,
        IBetSelector betSelector,
        IBetPublisher betPublisher,
        ILogger<PostingCycleService> logger)
    {
        _historyManager      = historyManager;
        _tipsterService      = tipsterService;
        _upcomingBetsFetcher = upcomingBetsFetcher;
        _betSelector         = betSelector;
        _betPublisher        = betPublisher;
        _logger              = logger;
    }

    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Cycle de publication démarré");

        // 1. Purge des entrées > 30 jours (Step="Purge" géré dans HistoryManager)
        await _historyManager.PurgeOldEntriesAsync(ct);

        // 2. Lecture des tipsters (Step="Scrape" géré dans TipsterService)
        var tipsters = await _tipsterService.LoadTipstersAsync(ct);

        // 3. Récupération des paris à venir (Step="Scrape" géré dans UpcomingBetsFetcher)
        var candidates = await _upcomingBetsFetcher.FetchAllAsync(tipsters, ct);

        // 4. Sélection aléatoire (Step="Select" géré dans BetSelector)
        var selected = await _betSelector.SelectAsync(candidates, ct);

        // 5. Publication et enregistrement (Step="Publish" géré dans BetPublisher)
        var published = await _betPublisher.PublishAllAsync(selected, ct);

        _logger.LogInformation(
            "Cycle terminé — {Published} pronostics publiés sur {Candidates} candidats",
            published, candidates.Count);
    }
}
```

### Point Critique : `BankrollId` manquant dans `PosterOptions`

**`BankrollId` n'est PAS encore dans `PosterOptions`.** Cette propriété est requise par `BetOrderRequest`.

**Action requise (Task 1) :**

```csharp
// src/Bet2InvestPoster/Configuration/PosterOptions.cs — AJOUTER :
public string BankrollId { get; set; } = "";
```

```json
// src/Bet2InvestPoster/appsettings.json — AJOUTER dans section "Poster" :
"BankrollId": ""
```

```csharp
// src/Bet2InvestPoster/Program.cs — AJOUTER dans le bloc fast-fail :
var posterOpts = host.Services.GetRequiredService<IOptions<PosterOptions>>().Value;
if (string.IsNullOrWhiteSpace(posterOpts.BankrollId)) missingVars.Add("Poster__BankrollId");
```

**Override env var :** `Poster__BankrollId=votre-bankroll-id`

### Conformité Architecture

| Décision | Valeur | Source |
|---|---|---|
| Emplacement `BetPublisher` | `Services/BetPublisher.cs` + `Services/IBetPublisher.cs` | [Architecture: Structure Patterns] |
| Emplacement `PostingCycleService` | `Services/PostingCycleService.cs` + `Services/IPostingCycleService.cs` | [Architecture: Structure Patterns] |
| DI Lifetime | `BetPublisher` et `PostingCycleService` = **Scoped** | [Architecture: DI Pattern] |
| Interface par service | Obligatoire — `IBetPublisher`, `IPostingCycleService` | [Architecture: Structure] |
| Logging Step | `Publish` pour `BetPublisher` | [Architecture: Serilog Template] |
| Délai 500ms | Géré par `ExtendedBet2InvestClient.PublishBetAsync` — NE PAS rajouter `Task.Delay` dans `BetPublisher` | [Architecture: API Boundary] |
| Orchestration | `PostingCycleService` orchestre — la logique métier est dans les Services | [Architecture: Orchestration Boundary] |
| Accès history | Via `IHistoryManager` uniquement — jamais d'accès direct à `history.json` | [Architecture: Data Boundary] |

### Boundaries à NE PAS Violer

- `BetPublisher` n'accède **jamais** directement à `history.json` — uniquement via `IHistoryManager`
- `PostingCycleService` ne contient **aucune logique métier** — uniquement l'orchestration des services
- Le submodule `jtdev-bet2invest-scraper/` est **INTERDIT de modification**
- `BetPublisher` utilise `IExtendedBet2InvestClient` — jamais d'appel HTTP direct
- Le délai 500ms est géré par `ExtendedBet2InvestClient` — `BetPublisher` ne doit PAS appeler `Task.Delay`

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
└── Services/
    ├── IBetPublisher.cs              ← NOUVEAU (interface)
    ├── BetPublisher.cs               ← NOUVEAU (implémentation)
    ├── IPostingCycleService.cs       ← NOUVEAU (interface)
    └── PostingCycleService.cs        ← NOUVEAU (implémentation)

tests/Bet2InvestPoster.Tests/
└── Services/
    ├── BetPublisherTests.cs          ← NOUVEAU (tests)
    └── PostingCycleServiceTests.cs   ← NOUVEAU (tests)
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
├── Configuration/PosterOptions.cs   ← MODIFIER (ajout BankrollId)
├── Program.cs                       ← MODIFIER (DI registration + fast-fail BankrollId)
├── appsettings.json                 ← MODIFIER (ajout BankrollId dans section Poster)
└── Worker.cs                        ← MODIFIER (connecter à PostingCycleService)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/            ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Services/HistoryManager.cs       ← NE PAS modifier
├── Services/IHistoryManager.cs      ← NE PAS modifier
├── Services/BetSelector.cs          ← NE PAS modifier
├── Services/IBetSelector.cs         ← NE PAS modifier
├── Services/TipsterService.cs       ← NE PAS modifier
├── Services/UpcomingBetsFetcher.cs  ← NE PAS modifier
├── Services/ExtendedBet2InvestClient.cs ← NE PAS modifier
├── Models/HistoryEntry.cs           ← NE PAS modifier
├── Models/BetOrderRequest.cs        ← NE PAS modifier
├── Exceptions/PublishException.cs   ← NE PAS modifier
└── Exceptions/Bet2InvestApiException.cs ← NE PAS modifier
```

### Exigences de Tests

**Framework :** xUnit (déjà configuré). Pas de framework de mocking — utiliser des fakes minimaux en nested class.

**Tests existants : 68 tests, 0 régression tolérée.**

**Pattern `FakeBetPublisher` (pour `PostingCycleServiceTests`) :**

```csharp
private sealed class FakeBetPublisher : IBetPublisher
{
    public int CallCount { get; private set; }
    public List<SettledBet> LastSelected { get; private set; } = [];

    public Task<int> PublishAllAsync(List<SettledBet> selected, CancellationToken ct = default)
    {
        CallCount++;
        LastSelected = selected;
        return Task.FromResult(selected.Count);
    }
}
```

**Pattern `FakeExtendedClient` (pour `BetPublisherTests`) :**

```csharp
private sealed class FakeExtendedClient : IExtendedBet2InvestClient
{
    public bool IsAuthenticated => true;
    public bool ShouldFail { get; set; }
    public int PublishCallCount { get; private set; }

    public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<(bool CanSeeBets, List<SettledBet> Bets)> GetUpcomingBetsAsync(int tipsterId, CancellationToken ct = default)
        => Task.FromResult((true, new List<SettledBet>()));

    public Task<string?> PublishBetAsync(BetOrderRequest bet, CancellationToken ct = default)
    {
        PublishCallCount++;
        if (ShouldFail) throw new PublishException(bet.SportId, 500, "Simulated failure");
        return Task.FromResult<string?>("order-id-123");
    }
}
```

**Pattern `FakeHistoryManager` (réutilisable, même pattern que story 3.2) :**

```csharp
private sealed class FakeHistoryManager : IHistoryManager
{
    public List<HistoryEntry> Recorded { get; } = [];
    public int PurgeCallCount { get; private set; }

    public Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)
        => Task.FromResult(new HashSet<int>());

    public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        Recorded.Add(entry);
        return Task.CompletedTask;
    }

    public Task PurgeOldEntriesAsync(CancellationToken ct = default)
    {
        PurgeCallCount++;
        return Task.CompletedTask;
    }
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 68 existants + ≥6 nouveaux = ≥74 tests, 0 échec
```

### Intelligence Story Précédente (Story 3.2)

**Learnings critiques applicables à Story 3.3 :**

1. **`LogContext.PushProperty("Step", "...")` scope = méthode entière** : `using` wrapper autour de tout le corps de `PublishAllAsync` — même pattern que `BetSelector.SelectAsync` et `UpcomingBetsFetcher.FetchAllAsync`.

2. **`CancellationToken ct = default` en dernier paramètre** : toutes les méthodes async l'ont.

3. **DI Scoped pattern validé** : `builder.Services.AddScoped<IBetPublisher, BetPublisher>()` après `IBetSelector` dans `Program.cs`.

4. **Pas de Moq/NSubstitute** : créer des fakes minimaux en nested class dans chaque fichier de test.

5. **`SettledBet.Id` est `int`** — confirmé story 3.2. `HistoryEntry.BetId` est aussi `int`. Pas de conversion.

6. **`Random.Shared` thread-safe** — non utilisé ici mais pattern établi.

7. **68 tests actuellement** : après story 3.2. Vérifier 0 régression avant de committer.

8. **Pattern DI test** : voir `BetSelectorTests.BetSelector_RegisteredAsScoped` pour le pattern de test DI Scoped.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api`

**Pattern de commit attendu :**
```
feat(publisher): BetPublisher et PostingCycleService publication et orchestration - story 3.3
```

**Commits récents :**
```
5f78316 feat(selector): BetSelector sélection aléatoire 5/10/15 - story 3.2
3917d06 docs(retro): rétrospective épique 2 — connexion API terminée
978a3f6 feat(history): HistoryManager stockage doublons et purge - story 3.1
```

**Fichiers créés par story 3.2 (état actuel du codebase) :**
- `src/Bet2InvestPoster/Services/IBetSelector.cs`
- `src/Bet2InvestPoster/Services/BetSelector.cs`
- `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs`

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-3.3] — AC originaux, FR9, FR10
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Services/, interface-per-service, Scoped DI
- [Source: .bmadOutput/planning-artifacts/architecture.md#Orchestration-Boundary] — PostingCycleService rôle pur orchestration
- [Source: .bmadOutput/planning-artifacts/architecture.md#API-Boundary] — Rate limiting géré dans ExtendedBet2InvestClient
- [Source: .bmadOutput/planning-artifacts/architecture.md#Data-Boundary] — Accès history via IHistoryManager uniquement
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — Steps logging, IOptions<T>, System.Text.Json
- [Source: src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs] — `PublishBetAsync` signature et `PublishException`, délai 500ms intégré
- [Source: src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs] — Interface à injecter dans `BetPublisher`
- [Source: src/Bet2InvestPoster/Services/IHistoryManager.cs] — `RecordAsync(HistoryEntry, ct)` et `PurgeOldEntriesAsync(ct)`
- [Source: src/Bet2InvestPoster/Models/HistoryEntry.cs] — Modèle à instancier (BetId:int, PublishedAt, MatchDescription, TipsterUrl)
- [Source: src/Bet2InvestPoster/Models/BetOrderRequest.cs] — Champs requis (BankrollId, SportId, EventId, Type, Team, Side, Handicap, Price, Units, PeriodNumber, Analysis, IsLive)
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs] — À modifier : ajouter `BankrollId`
- [Source: src/Bet2InvestPoster/Program.cs] — Pattern DI registration Scoped, fast-fail block
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs] — `SettledBet` propriétés complètes (Sport.Id, Event.Slug, Event.Home, Event.Away, etc.)
- [Source: src/Bet2InvestPoster/Exceptions/PublishException.cs] — `PublishException(int betId, int httpStatusCode, string message)`
- [Source: .bmadOutput/implementation-artifacts/3-2-bet-selector-selection-aleatoire.md] — Patterns tests fake, DI Scoped, LogContext

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- Correction nom de méthode : `ITipsterService.GetTipstersAsync` → `LoadTipstersAsync` (nom réel dans l'interface)
- Correction `UnitTest1.cs` : `Worker` constructeur mis à jour pour accepter `IServiceProvider`
- Ajout `using Microsoft.Extensions.Logging` dans les deux fichiers de test (requis pour `ILogger<>` dans tests DI Scoped)

### Completion Notes List

- AC#1 : `BetPublisher.PublishAllAsync()` appelle `IExtendedBet2InvestClient.PublishBetAsync()` pour chaque bet. Le délai 500ms est délégué à `ExtendedBet2InvestClient` — pas de `Task.Delay` dans `BetPublisher`. Validé par test `PublishAllAsync_PublishesEachBetAndRecordsInHistory`.
- AC#2 : `HistoryManager.RecordAsync()` appelé après chaque publication réussie. `HistoryEntry` inclut `BetId`, `PublishedAt = DateTime.UtcNow`, `MatchDescription = "Home vs Away"` (ou `"Bet#id"` si Event null). Validés par tests `RecordsMatchDescription_FromEvent` et `RecordsMatchDescription_WhenNoEvent`.
- AC#3 : `PostingCycleService.RunCycleAsync()` orchestre purge → tipsters → candidates → select → publish dans l'ordre exact. Validé par `RunCycleAsync_CallsAllPipelineStages` et `RunCycleAsync_CallsPurgeFirst`.
- AC#4 : `PublishException` propagée sans catch silencieux. Validé par `PublishAllAsync_WhenPublishFails_RethrowsPublishException`.
- AC#5 : Steps loggés par chaque service (délégation). Log final cycle terminé dans `PostingCycleService`. `BetPublisher` log avec `Step="Publish"` via `LogContext.PushProperty`.
- AC#6 : Liste vide → retour 0, aucun appel API. Validé par `PublishAllAsync_WithEmptyList_ReturnsZeroAndNoPublish`.
- `BankrollId` ajouté à `PosterOptions` + `appsettings.json` + fast-fail validation dans `Program.cs`.
- `Worker.cs` reécrit pour résoudre `IPostingCycleService` depuis un scope DI et appeler `RunCycleAsync`.
- `UnitTest1.cs` adapté au nouveau constructeur `Worker(ILogger, IServiceProvider)`.
- 79/79 tests passent : 68 existants (0 régression) + 11 nouveaux.
- 4 fichiers créés, 5 modifiés.

### File List

**Créés :**
- `src/Bet2InvestPoster/Services/IBetPublisher.cs`
- `src/Bet2InvestPoster/Services/BetPublisher.cs`
- `src/Bet2InvestPoster/Services/IPostingCycleService.cs`
- `src/Bet2InvestPoster/Services/PostingCycleService.cs`
- `tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Configuration/PosterOptions.cs` (ajout `BankrollId`)
- `src/Bet2InvestPoster/appsettings.json` (ajout `BankrollId` dans section Poster)
- `src/Bet2InvestPoster/Program.cs` (DI registration BetPublisher + PostingCycleService + fast-fail BankrollId)
- `src/Bet2InvestPoster/Worker.cs` (connexion à IPostingCycleService via scope DI)
- `tests/Bet2InvestPoster.Tests/UnitTest1.cs` (adaptation constructeur Worker)
- `.bmadOutput/implementation-artifacts/3-3-bet-publisher-et-posting-cycle-service.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 3.3 — analyse exhaustive artifacts |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 6 fichiers créés, 5 modifiés, 79/79 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale — 7 issues trouvées (2H, 3M, 2L), toutes fixées. H2: test BankrollId renforcé avec vérification du request. M1/M2: try/catch ajouté dans Worker. M3: test ordre d'appel Purge-first. L1: UnitTest1.cs renommé WorkerTests.cs. L2: Step="Cycle" ajouté à PostingCycleService. 79/79 tests verts. |
