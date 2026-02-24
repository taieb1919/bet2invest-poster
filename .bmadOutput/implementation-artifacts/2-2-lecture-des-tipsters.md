# Story 2.2 : Lecture des Tipsters (TipsterService)

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que le système lise ma liste de tipsters depuis tipsters.json,
So that seuls les tipsters que j'ai choisis soient utilisés pour l'extraction.

## Acceptance Criteria

1. **Given** un fichier `tipsters.json` contenant un tableau de `{ "url": "...", "name": "..." }`
   **When** le cycle d'exécution démarre
   **Then** `TipsterService` relit le fichier à chaque exécution (éditable à chaud, pas de redémarrage)

2. **Given** un fichier `tipsters.json` valide
   **When** `TipsterService` charge les tipsters
   **Then** seuls les tipsters gratuits (free) sont retenus (FR6)
   **And** chaque tipster retenu possède un ID numérique extrait de son URL

3. **Given** un fichier `tipsters.json` absent ou contenant du JSON invalide
   **When** le cycle d'exécution tente de charger les tipsters
   **Then** une erreur explicite est loguée avec le Step `Scrape`
   **And** le cycle est interrompu proprement (pas de crash silencieux)

## Tasks / Subtasks

- [x] Task 1 : Créer le modèle `TipsterConfig` (AC: #1)
  - [x] 1.1 Créer `Models/TipsterConfig.cs` — propriétés : `Url` (string), `Name` (string), `Id` (int, calculé depuis URL)
  - [x] 1.2 Attributs `[JsonPropertyName("url")]` et `[JsonPropertyName("name")]` (camelCase, aligné System.Text.Json)
  - [x] 1.3 Méthode ou logique d'extraction de l'ID numérique depuis l'URL (pattern : `https://bet2invest.com/tipster/{id}` ou `https://bet2invest.com/fr/tipster/{id}`)

- [x] Task 2 : Créer l'interface `ITipsterService` (AC: #1, #2)
  - [x] 2.1 Créer `Services/ITipsterService.cs`
  - [x] 2.2 Signature : `Task<List<TipsterConfig>> LoadTipstersAsync(CancellationToken ct = default)`
  - [x] 2.3 Doc XML : retourne uniquement les tipsters free, relecture fichier à chaque appel

- [x] Task 3 : Implémenter `TipsterService` (AC: #1, #2, #3)
  - [x] 3.1 Créer `Services/TipsterService.cs`
  - [x] 3.2 Injecter `IOptions<PosterOptions>` (pour `DataPath`) et `ILogger<TipsterService>`
  - [x] 3.3 Construire le chemin fichier : `Path.Combine(options.DataPath, "tipsters.json")`
  - [x] 3.4 Lire le fichier avec `File.ReadAllTextAsync()` — relecture à chaque appel (pas de cache)
  - [x] 3.5 Désérialiser avec `System.Text.Json` (`JsonSerializer.Deserialize<List<TipsterConfig>>()`, `PropertyNameCaseInsensitive = true`)
  - [x] 3.6 Extraire l'ID numérique de chaque URL (`Uri` parsing + recherche segment numérique après "tipster")
  - [x] 3.7 Valider : rejeter les entrées avec URL vide, nom vide, ou ID non extractible — loguer un warning par entrée invalide
  - [x] 3.8 Filtrage FR6 : fichier présumé curé par l'utilisateur avec ses tipsters free — vérification runtime `canSeeBets` en story 2.3
  - [x] 3.9 Loguer avec `LogContext.PushProperty("Step", "Scrape")` : nombre de tipsters chargés, nombre retenus
  - [x] 3.10 Si fichier absent : `FileNotFoundException` ou `DirectoryNotFoundException` catchée → loguer erreur avec Step `Scrape`, lever `FileNotFoundException`
  - [x] 3.11 Si JSON invalide : `JsonException` catchée → loguer erreur avec Step `Scrape`, remonter l'exception

- [x] Task 4 : Créer le fichier `tipsters.json` template (AC: #1)
  - [x] 4.1 Créer `tipsters.json` à la racine du projet avec un exemple documenté
  - [x] 4.2 Format : `[{ "url": "https://bet2invest.com/tipster/123", "name": "ExampleTipster" }]`
  - [x] 4.3 JSON est auto-documenté par sa structure — pas de commentaire nécessaire (JSON ne supporte pas les commentaires)

- [x] Task 5 : Enregistrement DI (AC: #1)
  - [x] 5.1 Enregistrer `ITipsterService` / `TipsterService` en **Scoped** dans `Program.cs`
  - [x] 5.2 Placement : après l'enregistrement de `IExtendedBet2InvestClient`

- [x] Task 6 : Tests unitaires (tous les ACs)
  - [x] 6.1 Créer `tests/Bet2InvestPoster.Tests/Services/TipsterServiceTests.cs`
  - [x] 6.2 Tester le chargement d'un fichier valide avec plusieurs tipsters
  - [x] 6.3 Tester l'extraction d'ID depuis différents formats d'URL (`/tipster/123`, `/fr/tipster/456`, trailing slash)
  - [x] 6.4 Tester le comportement fichier absent → `FileNotFoundException` + test `DirectoryNotFoundException` → `FileNotFoundException`
  - [x] 6.5 Tester le comportement JSON invalide → `JsonException`
  - [x] 6.6 Tester fichier vide (`[]`) → liste vide retournée sans erreur
  - [x] 6.7 Tester les entrées invalides (URL vide, nom vide, ID non numérique) → filtrées
  - [x] 6.8 Tester la relecture à chaque appel (modifier le fichier entre 2 appels → résultat différent)
  - [x] 6.9 Vérifier l'enregistrement DI Scoped
  - [x] 6.10 Vérifier 0 régression : 38/38 tests passent (27 existants + 11 nouveaux)

## Dev Notes

### Exigences Techniques Critiques

**Lecture fichier — PAS de cache :**
L'AC#1 exige que `TipsterService` relise le fichier à chaque appel de `LoadTipstersAsync()`. Le fichier est éditable à chaud par l'utilisateur (ajout/retrait de tipsters entre les cycles). NE PAS implémenter de `FileSystemWatcher`, de cache en mémoire, ou de singleton partagé. Chaque appel = une lecture disque complète.

**Extraction de l'ID tipster depuis l'URL :**
L'URL dans tipsters.json suit le pattern `https://bet2invest.com/tipster/{id}` ou `https://bet2invest.com/fr/tipster/{id}`. L'ID est le dernier segment numérique du chemin URL. Utiliser `Uri` parsing + extraction du dernier segment, pas de regex fragile. Exemples :
- `https://bet2invest.com/tipster/12345` → ID = 12345
- `https://bet2invest.com/fr/tipster/67890` → ID = 67890
- `https://bet2invest.com/tipster/abc` → INVALIDE (non numérique)

**Filtrage Free (FR6) — Stratégie à deux niveaux :**
1. **Niveau 1 (cette story)** : Le fichier `tipsters.json` est curé par l'utilisateur qui y met uniquement ses tipsters free. `TipsterService` charge tous les tipsters du fichier sans vérification API.
2. **Niveau 2 (story 2.3)** : `UpcomingBetsFetcher` vérifiera au runtime que `canSeeBets == true` dans la réponse API authentifiée. Si un tipster pro s'est glissé dans la liste, il sera ignoré avec un warning loguée.

**Gestion d'erreurs — Pattern établi :**
- Fichier absent : `FileNotFoundException` → loguer `"tipsters.json introuvable dans {DataPath}"` avec Step `Scrape`, remonter l'exception (le cycle appelant gère l'interruption)
- JSON invalide : `JsonException` → loguer `"tipsters.json contient du JSON invalide: {message}"` avec Step `Scrape`, remonter l'exception
- Fichier vide (`[]`) : PAS une erreur — loguer un warning `"Aucun tipster configuré dans tipsters.json"`, retourner liste vide
- Entrée invalide : loguer un warning par entrée, exclure de la liste retournée

**CancellationToken :**
Toutes les méthodes async prennent `CancellationToken ct = default` (pattern établi en story 2.1). Passer le token à `File.ReadAllTextAsync()` et aux opérations longues.

### Conformité Architecture

**Décisions architecturales à respecter impérativement :**

| Décision | Valeur | Source |
|---|---|---|
| Emplacement service | `Services/TipsterService.cs` avec `Services/ITipsterService.cs` | [Architecture: Structure Patterns] |
| Emplacement modèle | `Models/TipsterConfig.cs` | [Architecture: Structure Patterns] |
| DI Lifetime | `TipsterService` = **Scoped** (un scope par cycle d'exécution) | [Architecture: DI Pattern] |
| Sérialisation | `System.Text.Json` uniquement — JAMAIS Newtonsoft | [Architecture: Data] |
| JSON naming | `camelCase` via `[JsonPropertyName]` | [Architecture: Format Patterns] |
| Nommage C# | PascalCase classes/méthodes, camelCase locals, préfixe `I` interfaces | [Architecture: Naming] |
| Fichiers | Un fichier = une classe | [Architecture: Naming] |
| Interface par service | Obligatoire — `ITipsterService` | [Architecture: Structure] |
| Logging Step | `Scrape` pour les opérations TipsterService | [Architecture: Serilog Template] |
| Error handling | JAMAIS de catch silencieux — chaque erreur est loguée | [Architecture: Process Patterns] |
| Data files | JSON, `System.Text.Json`, `PropertyNameCaseInsensitive = true` | [Architecture: Data] |

**Boundaries à ne PAS violer :**
- `TipsterService` est le seul composant qui lit `tipsters.json` (Data Boundary)
- Pas d'accès direct aux fichiers depuis les Workers ou Telegram
- Pas d'appel HTTP dans `TipsterService` — c'est un service de lecture fichier uniquement
- Les modèles du submodule (`Tipster`, `TipsterStatistics`) ne sont PAS utilisés directement ici — `TipsterConfig` est un modèle propre au projet poster pour la config utilisateur

### Librairies et Frameworks — Exigences Spécifiques

**Packages déjà installés (NE PAS ajouter de nouveaux packages pour cette story) :**

| Package | Version | Usage dans cette story |
|---|---|---|
| System.Text.Json | Inclus .NET 9 | Désérialisation de tipsters.json |
| Serilog 4.3.1 | Déjà installé | Logging avec `ILogger<TipsterService>` et propriété Step `Scrape` |
| Microsoft.Extensions.Options | Inclus .NET 9 | `IOptions<PosterOptions>` pour `DataPath` |

**System.Text.Json — Configuration :**
```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true
};
```
Aligné avec le pattern de `ExtendedBet2InvestClient` (story 2.1).

**Serilog — Pattern de logging avec Step :**
```csharp
using (LogContext.PushProperty("Step", "Scrape"))
{
    _logger.LogInformation("{Count} tipsters chargés depuis {Path}", tipsters.Count, filePath);
}
```
Aligné avec le pattern établi en story 2.1. Utiliser `LogContext.PushProperty()` (pas `Log.ForContext()`) car le projet utilise `ILogger<T>` de Microsoft.Extensions.Logging via Serilog.Extensions.Hosting.

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
├── Models/
│   └── TipsterConfig.cs                 ← NOUVEAU (modèle config tipster)
└── Services/
    ├── ITipsterService.cs               ← NOUVEAU (interface)
    └── TipsterService.cs                ← NOUVEAU (implémentation)

tests/Bet2InvestPoster.Tests/
└── Services/
    └── TipsterServiceTests.cs           ← NOUVEAU (tests)

tipsters.json                            ← NOUVEAU (fichier template à la racine)
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
└── Program.cs                           ← MODIFIER (ajout DI registration Scoped ITipsterService)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/                ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Worker.cs                            ← Pas de logique métier dans les Workers
├── Configuration/                       ← Options déjà définies (PosterOptions.DataPath suffit)
│   ├── Bet2InvestOptions.cs             ← NE PAS modifier
│   ├── TelegramOptions.cs              ← NE PAS modifier
│   └── PosterOptions.cs                ← NE PAS modifier (DataPath déjà présent)
├── Services/
│   ├── IExtendedBet2InvestClient.cs    ← NE PAS modifier
│   ├── ExtendedBet2InvestClient.cs     ← NE PAS modifier
│   └── SerilogConsoleLoggerAdapter.cs  ← NE PAS modifier
├── Models/
│   └── BetOrderRequest.cs              ← NE PAS modifier
├── Exceptions/                          ← NE PAS modifier
├── appsettings.json                     ← NE PAS modifier
└── appsettings.Development.json         ← NE PAS modifier
```

**Conventions de nommage fichiers :**
- Interface : `I` + nom classe → `ITipsterService.cs`
- Implémentation : nom exact de la classe → `TipsterService.cs`
- Modèle : nom du DTO → `TipsterConfig.cs`
- Tests : nom classe + `Tests` → `TipsterServiceTests.cs`

### Exigences de Tests

**Framework :** xUnit (déjà configuré dans `Bet2InvestPoster.Tests.csproj`)

**Tests existants (27) — 0 RÉGRESSION TOLÉRÉE :**
- `UnitTest1.cs` : 1 test (Worker instantiation)
- `Configuration/OptionsTests.cs` : 10 tests (Options binding, defaults, redaction)
- `Services/ExtendedBet2InvestClientTests.cs` : 16 tests (auth, rate limiting, DI, exceptions)

**Nouveaux tests requis — `Services/TipsterServiceTests.cs` :**

`TipsterService` est un service de lecture fichier — les tests utilisent le système de fichiers temporaire (`Path.GetTempPath()` + fichiers temporaires). PAS besoin de `FakeHttpMessageHandler`.

**Stratégie de test :**

1. **Tests de chargement fichier** :
   - Créer un fichier JSON temporaire avec du contenu valide → vérifier parsing correct
   - Vérifier que les IDs sont extraits des URLs
   - Vérifier que les noms et URLs sont préservés

2. **Tests d'extraction d'ID** :
   - URL standard : `https://bet2invest.com/tipster/123` → ID = 123
   - URL avec locale : `https://bet2invest.com/fr/tipster/456` → ID = 456
   - URL invalide (pas d'ID numérique) → entrée exclue avec warning

3. **Tests d'erreurs** :
   - Fichier absent → exception appropriée
   - JSON invalide → exception appropriée
   - Fichier vide (`[]`) → liste vide sans erreur
   - Entrées invalides mélangées → seules les valides retournées

4. **Test de relecture (hot-reload)** :
   - Appeler `LoadTipstersAsync()`, modifier le fichier, rappeler → résultat mis à jour

5. **Test DI** :
   - Vérifier l'enregistrement Scoped via `ServiceCollection`

**Pattern de test (aligné avec les tests existants) :**
```csharp
public class TipsterServiceTests : IDisposable
{
    private readonly string _tempDir;

    public TipsterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    private TipsterService CreateService(string? dataPath = null) =>
        new(Options.Create(new PosterOptions { DataPath = dataPath ?? _tempDir }),
            NullLogger<TipsterService>.Instance);
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : tous les anciens tests (27) + nouveaux tests passent, 0 échec
```

### Intelligence Story Précédente (Story 2.1)

**Learnings clés de la story 2.1 :**

1. **Pattern DI validé** — Scoped pour les services, Singleton pour Bet2InvestClient. Enregistrement dans Program.cs via `builder.Services.AddScoped<ITipsterService, TipsterService>()`.

2. **System.Text.Json validé** — `JsonSerializerOptions` avec `PropertyNameCaseInsensitive = true`. Pas de `PropertyNamingPolicy` nécessaire pour la lecture (les attributs `[JsonPropertyName]` suffisent).

3. **LogContext.PushProperty validé** — Pattern `using (LogContext.PushProperty("Step", "Scrape"))` fonctionne correctement avec `ILogger<T>`. `Enrich.FromLogContext()` est déjà configuré dans Program.cs (story 1.2).

4. **CancellationToken partout** — Toutes les méthodes async ont `CancellationToken ct = default` en dernier paramètre. Le token est propagé à toutes les opérations async.

5. **InternalsVisibleTo** — Déjà configuré dans le csproj principal (`Bet2InvestPoster.Tests`). Les constructeurs et méthodes `internal` sont accessibles dans les tests.

6. **27 tests passent** — 11 epic 1 + 16 story 2.1. Pattern xUnit sans framework de mocking. Utiliser `NullLogger<T>.Instance` pour les loggers dans les tests.

7. **PosterOptions.DataPath** — Propriété existante (défaut `"."`) qui définit le répertoire des fichiers de données. Utiliser pour construire le chemin vers `tipsters.json`.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api`

**Commits récents :**
```
5f874d1 feat(api): ExtendedBet2InvestClient authentification et wrapper - story 2.1
b5f9279 docs(retro): rétrospective épique 1 — fondation du projet terminée
d44dc8e feat(infra): ajouter service systemd et CI GitHub Actions - story 1.3
```

**Pattern de commit :** `type(scope): description en français - story X.Y`
- Pour cette story : `feat(tipsters): TipsterService lecture tipsters.json - story 2.2`

**Fichiers créés/modifiés dans story 2.1 (contexte) :**
- `Services/ExtendedBet2InvestClient.cs` — pattern de service à suivre
- `Services/IExtendedBet2InvestClient.cs` — pattern d'interface à suivre
- `Models/BetOrderRequest.cs` — pattern de modèle à suivre
- `Program.cs` — lieu d'ajout de la registration DI

### Références

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-2.2] — AC originaux, FR4, FR6
- [Source: .bmadOutput/planning-artifacts/architecture.md#Format-Patterns] — Format tipsters.json, JSON conventions
- [Source: .bmadOutput/planning-artifacts/architecture.md#Structure-Patterns] — Services/, Models/, interface-per-service
- [Source: .bmadOutput/planning-artifacts/architecture.md#Process-Patterns] — Error handling, DI pattern, logging
- [Source: .bmadOutput/planning-artifacts/architecture.md#Project-Structure] — Data boundary, TipsterService emplacement
- [Source: .bmadOutput/implementation-artifacts/2-1-extended-bet2invest-client-authentification-et-wrapper.md] — Patterns DI, System.Text.Json, logging, CancellationToken, tests
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs] — DataPath propriété pour localiser tipsters.json
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs] — Modèle Tipster (Pro, Tier) pour référence FR6

## Dev Agent Record

### Agent Model Used

claude-opus-4-6

### Debug Log References

- **DirectoryNotFoundException** : `File.ReadAllTextAsync()` avec un `DataPath` inexistant lève `DirectoryNotFoundException` (pas `FileNotFoundException`). Résolu en catchant les deux et normalisant vers `FileNotFoundException` avec le chemin du fichier.
- **JSON ne supporte pas les commentaires** : Task 4.3 demandait un commentaire dans tipsters.json — JSON standard ne le permet pas. Le format est auto-documenté par sa structure.

### Completion Notes List

- AC#1 : `TipsterService.LoadTipstersAsync()` relit `tipsters.json` à chaque appel via `File.ReadAllTextAsync()` — pas de cache, hot-editable. Validé par test hot-reload.
- AC#2 : Chaque tipster retenu a un `Id` numérique extrait de l'URL via `TipsterConfig.TryExtractId()` (Uri parsing, segments après "tipster"). FR6 satisfait par curation utilisateur + vérification `canSeeBets` en story 2.3.
- AC#3 : Fichier absent → `FileNotFoundException` loguée avec Step `Scrape`. JSON invalide → `JsonException` loguée avec Step `Scrape`. Exceptions remontées pour interruption propre du cycle.
- 40/40 tests passent (27 existants + 11 nouveaux + 2 code review), 0 régression
- 0 nouveau package NuGet ajouté
- Pattern aligné avec story 2.1 : System.Text.Json, LogContext.PushProperty, CancellationToken, Scoped DI

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-opus-4-6 (create-story) | Création story 2.2 — analyse exhaustive artifacts + codebase exploration |
| 2026-02-24 | claude-opus-4-6 (dev-story) | Implémentation complète story 2.2 — 4 fichiers créés, 1 modifié, 38 tests verts |
| 2026-02-24 | claude-sonnet-4-6 (code-review) | Code review adversariale — 8 issues corrigés (3 MEDIUM, 5 LOW) — 40/40 tests verts |

### File List

**Créés :**
- `src/Bet2InvestPoster/Models/TipsterConfig.cs`
- `src/Bet2InvestPoster/Services/ITipsterService.cs`
- `src/Bet2InvestPoster/Services/TipsterService.cs`
- `tests/Bet2InvestPoster.Tests/Services/TipsterServiceTests.cs`
- `tipsters.json`

**Modifiés :**
- `src/Bet2InvestPoster/Program.cs` (ajout DI registration Scoped ITipsterService)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (mise à jour statut 2-2 → review)
