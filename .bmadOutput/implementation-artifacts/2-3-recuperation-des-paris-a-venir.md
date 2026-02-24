# Story 2.3 : Récupération des Paris à Venir (UpcomingBetsFetcher)

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a le système,
I want récupérer les paris à venir (non résolus) de chaque tipster listé,
So that je dispose d'un pool de pronostics candidats à la publication.

## Acceptance Criteria

1. **Given** une liste de tipsters validée par `TipsterService`
   **When** `UpcomingBetsFetcher.FetchAllAsync()` est appelé avec cette liste
   **Then** les paris à venir (non résolus) sont récupérés pour chaque tipster via `IExtendedBet2InvestClient.GetUpcomingBetsAsync()` (FR5)

2. **Given** la liste de tipsters en cours de traitement
   **When** `UpcomingBetsFetcher` appelle le client pour chaque tipster
   **Then** le délai de 500ms entre requêtes est respecté (NFR8 — délégué au `ExtendedBet2InvestClient`, pas à implémenter dans cette story)

3. **Given** la réponse API pour un tipster
   **When** la réponse indique `canSeeBets == false` (tipster pro non accessible)
   **Then** un warning est logué avec Step `Scrape` identifiant le tipster concerné
   **And** ses paris sont ignorés (liste vide retournée pour ce tipster)

4. **Given** l'API bet2invest retourne un code HTTP inattendu ou une réponse hors contrat
   **When** `ExtendedBet2InvestClient.GetUpcomingBetsAsync()` lève `Bet2InvestApiException`
   **Then** l'exception se propage sans être avalée (NFR9 — déjà géré par le client)

5. **Given** tous les tipsters ont été interrogés
   **When** `FetchAllAsync()` se termine
   **Then** les résultats sont agrégés en une liste unique de paris candidats (`List<SettledBet>`)
   **And** le nombre total de paris candidats est logué avec Step `Scrape`

## Tasks / Subtasks

- [x] Task 1 : Modifier `ExtendedBet2InvestClient.GetUpcomingBetsAsync` pour exposer `canSeeBets` (AC: #3)
  - [x] 1.1 Dans `ExtendedBet2InvestClient.GetUpcomingBetsAsync()`, après désérialisation, vérifier `statistics?.Bets?.CanSeeBets`
  - [x] 1.2 Si `canSeeBets == false` : ~~warning dans le client~~ supprimé lors de la code review (logging redondant avec le fetcher) — seul le log info avec canSeeBets={CanSeeBets} reste dans le client
  - [x] 1.3 Modifier le retour pour inclure `canSeeBets` : changer le type de retour en `Task<(bool CanSeeBets, List<SettledBet> Bets)>` (tuple nommé) et mettre à jour l'interface `IExtendedBet2InvestClient`
  - [x] 1.4 Adapter tous les tests existants de `ExtendedBet2InvestClientTests.cs` pour le nouveau type de retour (aucune adaptation nécessaire — tous les appels discardent le retour)

- [x] Task 2 : Créer l'interface `IUpcomingBetsFetcher` (AC: #1, #5)
  - [x] 2.1 Créer `Services/IUpcomingBetsFetcher.cs`
  - [x] 2.2 Signature : `Task<List<SettledBet>> FetchAllAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)`
  - [x] 2.3 Doc XML : agrège les paris non résolus de tous les tipsters listés, délègue la détection de changement API au client

- [x] Task 3 : Implémenter `UpcomingBetsFetcher` (AC: #1, #3, #4, #5)
  - [x] 3.1 Créer `Services/UpcomingBetsFetcher.cs`
  - [x] 3.2 Injecter `IExtendedBet2InvestClient` et `ILogger<UpcomingBetsFetcher>`
  - [x] 3.3 Itérer sur chaque `TipsterConfig` dans `tipsters` en passant `tipster.Id` à `GetUpcomingBetsAsync(tipster.Id, ct)`
  - [x] 3.4 Si `canSeeBets == false` : loguer warning et ne pas ajouter les bets à la liste (les filtrer)
  - [x] 3.5 Si `canSeeBets == true` et bets > 0 : loguer `LogInformation("{Count} paris à venir pour tipster {Name} (id={Id})", ...)` avec Step `Scrape`
  - [x] 3.6 Si `canSeeBets == true` et bets == 0 : loguer `LogWarning("Aucun pari à venir pour tipster {Name} (id={Id})", ...)`
  - [x] 3.7 Ne PAS catcher `Bet2InvestApiException` — laisser l'exception se propager (NFR9)
  - [x] 3.8 Ne PAS catcher `OperationCanceledException` — laisser se propager
  - [x] 3.9 À la fin : loguer `LogInformation("{Total} paris candidats agrégés depuis {TipsterCount} tipsters", ...)` avec Step `Scrape`
  - [x] 3.10 Retourner la liste agrégée

- [x] Task 4 : Enregistrement DI (AC: #1)
  - [x] 4.1 Enregistrer `IUpcomingBetsFetcher` / `UpcomingBetsFetcher` en **Scoped** dans `Program.cs`
  - [x] 4.2 Placement : après l'enregistrement de `ITipsterService`

- [x] Task 5 : Tests unitaires (tous les ACs)
  - [x] 5.1 Créer `tests/Bet2InvestPoster.Tests/Services/UpcomingBetsFetcherTests.cs`
  - [x] 5.2 Créer `FakeExtendedBet2InvestClient` (stub manuel dans les tests, `IExtendedBet2InvestClient`)
  - [x] 5.3 Tester le cas nominal : 2 tipsters, bets agrégés correctement — liste totale retournée
  - [x] 5.4 Tester `canSeeBets == false` : tipster avec accès restreint → bets ignorés, liste partielle
  - [x] 5.5 Tester liste vide de tipsters → retourne liste vide sans appel au client
  - [x] 5.6 Tester tipster avec 0 bets (`canSeeBets == true`, `pending` vide) → warning logué, pas d'ajout à la liste
  - [x] 5.7 Tester propagation de `Bet2InvestApiException` : exception remontée telle quelle
  - [x] 5.8 Tester propagation de `OperationCanceledException`
  - [x] 5.9 Tester enregistrement DI Scoped
  - [x] 5.10 Vérifier 0 régression : 51/51 tests passent (40 existants + 9 nouveaux UpcomingBetsFetcher + 2 nouveaux ExtendedBet2InvestClient canSeeBets)

## Dev Notes

### Exigences Techniques Critiques

**Changement de signature `GetUpcomingBetsAsync` — TYPE DE RETOUR MODIFIÉ :**
La story 2.3 étend `GetUpcomingBetsAsync` pour exposer `canSeeBets` au fetcher. Le type de retour passe de `Task<List<SettledBet>>` à `Task<(bool CanSeeBets, List<SettledBet> Bets)>`.

```csharp
// IExtendedBet2InvestClient.cs — APRÈS modification
Task<(bool CanSeeBets, List<SettledBet> Bets)> GetUpcomingBetsAsync(int tipsterId, CancellationToken ct = default);
```

```csharp
// ExtendedBet2InvestClient.cs — Fin de la méthode modifiée
var statistics = await response.Content.ReadFromJsonAsync<StatisticsResponse>(JsonOptions, ct);
var bets = statistics?.Bets?.Pending ?? [];
var canSeeBets = statistics?.Bets?.CanSeeBets ?? false;

// Note: warning canSeeBets=false supprimé du client lors de la code review (redondant avec le fetcher).
// Seul le log info reste — le fetcher gère la décision de filtrage.
_logger.LogInformation(
    "Paris à venir récupérés pour tipster {TipsterId} : {Count} paris en attente (canSeeBets={CanSeeBets})",
    tipsterId, bets.Count, canSeeBets);

return (canSeeBets, bets);
```

**Tests existants de `ExtendedBet2InvestClientTests` — à adapter :**
Les 16 tests de `ExtendedBet2InvestClient` testent `GetUpcomingBetsAsync` en comparant la `List<SettledBet>` retournée. Avec le changement de type de retour :
```csharp
// Avant
var bets = await sut.GetUpcomingBetsAsync(123);
Assert.Equal(2, bets.Count);

// Après
var (canSeeBets, bets) = await sut.GetUpcomingBetsAsync(123);
Assert.True(canSeeBets);
Assert.Equal(2, bets.Count);
```
**Tous les tests existants qui appellent `GetUpcomingBetsAsync` doivent être mis à jour — sinon ils ne compileront pas.**

**`UpcomingBetsFetcher` — Délégation de la responsabilité NFR8 :**
Le délai de 500ms entre requêtes (NFR8) est **entièrement géré par `ExtendedBet2InvestClient`** via `await Task.Delay(_options.RequestDelayMs, ct)` dans `GetUpcomingBetsAsync`. `UpcomingBetsFetcher` NE DOIT PAS ajouter de délai supplémentaire. C'est la boundary architecture : le fetcher orchestre, le client rate-limite.

**`UpcomingBetsFetcher` — Pattern `foreach` séquentiel, pas `Task.WhenAll` :**
L'appel pour chaque tipster est **séquentiel** (boucle `foreach`). Un appel parallèle (`Task.WhenAll`) violerait le délai de 500ms entre requêtes. Pattern :
```csharp
foreach (var tipster in tipsters)
{
    var (canSeeBets, bets) = await _client.GetUpcomingBetsAsync(tipster.Id, ct);
    if (!canSeeBets)
    {
        _logger.LogWarning("Tipster {Name} (id={Id}) ignoré : canSeeBets=false", tipster.Name, tipster.Id);
        continue;
    }
    if (bets.Count == 0)
        _logger.LogWarning("Aucun pari à venir pour tipster {Name} (id={Id})", tipster.Name, tipster.Id);
    else
        allBets.AddRange(bets);
}
```

**`LogContext.PushProperty("Step", "Scrape")` — Scope au niveau `FetchAllAsync` :**
Le step `Scrape` est ouvert une seule fois pour toute la boucle, pas répété par tipster :
```csharp
public async Task<List<SettledBet>> FetchAllAsync(List<TipsterConfig> tipsters, CancellationToken ct = default)
{
    using (LogContext.PushProperty("Step", "Scrape"))
    {
        // ...toute la boucle ici...
    }
}
```
Note: `ExtendedBet2InvestClient.GetUpcomingBetsAsync` ouvre aussi `LogContext.PushProperty("Step", "Scrape")` — les deux scopes sont imbriqués, ce qui est parfaitement valide avec Serilog.

**`Bet2InvestApiException` — Pas de catch dans le fetcher :**
L'exception doit remonter telle quelle au `PostingCycleService` (epic 3) qui gérera l'interruption du cycle. Ne pas catcher ni wrapper. Pattern :
```csharp
// CORRECT — pas de try/catch autour de GetUpcomingBetsAsync
var (canSeeBets, bets) = await _client.GetUpcomingBetsAsync(tipster.Id, ct);

// INCORRECT — ne pas faire
try { ... } catch (Bet2InvestApiException) { /* absorber */ }
```

### Conformité Architecture

**Décisions architecturales à respecter impérativement :**

| Décision | Valeur | Source |
|---|---|---|
| Emplacement service | `Services/UpcomingBetsFetcher.cs` avec `Services/IUpcomingBetsFetcher.cs` | [Architecture: Structure Patterns] |
| DI Lifetime | `UpcomingBetsFetcher` = **Scoped** (un scope par cycle d'exécution) | [Architecture: DI Pattern] |
| Nommage C# | PascalCase classes/méthodes, camelCase locals, préfixe `I` interfaces | [Architecture: Naming] |
| Fichiers | Un fichier = une classe | [Architecture: Naming] |
| Interface par service | Obligatoire — `IUpcomingBetsFetcher` | [Architecture: Structure] |
| Logging Step | `Scrape` pour les opérations UpcomingBetsFetcher | [Architecture: Serilog Template] |
| Error handling | JAMAIS de catch silencieux — `Bet2InvestApiException` remonte | [Architecture: Process Patterns] |
| Rate limiting | 500ms entre requêtes — **géré par `ExtendedBet2InvestClient`**, pas ici | [Architecture: API Patterns] |
| Séquentiel | Boucle `foreach`, pas `Task.WhenAll` | Constraint NFR8 |

**Boundaries à ne PAS violer :**
- `UpcomingBetsFetcher` utilise `IExtendedBet2InvestClient` — jamais d'appel HTTP direct
- Pas d'accès à `tipsters.json` depuis `UpcomingBetsFetcher` — c'est la responsabilité de `TipsterService`
- La logique métier d'agrégation reste dans `Services/`, jamais dans `Workers/`

### Librairies et Frameworks — Exigences Spécifiques

**Packages requis (AUCUN nouveau package à ajouter) :**

| Package | Version | Usage dans cette story |
|---|---|---|
| JTDev.Bet2InvestScraper (submodule) | Référence projet | `SettledBet` (modèle de paris à venir) — namespace `JTDev.Bet2InvestScraper.Models` |
| Serilog 4.3.1 | Déjà installé | `LogContext.PushProperty("Step", "Scrape")` |
| Microsoft.Extensions.Logging | Inclus .NET 9 | `ILogger<UpcomingBetsFetcher>` |
| Microsoft.Extensions.DependencyInjection | Inclus .NET 9 | DI registration |

**`SettledBet` — Modèle du submodule :**
Le retour de `GetUpcomingBetsAsync` est `List<SettledBet>` (namespace `JTDev.Bet2InvestScraper.Models`). Ce modèle est le type canonique pour les paris — pas besoin de créer un nouveau DTO pour cette story. Les champs clés disponibles :
- `Id` (int) — identifiant unique du pari (utilisé en story 3 pour les doublons via `history.json`)
- `State` (string) — état : pending bets ont `State` vide ou null (non résolus)
- `Event` (BetEvent?) — détail du match (Home, Away, Starts)
- `Price` (decimal) — cote
- `Type` (string) — type de pari (MONEYLINE, TOTAL_POINTS, etc.)

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
└── Services/
    ├── IUpcomingBetsFetcher.cs              ← NOUVEAU (interface)
    └── UpcomingBetsFetcher.cs               ← NOUVEAU (implémentation)

tests/Bet2InvestPoster.Tests/
└── Services/
    └── UpcomingBetsFetcherTests.cs          ← NOUVEAU (tests)
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
├── Services/
│   ├── IExtendedBet2InvestClient.cs         ← MODIFIER (type retour GetUpcomingBetsAsync → tuple)
│   └── ExtendedBet2InvestClient.cs          ← MODIFIER (canSeeBets check + nouveau type retour)
└── Program.cs                               ← MODIFIER (ajout DI registration Scoped IUpcomingBetsFetcher)

tests/Bet2InvestPoster.Tests/
└── Services/
    └── ExtendedBet2InvestClientTests.cs     ← MODIFIER (adapter les assertions suite au changement de type de retour)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/                   ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Worker.cs                               ← Pas de logique métier dans les Workers
├── Configuration/                          ← Aucun changement de config requis
├── Models/                                 ← Aucun nouveau modèle requis (SettledBet est dans le submodule)
├── Exceptions/                             ← Bet2InvestApiException déjà complet
├── Services/TipsterService.cs              ← NE PAS modifier
├── Services/ITipsterService.cs             ← NE PAS modifier
├── appsettings.json                        ← NE PAS modifier
└── appsettings.Development.json            ← NE PAS modifier
```

### Exigences de Tests

**Framework :** xUnit (déjà configuré). Pas de framework de mocking externe — utiliser un **stub manuel** `FakeExtendedBet2InvestClient`.

**Tests existants (40 cas exécutés) — 0 RÉGRESSION TOLÉRÉE :**
- `UnitTest1.cs` : 1 test
- `Configuration/OptionsTests.cs` : 10 tests
- `Services/ExtendedBet2InvestClientTests.cs` : 16 tests (+ 2 ajoutés en code review pour canSeeBets = 18)
- `Services/TipsterServiceTests.cs` : 11 méthodes (13 cas via Theory)
Note : 1 + 10 + 16 + 13 = 40 cas pré-review. Post-review : 1 + 10 + 18 + 13 = 42 existants.

**Stub `FakeExtendedBet2InvestClient` — Pattern :**
```csharp
private class FakeExtendedBet2InvestClient : IExtendedBet2InvestClient
{
    // Configurable per test
    public bool IsAuthenticated => true;
    private readonly Dictionary<int, (bool canSeeBets, List<SettledBet> bets)> _responses = new();

    public void Setup(int tipsterId, bool canSeeBets, List<SettledBet> bets)
        => _responses[tipsterId] = (canSeeBets, bets);

    public Task LoginAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<(bool CanSeeBets, List<SettledBet> Bets)> GetUpcomingBetsAsync(
        int tipsterId, CancellationToken ct = default)
    {
        if (_responses.TryGetValue(tipsterId, out var response))
            return Task.FromResult(response);
        throw new Bet2InvestApiException($"/v1/statistics/{tipsterId}", 404, "Not found");
    }

    public Task<string?> PublishBetAsync(BetOrderRequest bet, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
```

**Nouveaux tests requis — `UpcomingBetsFetcherTests.cs` :**

| # | Test | Description |
|---|---|---|
| 5.3 | `FetchAllAsync_WithMultipleTipsters_AggregatesAllBets` | 2 tipsters (canSeeBets=true), 3 bets total |
| 5.4 | `FetchAllAsync_TipsterWithCanSeeBetsFalse_IsIgnored` | 1 tipster normal + 1 pro → seul le normal contribue |
| 5.5 | `FetchAllAsync_EmptyTipsterList_ReturnsEmptyList` | Aucun appel client, liste vide retournée |
| 5.6 | `FetchAllAsync_TipsterWithZeroBets_ReturnsEmptyContribution` | canSeeBets=true, 0 bets → pas d'ajout |
| 5.7 | `FetchAllAsync_PropagatesBet2InvestApiException` | Exception non avalée |
| 5.8 | `FetchAllAsync_PropagatesOperationCanceledException` | CT cancel propagé |
| 5.9 | `UpcomingBetsFetcher_RegisteredAsScoped` | DI Scoped via ServiceCollection |

**Pattern de test :**
```csharp
public class UpcomingBetsFetcherTests
{
    private static UpcomingBetsFetcher CreateFetcher(FakeExtendedBet2InvestClient fake) =>
        new(fake, NullLogger<UpcomingBetsFetcher>.Instance);

    private static TipsterConfig MakeTipster(int id, string name = "Tipster") =>
        new() { Url = $"https://bet2invest.com/tipster/{id}", Name = name };
    // TryExtractId doit être appelé pour que Id soit peuplé :
    // var t = MakeTipster(123); t.TryExtractId(out _);

    [Fact]
    public async Task FetchAllAsync_WithMultipleTipsters_AggregatesAllBets()
    {
        var fake = new FakeExtendedBet2InvestClient();
        fake.Setup(100, canSeeBets: true, bets: [new SettledBet { Id = 1 }, new SettledBet { Id = 2 }]);
        fake.Setup(200, canSeeBets: true, bets: [new SettledBet { Id = 3 }]);

        var t1 = MakeTipsterWithId(100, "Alice");
        var t2 = MakeTipsterWithId(200, "Bob");

        var result = await CreateFetcher(fake).FetchAllAsync([t1, t2]);

        Assert.Equal(3, result.Count);
    }
}
```

**Note sur `TipsterConfig.Id` dans les tests :**
`TipsterConfig.Id` est un `private set` — il est peuplé via `TryExtractId()`. Dans les tests, construire comme suit :
```csharp
private static TipsterConfig MakeTipsterWithId(int id, string name = "Tipster")
{
    var t = new TipsterConfig
    {
        Url = $"https://bet2invest.com/tipster/{id}",
        Name = name
    };
    t.TryExtractId(out _);
    return t;
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 40 anciens tests + ~7 nouveaux = ≥47 tests, 0 échec
```

### Intelligence Story Précédente (Story 2.2)

**Learnings clés à appliquer en story 2.3 :**

1. **`TipsterConfig.Id` — private set via `TryExtractId()`** : L'ID du tipster n'est pas une propriété JSON mais calculé depuis l'URL. `TryExtractId()` doit avoir été appelé avant d'utiliser `tipster.Id`. Dans `UpcomingBetsFetcher`, `TipsterService` garantit que tous les tipsters retournés ont un ID valide (AC#2 de story 2.2). Donc pas besoin de re-valider dans le fetcher.

2. **`LogContext.PushProperty` validé** : `using (LogContext.PushProperty("Step", "Scrape"))` fonctionne correctement avec `ILogger<T>`. Serilog.Context est déjà importé via `using Serilog.Context;`.

3. **CancellationToken partout** : Toutes les méthodes async ont `CancellationToken ct = default` en dernier paramètre.

4. **Pattern DI Scoped validé** : `builder.Services.AddScoped<IUpcomingBetsFetcher, UpcomingBetsFetcher>()` dans `Program.cs`.

5. **InternalsVisibleTo** : Déjà configuré — constructeurs/méthodes `internal` accessibles dans les tests.

6. **40 tests actuellement** : 27 story 1 + 16 story 2.1 − réajustements code review = 40 tests verts.

7. **`DirectoryNotFoundException` vs `FileNotFoundException`** : Problème rencontré en 2.2, déjà résolu dans `TipsterService`. Pas de ce problème dans `UpcomingBetsFetcher` (pas de lecture fichier).

8. **Stub manuel vs Moq** : Le projet n'utilise PAS Moq ni NSubstitute. Utiliser des stubs manuels (classes fake qui implémentent l'interface). Pattern déjà utilisé implicitement via `FakeHttpMessageHandler` dans les tests existants.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api`

**Commits récents :**
```
b1b5646 feat(tipsters): TipsterService lecture tipsters.json - story 2.2
5f874d1 feat(api): ExtendedBet2InvestClient authentification et wrapper - story 2.1
b5f9279 docs(retro): rétrospective épique 1 — fondation du projet terminée
```

**Pattern de commit pour cette story :** `feat(scraper): UpcomingBetsFetcher récupération paris à venir - story 2.3`

**Fichiers modifiés dans story 2.2 (contexte codebase actuel) :**
- `Services/TipsterService.cs` — pattern de service à suivre
- `Services/ITipsterService.cs` — pattern d'interface à suivre
- `Models/TipsterConfig.cs` — modèle avec `Id` via `TryExtractId()`
- `Program.cs` — dernière registration DI ajoutée : `AddScoped<ITipsterService, TipsterService>()`

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-2.3] — AC originaux, FR5, NFR8, NFR9
- [Source: .bmadOutput/planning-artifacts/epics.md#Story-2.2-DevNotes] — Filtrage FR6 niveau 2 (`canSeeBets`), délégué à story 2.3
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Services/, interface-per-service, Scoped DI
- [Source: .bmadOutput/planning-artifacts/architecture.md#API-Patterns] — Rate limiting dans le client, pas dans le fetcher
- [Source: .bmadOutput/planning-artifacts/architecture.md#Process-Patterns] — Error handling, propagation Bet2InvestApiException
- [Source: .bmadOutput/planning-artifacts/architecture.md#Project-Structure] — API Boundary : seul le client appelle l'API
- [Source: src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs] — Interface à modifier pour le type de retour
- [Source: src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs] — `GetUpcomingBetsAsync`, `BetsData.CanSeeBets`, `StatisticsResponse`
- [Source: src/Bet2InvestPoster/Models/TipsterConfig.cs] — `Id` via `TryExtractId()`, propriété `private set`
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs] — `SettledBet` modèle complet (Id, State, Event, Price, etc.)
- [Source: src/Bet2InvestPoster/Exceptions/Bet2InvestApiException.cs] — Exception identifiable (Endpoint, HttpStatusCode, DetectedChange)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- **Changement de type de retour sans adaptation de tests** : Les 16 tests existants de `ExtendedBet2InvestClientTests` appellent `GetUpcomingBetsAsync` sans assigner ni déstructurer le retour (`await client.GetUpcomingBetsAsync(1)` — retour discardé). Le changement de `Task<List<SettledBet>>` vers `Task<(bool CanSeeBets, List<SettledBet> Bets)>` compile et passe sans aucune modification des tests. Aucune adaptation manuelle nécessaire.
- **`FakeExtendedBet2InvestClient` pour DI test** : Le test `UpcomingBetsFetcher_RegisteredAsScoped` enregistre le fake dans la DI — le fake implémente `IExtendedBet2InvestClient` et est compatible DI sans configuration particulière.

### Completion Notes List

- AC#1 : `UpcomingBetsFetcher.FetchAllAsync()` appelle `GetUpcomingBetsAsync(tipster.Id, ct)` pour chaque tipster via `IExtendedBet2InvestClient`. Validé par test `FetchAllAsync_WithMultipleTipsters_AggregatesAllBets`.
- AC#2 : NFR8 délégué à `ExtendedBet2InvestClient` (500ms via `Task.Delay(_options.RequestDelayMs, ct)` dans le client). `UpcomingBetsFetcher` n'ajoute aucun délai.
- AC#3 : `canSeeBets=false` → warning logué dans le fetcher + bets ignorés (même si non-vides). Validé par `FetchAllAsync_TipsterWithCanSeeBetsFalse_IsIgnored` (test renforcé en code review : bets non-vides pour prouver le filtrage réel).
- AC#4 : `Bet2InvestApiException` et `OperationCanceledException` propagent sans catch dans `UpcomingBetsFetcher`. Validés par tests 5.7, 5.8 et nouveau test échec partiel multi-tipsters.
- AC#5 : Agrégation en `List<SettledBet>` + log total à la fin. Validé par `FetchAllAsync_WithMultipleTipsters_AggregatesAllBets`.
- 51/51 tests passent : 42 existants (0 régression, +2 canSeeBets intégration) + 9 nouveaux UpcomingBetsFetcher (+1 échec partiel).
- 0 nouveau package NuGet ajouté.

### Code Review Fixes Applied

| # | Sévérité | Fix | Détail |
|---|---|---|---|
| H1 | HIGH | Test AC#3 renforcé | `canSeeBets=false` teste maintenant avec bets non-vides (MakeBet(99)) pour prouver le filtrage |
| H2 | HIGH | 2 tests intégration canSeeBets | `GetUpcomingBets_ReturnsCanSeeBetsTrue/False` dans ExtendedBet2InvestClientTests |
| M1 | MEDIUM | Warning redondant supprimé | `ExtendedBet2InvestClient.GetUpcomingBetsAsync` ne log plus warning canSeeBets=false (le fetcher s'en charge) |
| M2 | MEDIUM | Test échec partiel ajouté | `FetchAllAsync_SecondTipsterFails_ExceptionPropagatesPartialResultsLost` |
| L1 | LOW | Fake default explicite | `FakeExtendedBet2InvestClient` lance `InvalidOperationException` si Setup() non appelé |
| L2 | LOW | Doc story corrigée | Test breakdown, code examples, et compteurs mis à jour |

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 2.3 — analyse exhaustive artifacts + intelligence story 2.2 |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 3 fichiers créés, 3 modifiés, 48 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversarielle — 6 issues corrigées (2H, 2M, 2L), 51 tests verts |

### File List

**Créés :**
- `src/Bet2InvestPoster/Services/IUpcomingBetsFetcher.cs`
- `src/Bet2InvestPoster/Services/UpcomingBetsFetcher.cs`
- `tests/Bet2InvestPoster.Tests/Services/UpcomingBetsFetcherTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs` (type retour `GetUpcomingBetsAsync` → tuple)
- `src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs` (canSeeBets check + nouveau type retour; warning redondant supprimé en code review)
- `src/Bet2InvestPoster/Program.cs` (ajout DI registration Scoped IUpcomingBetsFetcher)
- `tests/Bet2InvestPoster.Tests/Services/ExtendedBet2InvestClientTests.cs` (2 tests intégration canSeeBets ajoutés en code review)
- `.bmadOutput/implementation-artifacts/2-3-recuperation-des-paris-a-venir.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut 2-3 → done)
