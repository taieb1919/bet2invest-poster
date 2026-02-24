# Story 3.2 : BetSelector — Sélection Aléatoire

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que le système sélectionne aléatoirement 5, 10 ou 15 pronostics parmi les candidats disponibles,
so that mes publications soient variées et en quantité adaptée.

## Acceptance Criteria

1. **Given** une liste de paris candidats (issus de `UpcomingBetsFetcher`) et un historique de doublons chargé via `IHistoryManager`
   **When** `BetSelector.SelectAsync()` est appelé
   **Then** les pronostics dont le `betId` figure dans `history.json` sont exclus de la sélection (FR8)

2. **Given** une liste de candidats filtrés (doublons exclus)
   **When** `BetSelector.SelectAsync()` effectue la sélection
   **Then** le nombre cible est aléatoirement 5, 10 ou 15 (FR7)
   **And** la sélection parmi les candidats disponibles est aléatoire

3. **Given** une liste de candidats disponibles inférieure au nombre cible
   **When** `BetSelector.SelectAsync()` effectue la sélection
   **Then** tous les candidats disponibles sont sélectionnés (sans erreur)

4. **Given** une sélection effectuée
   **When** l'opération se termine
   **Then** l'opération est loguée avec le Step `Select` : nombre candidats disponibles, nombre sélectionnés, cible

## Tasks / Subtasks

- [x] Task 1 : Créer l'interface `IBetSelector` (AC: #1, #2, #3, #4)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Services/IBetSelector.cs`
  - [x] 1.2 Méthode unique : `Task<List<SettledBet>> SelectAsync(List<SettledBet> candidates, CancellationToken ct = default)`
  - [x] 1.3 Doc XML sur la méthode (filtrage doublons via IHistoryManager, sélection 5/10/15)

- [x] Task 2 : Implémenter `BetSelector` (tous les ACs)
  - [x] 2.1 Créer `src/Bet2InvestPoster/Services/BetSelector.cs`
  - [x] 2.2 Injecter `IHistoryManager` et `ILogger<BetSelector>`
  - [x] 2.3 Appeler `_historyManager.LoadPublishedIdsAsync(ct)` pour obtenir les betIds déjà publiés
  - [x] 2.4 Filtrer : `candidates.Where(b => !publishedIds.Contains(b.Id)).ToList()`
  - [x] 2.5 Tirer aléatoirement le nombre cible parmi `[5, 10, 15]` via `Random.Shared`
  - [x] 2.6 Si `available.Count <= targetCount` → retourner toute la liste disponible
  - [x] 2.7 Sinon → mélanger aléatoirement et prendre les `targetCount` premiers (`.OrderBy(_ => Random.Shared.Next()).Take(targetCount).ToList()`)
  - [x] 2.8 Log avec `Step="Select"` : `"{Available} candidats disponibles, {Selected} sélectionnés (cible={Target})"`
  - [x] 2.9 Wrapper tout le corps de méthode dans `using (LogContext.PushProperty("Step", "Select"))`

- [x] Task 3 : Enregistrement DI (AC: #1)
  - [x] 3.1 Enregistrer `IBetSelector` / `BetSelector` en **Scoped** dans `Program.cs`
  - [x] 3.2 Placement : après l'enregistrement de `IHistoryManager`

- [x] Task 4 : Tests unitaires (tous les ACs)
  - [x] 4.1 Créer `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs`
  - [x] 4.2 Créer une classe `FakeHistoryManager : IHistoryManager` en nested private dans le fichier de test (voir pattern ci-dessous)
  - [x] 4.3 Test `SelectAsync_ExcludesAlreadyPublishedBets` : 5 candidats, 2 déjà dans history → les 2 exclus ne figurent pas dans le résultat
  - [x] 4.4 Test `SelectAsync_TargetIsOneOf_5_10_Or_15` : 20 candidats, 0 doublons → résultat de taille 5, 10 ou 15 (run 50 fois, vérifier que seules ces valeurs apparaissent)
  - [x] 4.5 Test `SelectAsync_WhenFewerCandidatesThanTarget_ReturnsAll` : 3 candidats, 0 doublons → les 3 candidats retournés
  - [x] 4.6 Test `SelectAsync_WithEmptyCandidates_ReturnsEmptyList` : 0 candidats → liste vide
  - [x] 4.7 Test `SelectAsync_AllCandidatesAlreadyPublished_ReturnsEmptyList` : 5 candidats, tous dans history → liste vide
  - [x] 4.8 Test `BetSelector_RegisteredAsScoped` : vérification DI Scoped via ServiceCollection
  - [x] 4.9 Vérifier 0 régression : **62 tests existants + 6 nouveaux = 68 tests, 0 échec** ✅

## Dev Notes

### Exigences Techniques Critiques

**Interface `IBetSelector` :**

```csharp
// src/Bet2InvestPoster/Services/IBetSelector.cs
using JTDev.Bet2InvestScraper.Models;

namespace Bet2InvestPoster.Services;

public interface IBetSelector
{
    /// <summary>
    /// Filters out already-published bets (via IHistoryManager) and randomly selects
    /// 5, 10, or 15 from the remaining candidates. Returns all available if fewer than target.
    /// Logs result with Step="Select".
    /// </summary>
    Task<List<SettledBet>> SelectAsync(List<SettledBet> candidates, CancellationToken ct = default);
}
```

**Implémentation `BetSelector` — Pattern Complet :**

```csharp
// src/Bet2InvestPoster/Services/BetSelector.cs
using JTDev.Bet2InvestScraper.Models;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Bet2InvestPoster.Services;

public class BetSelector : IBetSelector
{
    private static readonly int[] ValidCounts = [5, 10, 15];

    private readonly IHistoryManager _historyManager;
    private readonly ILogger<BetSelector> _logger;

    public BetSelector(IHistoryManager historyManager, ILogger<BetSelector> logger)
    {
        _historyManager = historyManager;
        _logger = logger;
    }

    public async Task<List<SettledBet>> SelectAsync(List<SettledBet> candidates, CancellationToken ct = default)
    {
        using (LogContext.PushProperty("Step", "Select"))
        {
            // AC#1 : exclure les betIds déjà publiés
            var publishedIds = await _historyManager.LoadPublishedIdsAsync(ct);
            var available = candidates.Where(b => !publishedIds.Contains(b.Id)).ToList();

            // AC#2 : cible aléatoire parmi 5, 10, 15
            var targetCount = ValidCounts[Random.Shared.Next(ValidCounts.Length)];

            // AC#3 : si moins de candidats que la cible, retourner tout ce qui est disponible
            List<SettledBet> selected;
            if (available.Count <= targetCount)
            {
                selected = available;
            }
            else
            {
                selected = available.OrderBy(_ => Random.Shared.Next()).Take(targetCount).ToList();
            }

            // AC#4 : log Step="Select"
            _logger.LogInformation(
                "{Available} candidats disponibles (après filtre doublons), {Selected} sélectionnés (cible={Target})",
                available.Count, selected.Count, targetCount);

            return selected;
        }
    }
}
```

**Points critiques :**
- `Random.Shared` est thread-safe (.NET 6+) — ne pas créer `new Random()` par instance
- `ValidCounts[Random.Shared.Next(ValidCounts.Length)]` : Next(3) → index 0, 1 ou 2 → valeurs 5, 10, 15
- `.OrderBy(_ => Random.Shared.Next())` : mélange aléatoire standard sans dépendance externe
- `LogContext.PushProperty("Step", "Select")` : scope `using` autour de toute la méthode (pattern identique à `UpcomingBetsFetcher.FetchAllAsync`)
- `SettledBet.Id` est `int` (namespace `JTDev.Bet2InvestScraper.Models`) — pas de conversion de type

**`LogContext.PushProperty` — Step Boundaries :**

| Opération | Step | Niveau |
|---|---|---|
| `SelectAsync` — résultat sélection | `Select` | `LogInformation` |

**Aucun nouveau package NuGet :** `Serilog.Context` est déjà disponible (Serilog 4.3.1 installé).

### Conformité Architecture

**Décisions architecturales à respecter impérativement :**

| Décision | Valeur | Source |
|---|---|---|
| Emplacement | `Services/BetSelector.cs` + `Services/IBetSelector.cs` | [Architecture: Structure Patterns] |
| DI Lifetime | `BetSelector` = **Scoped** (un scope par cycle d'exécution) | [Architecture: DI Pattern] |
| Interface par service | Obligatoire — `IBetSelector` | [Architecture: Structure] |
| Logging Step | `Select` pour toute l'opération | [Architecture: Serilog Template] |
| Dépendance history | Via `IHistoryManager.LoadPublishedIdsAsync()` uniquement — jamais d'accès direct à `history.json` | [Architecture: Data Boundary] |

**Boundaries à ne PAS violer :**
- `BetSelector` n'accède **jamais** directement à `history.json` — uniquement via `IHistoryManager`
- Le submodule `jtdev-bet2invest-scraper/` est **INTERDIT de modification**
- `BetSelector` ne publie rien — c'est le rôle de `BetPublisher` (story 3.3)

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
└── Services/
    ├── IBetSelector.cs              ← NOUVEAU (interface)
    └── BetSelector.cs               ← NOUVEAU (implémentation)

tests/Bet2InvestPoster.Tests/
└── Services/
    └── BetSelectorTests.cs          ← NOUVEAU (tests)
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
└── Program.cs                       ← MODIFIER (ajout DI registration Scoped IBetSelector)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/            ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Services/HistoryManager.cs       ← NE PAS modifier
├── Services/IHistoryManager.cs      ← NE PAS modifier
├── Services/TipsterService.cs       ← NE PAS modifier
├── Services/UpcomingBetsFetcher.cs  ← NE PAS modifier
├── Services/ExtendedBet2InvestClient.cs ← NE PAS modifier
├── Models/HistoryEntry.cs           ← NE PAS modifier
├── Configuration/                   ← NE PAS modifier
├── Exceptions/                      ← NE PAS modifier
└── appsettings.json                 ← NE PAS modifier
```

### Exigences de Tests

**Framework :** xUnit (déjà configuré). Pas de framework de mocking — utiliser un `FakeHistoryManager` minimal en nested class dans le fichier de test.

**Tests existants : 62 tests, 0 régression tolérée.**

**Pattern `FakeHistoryManager` (stub minimal, pas de Moq) :**

```csharp
// Nested class dans BetSelectorTests.cs
private sealed class FakeHistoryManager : IHistoryManager
{
    private readonly HashSet<int> _publishedIds;

    public FakeHistoryManager(IEnumerable<int>? publishedIds = null)
    {
        _publishedIds = publishedIds?.ToHashSet() ?? [];
    }

    public Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)
        => Task.FromResult(_publishedIds);

    public Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task PurgeOldEntriesAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
```

**Helper pour créer des `SettledBet` de test :**

```csharp
// SettledBet est dans JTDev.Bet2InvestScraper.Models — instanciation directe
private static SettledBet MakeBet(int id) => new SettledBet { Id = id };
```

**Attention :** vérifier les propriétés disponibles sur `SettledBet` dans le submodule avant d'instancier. Utiliser uniquement `Id` (int) qui est la propriété utilisée pour le filtrage.

**Tests requis — `BetSelectorTests.cs` :**

```csharp
public class BetSelectorTests
{
    private static BetSelector CreateSelector(IEnumerable<int>? publishedIds = null)
        => new(new FakeHistoryManager(publishedIds), NullLogger<BetSelector>.Instance);

    [Fact]
    public async Task SelectAsync_ExcludesAlreadyPublishedBets()
    {
        var selector = CreateSelector(publishedIds: [3, 5]);
        var candidates = Enumerable.Range(1, 7).Select(MakeBet).ToList(); // 7 candidats, ids 1-7

        var result = await selector.SelectAsync(candidates);

        // ids 3 et 5 doivent être absents
        Assert.DoesNotContain(result, b => b.Id == 3);
        Assert.DoesNotContain(result, b => b.Id == 5);
    }

    [Fact]
    public async Task SelectAsync_TargetIsOneOf_5_10_Or_15()
    {
        var selector = CreateSelector();
        var candidates = Enumerable.Range(1, 20).Select(MakeBet).ToList();
        var validCounts = new HashSet<int> { 5, 10, 15 };
        var observedCounts = new HashSet<int>();

        for (int i = 0; i < 50; i++)
        {
            var result = await selector.SelectAsync(candidates);
            observedCounts.Add(result.Count);
            Assert.Contains(result.Count, validCounts);
        }
        // Après 50 runs, on devrait voir au moins 2 valeurs différentes (probabilité > 99.99%)
        Assert.True(observedCounts.Count > 1, "La sélection doit varier aléatoirement");
    }

    [Fact]
    public async Task SelectAsync_WhenFewerCandidatesThanTarget_ReturnsAll()
    {
        var selector = CreateSelector();
        var candidates = Enumerable.Range(1, 3).Select(MakeBet).ToList(); // 3 < 5 (min target)

        var result = await selector.SelectAsync(candidates);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task SelectAsync_WithEmptyCandidates_ReturnsEmptyList()
    {
        var selector = CreateSelector();

        var result = await selector.SelectAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SelectAsync_AllCandidatesAlreadyPublished_ReturnsEmptyList()
    {
        var selector = CreateSelector(publishedIds: [1, 2, 3, 4, 5]);
        var candidates = Enumerable.Range(1, 5).Select(MakeBet).ToList();

        var result = await selector.SelectAsync(candidates);

        Assert.Empty(result);
    }

    [Fact]
    public void BetSelector_RegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IHistoryManager, HistoryManager>();
        services.Configure<PosterOptions>(o => o.DataPath = Path.GetTempPath());
        services.AddScoped<IBetSelector, BetSelector>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var selector = scope.ServiceProvider.GetRequiredService<IBetSelector>();
        Assert.NotNull(selector);
    }
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 62 existants + ≥6 nouveaux = ≥68 tests, 0 échec
```

### Intelligence Story Précédente (Story 3.1)

**Learnings critiques applicables à Story 3.2 :**

1. **`LogContext.PushProperty("Step", "...")` scope = méthode entière** : `using` wrapper autour de tout le corps de `SelectAsync` — même pattern que `UpcomingBetsFetcher.FetchAllAsync`.

2. **`CancellationToken ct = default` en dernier paramètre** : toutes les méthodes async l'ont.

3. **DI Scoped pattern validé** : `builder.Services.AddScoped<IBetSelector, BetSelector>()` dans `Program.cs` après `IHistoryManager`.

4. **`InternalsVisibleTo` déjà configuré** : accès aux membres `internal` sans configuration supplémentaire.

5. **62 tests actuellement** : après story 3.1 (51 initiaux + 8 nouveaux + 3 review). Vérifier 0 régression.

6. **Pas de Moq/NSubstitute** : le projet n'utilise pas de framework de mocking — créer un `FakeHistoryManager` minimal en nested class dans `BetSelectorTests.cs`.

7. **`SettledBet.Id` est `int`** : confirmé par la story 3.1. Le submodule expose `JTDev.Bet2InvestScraper.Models.SettledBet` avec `Id` (int).

8. **Pas de nouveau package NuGet** : `System.Linq`, `Serilog.Context`, `ILogger<T>` sont tous disponibles sans ajout.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api`

**Action attendue pour l'agent dev :** travailler sur la branche courante `epic-2/connexion-api` (les stories 3.x ont démarré sur cette branche — voir commit `978a3f6 feat(history): HistoryManager stockage doublons et purge - story 3.1`).

**Pattern de commit pour cette story :**
```
feat(selector): BetSelector sélection aléatoire 5/10/15 - story 3.2
```

**Commits récents (contexte codebase) :**
```
3917d06 docs(retro): rétrospective épique 2 — connexion API terminée
978a3f6 feat(history): HistoryManager stockage doublons et purge - story 3.1
39ad127 feat(scraper): UpcomingBetsFetcher récupération paris à venir - story 2.3
```

**Fichiers créés par story 3.1 (état actuel du codebase) :**
- `src/Bet2InvestPoster/Models/HistoryEntry.cs` — modèle historique
- `src/Bet2InvestPoster/Services/IHistoryManager.cs` — interface à injecter dans `BetSelector`
- `src/Bet2InvestPoster/Services/HistoryManager.cs` — implémentation
- `tests/Bet2InvestPoster.Tests/Services/HistoryManagerTests.cs` — pattern de test à consulter

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-3.2] — AC originaux, FR7, FR8
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Services/, interface-per-service, Scoped DI
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — IOptions<T>, logging steps, System.Text.Json
- [Source: .bmadOutput/planning-artifacts/architecture.md#Architectural-Boundaries] — BetSelector dépend IHistoryManager (Data Boundary)
- [Source: src/Bet2InvestPoster/Services/UpcomingBetsFetcher.cs] — Pattern Step="Scrape" logging, `LogContext.PushProperty` scope
- [Source: src/Bet2InvestPoster/Services/HistoryManager.cs] — `LoadPublishedIdsAsync()` retourne `HashSet<int>`
- [Source: src/Bet2InvestPoster/Services/IHistoryManager.cs] — Interface à injecter
- [Source: src/Bet2InvestPoster/Program.cs] — Pattern DI registration Scoped, placement après IHistoryManager
- [Source: tests/Bet2InvestPoster.Tests/Services/HistoryManagerTests.cs] — Pattern tests sans mocking
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs] — `SettledBet.Id` (int)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

_(aucun problème rencontré — implémentation directe conforme aux specs)_

### Completion Notes List

- AC#1 : `BetSelector.SelectAsync()` appelle `IHistoryManager.LoadPublishedIdsAsync()` et filtre via `!publishedIds.Contains(b.Id)`. Validé par tests 4.3 et 4.7.
- AC#2 : Cible aléatoire parmi `[5, 10, 15]` via `ValidCounts[Random.Shared.Next(3)]`. Aléatoire de la sélection via `.OrderBy(_ => Random.Shared.Next())`. Validé par test 4.4 (50 runs, variance observée).
- AC#3 : Si `available.Count <= targetCount` → retour direct de la liste disponible. Validé par tests 4.5 et 4.6.
- AC#4 : Log `Step="Select"` via `LogContext.PushProperty` wrappant tout le corps. Message structuré avec `{Available}`, `{Selected}`, `{Target}`.
- `Random.Shared` utilisé (thread-safe .NET 6+) — pas de `new Random()` par instance.
- `FakeHistoryManager` stub inline (nested class) — pas de framework de mocking.
- 68/68 tests passent : 62 existants (0 régression) + 6 nouveaux.
- 0 nouveau package NuGet ajouté.
- 3 fichiers créés, 1 modifié.

### File List

**Créés :**
- `src/Bet2InvestPoster/Services/IBetSelector.cs`
- `src/Bet2InvestPoster/Services/BetSelector.cs`
- `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Program.cs` (ajout DI registration Scoped IBetSelector)
- `.bmadOutput/implementation-artifacts/3-2-bet-selector-selection-aleatoire.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut 3-2 → review)

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 3.2 — analyse exhaustive artifacts + intelligence story 3.1 |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 3 fichiers créés, 1 modifié, 68/68 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale — 4 issues fixées (M1: completion notes, M2: test DI Scoped, L1: test exclusion, L2: test diversité éléments) |
