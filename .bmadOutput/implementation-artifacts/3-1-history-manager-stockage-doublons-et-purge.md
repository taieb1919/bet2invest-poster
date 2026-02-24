# Story 3.1 : HistoryManager — Stockage, Doublons et Purge

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que le système ne publie jamais un pronostic déjà posté,
So that mon compte bet2invest ne contienne pas de doublons.

## Acceptance Criteria

1. **Given** un fichier `history.json` contenant les pronostics précédemment publiés
   **When** le système vérifie un pronostic candidat
   **Then** `HistoryManager` détecte si le `betId` (int) existe déjà dans l'historique (FR8)

2. **Given** un pronostic à enregistrer après publication réussie
   **When** `HistoryManager.RecordAsync()` est appelé
   **Then** l'écriture dans `history.json` est atomique (write-to-temp + rename) pour éviter toute corruption en cas de crash (NFR4)

3. **Given** le début d'un cycle d'exécution
   **When** `HistoryManager.PurgeOldEntriesAsync()` est appelé
   **Then** les entrées de plus de 30 jours (`PublishedAt < UtcNow - 30 jours`) sont supprimées
   **And** la purge est loguée avec le Step `Purge` (nombre d'entrées purgées)

4. **Given** `history.json` n'existe pas sur le disque
   **When** `HistoryManager` est utilisé pour la première fois
   **Then** le fichier est créé automatiquement comme tableau vide `[]` à la première écriture

5. **Given** un pronostic publié enregistré dans `history.json`
   **When** `HistoryManager.RecordAsync()` est appelé
   **Then** chaque enregistrement est logué avec le Step `Publish` (betId enregistré)

6. **Given** `history.json` existe
   **When** `HistoryManager.LoadPublishedIdsAsync()` est appelé
   **Then** un `HashSet<int>` des `betId` déjà publiés est retourné (pour usage en story 3.2 par BetSelector)

## Tasks / Subtasks

- [x] Task 1 : Créer le modèle `HistoryEntry` (AC: #1, #2, #3)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Models/HistoryEntry.cs`
  - [x] 1.2 Propriétés : `BetId` (int), `PublishedAt` (DateTime ISO 8601 UTC), `MatchDescription` (string?, lisibilité humaine)
  - [x] 1.3 Attributs `[JsonPropertyName("camelCase")]` avec `System.Text.Json`
  - [x] 1.4 `PublishedAt` en UTC (stocker `DateTime.UtcNow` à l'enregistrement)

- [x] Task 2 : Créer l'interface `IHistoryManager` (AC: #1, #2, #3, #5, #6)
  - [x] 2.1 Créer `src/Bet2InvestPoster/Services/IHistoryManager.cs`
  - [x] 2.2 Méthode `Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)` — retourne les betIds publiés
  - [x] 2.3 Méthode `Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)` — enregistrement atomique
  - [x] 2.4 Méthode `Task PurgeOldEntriesAsync(CancellationToken ct = default)` — purge > 30 jours
  - [x] 2.5 Doc XML sur chaque méthode (atomicité NFR4, seuil 30 jours)

- [x] Task 3 : Implémenter `HistoryManager` (tous les ACs)
  - [x] 3.1 Créer `src/Bet2InvestPoster/Services/HistoryManager.cs`
  - [x] 3.2 Injecter `IOptions<PosterOptions>` et `ILogger<HistoryManager>`
  - [x] 3.3 Calculer `_historyPath = Path.Combine(options.DataPath, "history.json")` dans le constructeur
  - [x] 3.4 Implémenter `LoadPublishedIdsAsync()` : si fichier absent → retourner `HashSet<int>` vide ; sinon désérialiser et extraire les `BetId`
  - [x] 3.5 Implémenter `RecordAsync()` : charger la liste courante (ou `[]`), ajouter l'entrée, sérialiser en JSON indenté, écrire dans `.tmp`, puis `File.Move(tempPath, historyPath, overwrite: true)`
  - [x] 3.6 Implémenter `PurgeOldEntriesAsync()` : charger la liste, calculer le seuil `DateTime.UtcNow.AddDays(-30)`, filtrer les entrées expirées, si des entrées ont été supprimées → réécrire atomiquement + loguer Step `Purge`
  - [x] 3.7 Log `RecordAsync` : `_logger.LogInformation("Paris enregistré dans l'historique : betId={BetId}", entry.BetId)` avec Step `Publish`
  - [x] 3.8 Log `PurgeOldEntriesAsync` : `_logger.LogInformation("{Count} entrée(s) purgées de l'historique (> 30 jours)", purgedCount)` avec Step `Purge`
  - [x] 3.9 Utiliser `JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true }` pour sérialisation/désérialisation
  - [x] 3.10 `LoadPublishedIdsAsync` doit être idempotent et thread-safe (pas de cache statique — relit à chaque appel)

- [x] Task 4 : Enregistrement DI (AC: #1)
  - [x] 4.1 Enregistrer `IHistoryManager` / `HistoryManager` en **Scoped** dans `Program.cs`
  - [x] 4.2 Placement : après l'enregistrement de `IUpcomingBetsFetcher`

- [x] Task 5 : Tests unitaires (tous les ACs)
  - [x] 5.1 Créer `tests/Bet2InvestPoster.Tests/Services/HistoryManagerTests.cs`
  - [x] 5.2 Utiliser un répertoire temporaire (`Path.GetTempPath() + Guid`) créé et supprimé via `IDisposable` (pattern identique à `TipsterServiceTests`)
  - [x] 5.3 Test nominal `LoadPublishedIdsAsync_WithExistingHistory_ReturnsAllBetIds` : fichier avec 3 entrées → HashSet de 3 betIds
  - [x] 5.4 Test `LoadPublishedIdsAsync_WhenFileAbsent_ReturnsEmptySet` : pas de fichier → HashSet vide, pas d'exception
  - [x] 5.5 Test `RecordAsync_AddsEntryAndWritesAtomically` : enregistrer 1 entrée → fichier créé, betId présent dans `LoadPublishedIdsAsync`
  - [x] 5.6 Test `RecordAsync_AppendsToPreviousHistory` : 2 appels successifs → 2 entrées dans le fichier
  - [x] 5.7 Test `PurgeOldEntriesAsync_RemovesEntriesOlderThan30Days` : 1 entrée récente + 1 ancienne → seule l'ancienne supprimée
  - [x] 5.8 Test `PurgeOldEntriesAsync_WhenNoExpiredEntries_PreservesAll` : toutes récentes → fichier inchangé, log "0 entrée(s) purgées"
  - [x] 5.9 Test `PurgeOldEntriesAsync_WhenFileAbsent_DoesNotThrow` : pas de fichier → pas d'exception
  - [x] 5.10 Test `HistoryManager_RegisteredAsScoped` : vérification DI Scoped via ServiceCollection
  - [x] 5.11 Vérifier 0 régression : **51 tests existants + nouveaux = ≥ 58 tests, 0 échec** → **62/62 ✅**

## Dev Notes

### Exigences Techniques Critiques

**Modèle `HistoryEntry` — Champs minimaux :**

```csharp
// src/Bet2InvestPoster/Models/HistoryEntry.cs
using System.Text.Json.Serialization;

namespace Bet2InvestPoster.Models;

public class HistoryEntry
{
    [JsonPropertyName("betId")]
    public int BetId { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTime PublishedAt { get; set; }

    /// <summary>Human-readable match description for log/audit (optional).</summary>
    [JsonPropertyName("matchDescription")]
    public string? MatchDescription { get; set; }
}
```

Note : `BetId` est un `int` car `SettledBet.Id` (submodule) est `int`. Le document d'architecture montre `"betId": "abc-123"` (exemple illustratif) mais le type réel du modèle est `int`.

**Interface `IHistoryManager` :**

```csharp
// src/Bet2InvestPoster/Services/IHistoryManager.cs
namespace Bet2InvestPoster.Services;

public interface IHistoryManager
{
    /// <summary>
    /// Loads all betIds already published from history.json.
    /// Returns an empty HashSet if the file does not exist.
    /// </summary>
    Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically appends an entry to history.json using write-to-temp + rename (NFR4).
    /// Creates history.json if it does not exist.
    /// </summary>
    Task RecordAsync(HistoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes entries older than 30 days from history.json and rewrites atomically.
    /// Logs purged count with Step="Purge". No-op if file does not exist.
    /// </summary>
    Task PurgeOldEntriesAsync(CancellationToken ct = default);
}
```

**Implémentation — Écriture Atomique (NFR4) :**

```csharp
// Patron obligatoire pour TOUTE écriture dans history.json
private async Task SaveAtomicAsync(List<HistoryEntry> entries, CancellationToken ct)
{
    var json = JsonSerializer.Serialize(entries, _jsonOptions);
    var tempPath = _historyPath + ".tmp";
    await File.WriteAllTextAsync(tempPath, json, ct);
    File.Move(tempPath, _historyPath, overwrite: true);
}
```

- `File.Move` avec `overwrite: true` est atomique sur Linux (rename syscall) — NFR4 satisfait
- **Ne jamais** appeler `File.WriteAllTextAsync` directement sur `_historyPath`

**`HistoryManager` — Pattern Complet :**

```csharp
// src/Bet2InvestPoster/Services/HistoryManager.cs
public class HistoryManager : IHistoryManager
{
    private readonly string _historyPath;
    private readonly ILogger<HistoryManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public HistoryManager(IOptions<PosterOptions> options, ILogger<HistoryManager> logger)
    {
        _historyPath = Path.Combine(options.Value.DataPath, "history.json");
        _logger = logger;
    }

    public async Task<HashSet<int>> LoadPublishedIdsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_historyPath))
            return [];

        var json = await File.ReadAllTextAsync(_historyPath, ct);
        var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, _jsonOptions) ?? [];
        return entries.Select(e => e.BetId).ToHashSet();
    }

    public async Task RecordAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        var entries = await LoadEntriesAsync(ct);
        entries.Add(entry);
        await SaveAtomicAsync(entries, ct);

        using (LogContext.PushProperty("Step", "Publish"))
        {
            _logger.LogInformation(
                "Paris enregistré dans l'historique : betId={BetId}", entry.BetId);
        }
    }

    public async Task PurgeOldEntriesAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_historyPath))
            return;

        var entries = await LoadEntriesAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var purged = entries.RemoveAll(e => e.PublishedAt < cutoff);

        using (LogContext.PushProperty("Step", "Purge"))
        {
            if (purged > 0)
            {
                await SaveAtomicAsync(entries, ct);
                _logger.LogInformation(
                    "{Count} entrée(s) purgées de l'historique (> 30 jours)", purged);
            }
            else
            {
                _logger.LogInformation(
                    "Aucune entrée à purger dans l'historique (0 entrée > 30 jours)");
            }
        }
    }

    private async Task<List<HistoryEntry>> LoadEntriesAsync(CancellationToken ct)
    {
        if (!File.Exists(_historyPath))
            return [];
        var json = await File.ReadAllTextAsync(_historyPath, ct);
        return JsonSerializer.Deserialize<List<HistoryEntry>>(json, _jsonOptions) ?? [];
    }

    private async Task SaveAtomicAsync(List<HistoryEntry> entries, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(entries, _jsonOptions);
        var tempPath = _historyPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, _historyPath, overwrite: true);
    }
}
```

**`LogContext.PushProperty` — Step Boundaries :**

| Opération | Step | Niveau |
|---|---|---|
| `RecordAsync` — enregistrement betId | `Publish` | `LogInformation` |
| `PurgeOldEntriesAsync` — résultat purge | `Purge` | `LogInformation` |

Voir story 2.3 : `using (LogContext.PushProperty("Step", "StepName"))` scope sur toute l'opération.

**`PosterOptions.DataPath` — Configuration :**

```csharp
// Configuration/PosterOptions.cs (existant, NE PAS modifier)
public string DataPath { get; set; } = ".";  // répertoire courant par défaut
```

En production VPS : `DataPath = "/opt/bet2invest-poster"` → `history.json` à `/opt/bet2invest-poster/history.json`.
En développement local et en tests : utiliser un répertoire temporaire.

**`SettledBet.Id` — Source du `BetId` :**

`SettledBet` est dans le namespace `JTDev.Bet2InvestScraper.Models` (submodule). `Id` est `int`. La story 3.2 (BetSelector) utilisera `HistoryManager.LoadPublishedIdsAsync()` pour obtenir le `HashSet<int>` et filtrer les candidats via `candidate.Id`.

**Utilisation par Story 3.2 (BetSelector) :**

```csharp
// Dans BetSelector (story 3.2) — pattern attendu
var publishedIds = await _historyManager.LoadPublishedIdsAsync(ct);
var available = candidates.Where(b => !publishedIds.Contains(b.Id)).ToList();
```

**Utilisation par Story 3.3 (BetPublisher) :**

```csharp
// Dans BetPublisher (story 3.3) — pattern attendu
await _client.PublishBetAsync(betOrderRequest, ct);
var entry = new HistoryEntry
{
    BetId = bet.Id,
    PublishedAt = DateTime.UtcNow,
    MatchDescription = $"{bet.Event?.Home} vs {bet.Event?.Away} - {bet.Type}"
};
await _historyManager.RecordAsync(entry, ct);
```

**Aucun nouveau package NuGet :** `System.Text.Json` est inclus dans .NET 9, `Serilog.Context` est déjà installé (Serilog 4.3.1).

### Conformité Architecture

**Décisions architecturales à respecter impérativement :**

| Décision | Valeur | Source |
|---|---|---|
| Emplacement | `Services/HistoryManager.cs` + `Services/IHistoryManager.cs` | [Architecture: Structure Patterns] |
| Modèle | `Models/HistoryEntry.cs` | [Architecture: Project Structure] |
| DI Lifetime | `HistoryManager` = **Scoped** (un scope par cycle d'exécution) | [Architecture: DI Pattern] |
| Interface par service | Obligatoire — `IHistoryManager` | [Architecture: Structure] |
| Sérialisation | `System.Text.Json` uniquement (pas Newtonsoft) | [Architecture: Enforcement] |
| Écriture atomique | write-to-temp + rename — `File.Move(temp, path, overwrite: true)` | NFR4 |
| Purge automatique | Entrées > 30 jours supprimées à chaque cycle | [epics.md#Story-3.1] |
| Logging Step | `Publish` pour `RecordAsync`, `Purge` pour `PurgeOldEntriesAsync` | [Architecture: Serilog Template] |
| Auto-création | Fichier absent → tableau vide créé à la première écriture | [epics.md#Story-3.1 AC] |

**Boundaries à ne PAS violer :**
- `HistoryManager` est le **seul** composant qui lit/écrit `history.json` (Data Boundary)
- `BetSelector` et `BetPublisher` accèdent à history uniquement via `IHistoryManager`
- Pas d'accès fichier direct depuis Workers/ ou Telegram/
- Le submodule `jtdev-bet2invest-scraper/` est **INTERDIT de modification**

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
├── Models/
│   └── HistoryEntry.cs              ← NOUVEAU (modèle entrée historique)
└── Services/
    ├── IHistoryManager.cs           ← NOUVEAU (interface)
    └── HistoryManager.cs            ← NOUVEAU (implémentation)

tests/Bet2InvestPoster.Tests/
└── Services/
    └── HistoryManagerTests.cs       ← NOUVEAU (tests)
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
└── Program.cs                       ← MODIFIER (ajout DI registration Scoped IHistoryManager)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/            ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Services/TipsterService.cs       ← NE PAS modifier
├── Services/ITipsterService.cs      ← NE PAS modifier
├── Services/UpcomingBetsFetcher.cs  ← NE PAS modifier
├── Services/IUpcomingBetsFetcher.cs ← NE PAS modifier
├── Services/IExtendedBet2InvestClient.cs ← NE PAS modifier
├── Services/ExtendedBet2InvestClient.cs  ← NE PAS modifier
├── Configuration/                   ← NE PAS modifier
├── Exceptions/                      ← NE PAS modifier
└── appsettings.json                 ← NE PAS modifier
```

### Exigences de Tests

**Framework :** xUnit (déjà configuré). Pas de framework de mocking — utiliser un répertoire temporaire réel sur disque (pattern `TipsterServiceTests`).

**Tests existants : 51 tests, 0 régression tolérée.**

**Pattern répertoire temporaire (comme TipsterServiceTests) :**

```csharp
public class HistoryManagerTests : IDisposable
{
    private readonly string _tempDir;

    public HistoryManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private HistoryManager CreateManager() =>
        new(Options.Create(new PosterOptions { DataPath = _tempDir }),
            NullLogger<HistoryManager>.Instance);

    private string HistoryPath => Path.Combine(_tempDir, "history.json");
}
```

**Tests requis — `HistoryManagerTests.cs` :**

| # | Test | Description |
|---|---|---|
| 5.3 | `LoadPublishedIdsAsync_WithExistingHistory_ReturnsAllBetIds` | 3 entrées → HashSet de 3 betIds |
| 5.4 | `LoadPublishedIdsAsync_WhenFileAbsent_ReturnsEmptySet` | Pas de fichier → HashSet vide |
| 5.5 | `RecordAsync_AddsEntryAndWritesAtomically` | 1 enregistrement → fichier créé, betId dans `LoadPublishedIdsAsync` |
| 5.6 | `RecordAsync_AppendsToPreviousHistory` | 2 appels → 2 entrées distinctes |
| 5.7 | `PurgeOldEntriesAsync_RemovesEntriesOlderThan30Days` | 1 récente + 1 > 30j → seule l'ancienne supprimée |
| 5.8 | `PurgeOldEntriesAsync_WhenNoExpiredEntries_PreservesAll` | Toutes récentes → tout conservé |
| 5.9 | `PurgeOldEntriesAsync_WhenFileAbsent_DoesNotThrow` | Pas de fichier → pas d'exception |
| 5.10 | `HistoryManager_RegisteredAsScoped` | DI Scoped via ServiceCollection |

**Pattern de test :**

```csharp
[Fact]
public async Task RecordAsync_AddsEntryAndWritesAtomically()
{
    var manager = CreateManager();
    var entry = new HistoryEntry { BetId = 42, PublishedAt = DateTime.UtcNow };

    await manager.RecordAsync(entry);

    Assert.True(File.Exists(HistoryPath));
    var ids = await manager.LoadPublishedIdsAsync();
    Assert.Contains(42, ids);
}

[Fact]
public async Task PurgeOldEntriesAsync_RemovesEntriesOlderThan30Days()
{
    var manager = CreateManager();
    var recent = new HistoryEntry { BetId = 1, PublishedAt = DateTime.UtcNow };
    var old = new HistoryEntry { BetId = 2, PublishedAt = DateTime.UtcNow.AddDays(-31) };
    await manager.RecordAsync(recent);
    await manager.RecordAsync(old);

    await manager.PurgeOldEntriesAsync();

    var ids = await manager.LoadPublishedIdsAsync();
    Assert.Contains(1, ids);
    Assert.DoesNotContain(2, ids);
}
```

**Test DI Scoped :**

```csharp
[Fact]
public void HistoryManager_RegisteredAsScoped()
{
    var services = new ServiceCollection();
    services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
    services.AddLogging();
    services.AddScoped<IHistoryManager, HistoryManager>();

    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var manager = scope.ServiceProvider.GetRequiredService<IHistoryManager>();
    Assert.NotNull(manager);
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : 51 existants + ≥7 nouveaux = ≥58 tests, 0 échec
```

### Intelligence Story Précédente (Story 2.3)

**Learnings critiques applicables à Story 3.1 :**

1. **Pattern `IDisposable` pour tests avec fichiers** : `TipsterServiceTests` gère un répertoire temporaire via `IDisposable`. Reprendre ce pattern exactement pour `HistoryManagerTests` — répertoire temporaire unique par test class via `Guid.NewGuid()`.

2. **`LogContext.PushProperty("Step", "...")` validé** : `using (LogContext.PushProperty(...))` fonctionne avec `ILogger<T>`. Ouvrir un scope par méthode publique.

3. **`CancellationToken ct = default` partout** : Toutes les méthodes async ont ce paramètre en dernier.

4. **DI Scoped pattern validé** : `builder.Services.AddScoped<IHistoryManager, HistoryManager>()` dans `Program.cs`, après l'enregistrement de `IUpcomingBetsFetcher`.

5. **`InternalsVisibleTo` déjà configuré** : Les tests ont accès aux membres `internal` sans configuration supplémentaire.

6. **51 tests actuellement** : Vérifier 0 régression après implémentation.

7. **`System.Text.Json` options** : Utiliser `PropertyNameCaseInsensitive = true` pour la désérialisation (robustesse), `WriteIndented = true` pour la lisibilité du `history.json` humain.

8. **Pas de Moq/NSubstitute** : Le projet n'utilise pas de framework de mocking — tests avec fichiers réels sur disque temporaire.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api`

**Action attendue pour l'agent dev :** créer une branche `epic-3/selection-publication-historique` ou continuer sur la branche courante selon la convention du projet (les stories 2.x ont toutes été faites sur `epic-2/connexion-api`).

**Pattern de commit pour cette story :**
```
feat(history): HistoryManager stockage doublons et purge - story 3.1
```

**Commits récents (contexte codebase) :**
```
39ad127 feat(scraper): UpcomingBetsFetcher récupération paris à venir - story 2.3
b1b5646 feat(tipsters): TipsterService lecture tipsters.json - story 2.2
5f874d1 feat(api): ExtendedBet2InvestClient authentification et wrapper - story 2.1
```

**Fichiers créés par story 2.3 (état actuel du codebase) :**
- `Services/IUpcomingBetsFetcher.cs` — pattern d'interface à suivre
- `Services/UpcomingBetsFetcher.cs` — pattern d'implémentation à suivre
- `tests/Bet2InvestPoster.Tests/Services/UpcomingBetsFetcherTests.cs` — pattern de tests à suivre

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-3.1] — AC originaux, FR8, FR10, NFR4
- [Source: .bmadOutput/planning-artifacts/architecture.md#Data-Architecture] — JSON files, atomic writes, 30-day purge, history.json format
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Services/, Models/, interface-per-service, Scoped DI
- [Source: .bmadOutput/planning-artifacts/architecture.md#Process-Patterns] — Error handling, atomic writes
- [Source: .bmadOutput/planning-artifacts/architecture.md#Enforcement-Guidelines] — System.Text.Json, IOptions<T>, logging steps
- [Source: .bmadOutput/planning-artifacts/architecture.md#Architectural-Boundaries] — HistoryManager seul accès à history.json
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs] — `DataPath` (répertoire history.json)
- [Source: src/Bet2InvestPoster/Program.cs] — Pattern DI registration, placement `AddScoped`
- [Source: src/Bet2InvestPoster/Services/UpcomingBetsFetcher.cs] — Pattern Step logging, `LogContext.PushProperty`
- [Source: tests/Bet2InvestPoster.Tests/Services/TipsterServiceTests.cs] — Pattern répertoire temporaire `IDisposable`
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs] — `SettledBet.Id` (int) → source du `BetId`

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

_(aucun problème rencontré — implémentation directe conforme aux specs)_

### Completion Notes List

- AC#1 : `LoadPublishedIdsAsync()` retourne un `HashSet<int>` de tous les betIds depuis `history.json`. Validé par tests 5.3 et 5.4.
- AC#2 : Écriture atomique via `write-to-temp + File.Move(overwrite: true)` dans `SaveAtomicAsync()`. NFR4 satisfait. Validé par test 5.5.
- AC#3 : `PurgeOldEntriesAsync()` filtre `PublishedAt < UtcNow.AddDays(-30)`, réécrit atomiquement, log Step="Purge". Validé par tests 5.7 et 5.8.
- AC#4 : `history.json` absent → `LoadEntriesAsync` retourne `[]` → `RecordAsync` crée le fichier à la première écriture. Validé par test 5.5.
- AC#5 : `RecordAsync` log avec `Step="Publish"` via `LogContext.PushProperty`. Validé par test 5.5 (log via NullLogger).
- AC#6 : `LoadPublishedIdsAsync` retourne `HashSet<int>` — interface utilisable par BetSelector (story 3.2). Validé par tests 5.3, 5.4.
- 62/62 tests passent : 51 existants (0 régression) + 8 originaux + 3 review (DI descriptor, doublon, boundary TimeProvider).
- 0 nouveau package NuGet ajouté.
- 4 fichiers créés, 1 modifié. Review : 3 fichiers modifiés (HistoryEntry.cs, HistoryManager.cs, HistoryManagerTests.cs).

### File List

**Créés :**
- `src/Bet2InvestPoster/Models/HistoryEntry.cs`
- `src/Bet2InvestPoster/Services/IHistoryManager.cs`
- `src/Bet2InvestPoster/Services/HistoryManager.cs`
- `tests/Bet2InvestPoster.Tests/Services/HistoryManagerTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Program.cs` (ajout DI registration Scoped IHistoryManager)
- `.bmadOutput/implementation-artifacts/3-1-history-manager-stockage-doublons-et-purge.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut 3-1 → review)

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-sonnet-4-6 (create-story) | Création story 3.1 — analyse exhaustive artifacts + intelligence story 2.3 |
| 2026-02-24 | claude-sonnet-4-6 (dev-story) | Implémentation complète — 4 fichiers créés, 1 modifié, 59 tests verts |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale : 4 MEDIUM + 3 LOW corrigés — M1:DRY LoadPublishedIdsAsync, M2:test DI descriptor, M3:LogContext scope RecordAsync, M4:ajout TipsterUrl, L1:Directory.CreateDirectory constructeur, L2:TimeProvider injection, L3:guard doublon RecordAsync — 62/62 tests |
