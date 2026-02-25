# Story 9.1: Filtrage par Cotes et Plage Horaire

Status: review

## Story

As a l'utilisateur,
I want configurer une fourchette de cotes acceptées et une plage horaire maximale pour les événements,
so that seuls les pronostics pertinents (cotes raisonnables, événements proches) soient publiés.

## Acceptance Criteria

1. **Given** `PosterOptions` configuré avec `MinOdds: 1.20`, `MaxOdds: 3.50`, `EventHorizonHours: 24` **When** `BetSelector` filtre les paris candidats **Then** les paris avec une cote < `MinOdds` ou > `MaxOdds` sont exclus de la sélection (FR35) **And** les paris dont l'événement démarre au-delà de `EventHorizonHours` heures sont exclus (FR36) **And** le filtrage est appliqué AVANT la sélection aléatoire
2. **Given** `MinOdds` et `MaxOdds` non configurés (valeurs par défaut) **When** le cycle s'exécute **Then** aucun filtrage par cotes n'est appliqué (comportement rétrocompatible)
3. **Given** `EventHorizonHours` non configuré (valeur par défaut) **When** le cycle s'exécute **Then** aucun filtrage par plage horaire n'est appliqué (comportement rétrocompatible)
4. **Given** les filtres configurés réduisent les candidats à zéro **When** `BetSelector` effectue la sélection **Then** le cycle se termine avec un message `"⚠️ Aucun pronostic ne correspond aux critères de filtrage."` **And** une notification Telegram est envoyée avec le détail des filtres actifs
5. **Given** l'utilisateur configure les filtres via `appsettings.json` ou variables d'environnement **When** le service démarre **Then** les paramètres `MinOdds`, `MaxOdds`, `EventHorizonHours` sont chargés dans `PosterOptions` **And** les variables d'environnement surchargent `appsettings.json` (ex: `Poster__MinOdds=1.50`)
6. **Given** le cycle s'exécute avec filtrage actif **When** les logs sont écrits **Then** le nombre de candidats avant et après filtrage est logué avec le Step `Select`

## Tasks / Subtasks

- [x] Task 1 : Ajouter les propriétés de filtrage à `PosterOptions` (AC: #5)
  - [x] 1.1 Ajouter `decimal? MinOdds` (default `null` = pas de filtrage)
  - [x] 1.2 Ajouter `decimal? MaxOdds` (default `null` = pas de filtrage)
  - [x] 1.3 Ajouter `int? EventHorizonHours` (default `null` = pas de filtrage)
  - [x] 1.4 Ajouter les entrées correspondantes dans `appsettings.json`
- [x] Task 2 : Modifier `BetSelector.SelectAsync` pour appliquer les filtres (AC: #1, #2, #3, #6)
  - [x] 2.1 Injecter `IOptions<PosterOptions>` dans `BetSelector`
  - [x] 2.2 Filtrer par cotes : exclure `b.Price < MinOdds` ou `b.Price > MaxOdds` (si configuré)
  - [x] 2.3 Filtrer par plage horaire : exclure `b.Event.Starts > DateTime.UtcNow.AddHours(EventHorizonHours)` (si configuré)
  - [x] 2.4 Appliquer les filtres AVANT le filtre doublons existant et AVANT la sélection aléatoire
  - [x] 2.5 Loguer le nombre avant/après filtrage avec Step `Select`
- [x] Task 3 : Gérer le cas zéro candidats après filtrage (AC: #4)
  - [x] 3.1 Si `available.Count == 0` après filtrage, retourner liste vide (BetSelector)
  - [x] 3.2 Dans `PostingCycleService`, détecter la liste vide et envoyer notification avec détail des filtres
- [x] Task 4 : Tests unitaires (AC: #1–#6)
  - [x] 4.1 Tests `BetSelector` : filtrage par cotes (min, max, les deux)
  - [x] 4.2 Tests `BetSelector` : filtrage par plage horaire
  - [x] 4.3 Tests `BetSelector` : aucun filtrage quand options nulles (rétrocompatibilité)
  - [x] 4.4 Tests `BetSelector` : zéro candidats après filtrage
  - [x] 4.5 Tests `BetSelector` : filtrage appliqué AVANT sélection aléatoire
  - [x] 4.6 Mettre à jour les tests existants `BetSelectorTests` si le constructeur change

## Dev Notes

### Propriétés de cotes et dates dans PendingBet

`PendingBet` hérite de `SettledBet` (submodule). Propriétés pertinentes :

| Propriété | Type | Classe | Usage filtrage |
|---|---|---|---|
| `Price` | `decimal` | `SettledBet` | **Cote principale** — utiliser pour MinOdds/MaxOdds |
| `Event.Starts` | `DateTime` | `BetEvent` (via SettledBet) | **Date de l'événement** — utiliser pour EventHorizonHours |

**ATTENTION** : `Price` est en format décimal (ex: `1.85`, `2.50`). Les `MarketPrice.Price` dans `Market.Prices` est un `int` (format différent) — NE PAS utiliser celui-là.

### Modification de BetSelector — Injection IOptions<PosterOptions>

Le constructeur actuel (`src/Bet2InvestPoster/Services/BetSelector.cs`) :
```csharp
public BetSelector(IHistoryManager historyManager, ILogger<BetSelector> logger)
```

Ajouter `IOptions<PosterOptions>` :
```csharp
public BetSelector(IHistoryManager historyManager, IOptions<PosterOptions> posterOptions, ILogger<BetSelector> logger)
{
    _historyManager = historyManager;
    _options = posterOptions.Value;
    _logger = logger;
}
```

`BetSelector` est enregistré Scoped dans `Program.cs` — pas de changement DI nécessaire car `IOptions<PosterOptions>` est déjà disponible.

### Ordre des filtres dans SelectAsync

Le code actuel dans `BetSelector.SelectAsync` fait :
1. Exclure les doublons (via `HistoryManager`)
2. Choisir cible aléatoire (5/10/15)
3. Sélection aléatoire

Modifier pour insérer le filtrage avancé **entre les étapes 1 et 2** :
1. Exclure les doublons (existant)
2. **NOUVEAU : Filtrer par cotes** (si MinOdds/MaxOdds configurés)
3. **NOUVEAU : Filtrer par plage horaire** (si EventHorizonHours configuré)
4. Loguer avant/après filtrage
5. Choisir cible aléatoire (5/10/15)
6. Sélection aléatoire

```csharp
// Après le filtre doublons existant...
var beforeFilterCount = available.Count;

if (_options.MinOdds.HasValue)
    available = available.Where(b => b.Price >= _options.MinOdds.Value).ToList();

if (_options.MaxOdds.HasValue)
    available = available.Where(b => b.Price <= _options.MaxOdds.Value).ToList();

if (_options.EventHorizonHours.HasValue)
{
    var horizon = DateTime.UtcNow.AddHours(_options.EventHorizonHours.Value);
    available = available.Where(b => b.Event?.Starts <= horizon).ToList();
}

if (beforeFilterCount != available.Count)
{
    _logger.LogInformation(
        "Filtrage avancé : {Before} → {After} candidats (MinOdds={Min}, MaxOdds={Max}, Horizon={Horizon}h)",
        beforeFilterCount, available.Count, _options.MinOdds, _options.MaxOdds, _options.EventHorizonHours);
}
```

### PosterOptions — Valeurs par défaut null

Les propriétés DOIVENT être `nullable` pour assurer la rétrocompatibilité (AC #2, #3) :
```csharp
public decimal? MinOdds { get; set; }      // null = pas de filtrage
public decimal? MaxOdds { get; set; }      // null = pas de filtrage
public int? EventHorizonHours { get; set; } // null = pas de filtrage
```

NE PAS utiliser de valeurs par défaut comme `0` ou `decimal.MaxValue` — cela compliquerait la logique de "pas de filtrage".

### appsettings.json — Section Poster étendue

```json
{
  "Poster": {
    "ScheduleTime": "08:00",
    "RetryDelayMs": 60000,
    "MaxRetryCount": 3,
    "DataPath": ".",
    "LogPath": "logs",
    "BankrollId": "",
    "MinOdds": null,
    "MaxOdds": null,
    "EventHorizonHours": null
  }
}
```

Variables d'environnement : `Poster__MinOdds=1.50`, `Poster__MaxOdds=3.50`, `Poster__EventHorizonHours=24`

### Cas zéro candidats — Notification dans PostingCycleService

Quand `BetSelector.SelectAsync` retourne une liste vide, `PostingCycleService` doit déjà gérer ce cas. Vérifier le comportement actuel :
- Si le cycle continue normalement avec 0 pronostics → ajouter une vérification spécifique
- Le message notification doit inclure les filtres actifs : `"⚠️ Aucun pronostic ne correspond aux critères de filtrage (cotes: 1.20-3.50, horizon: 24h)."`

**IMPORTANT** : Distinguer "zéro candidats car tous exclus par filtrage" vs "zéro candidats car tous déjà publiés". Le message doit être différent.

### Tests existants à mettre à jour

`BetSelectorTests.cs` crée le selector via :
```csharp
private static BetSelector CreateSelector(IEnumerable<string>? publishedKeys = null)
    => new(new FakeHistoryManager(publishedKeys), NullLogger<BetSelector>.Instance);
```

Modifier pour injecter `IOptions<PosterOptions>` :
```csharp
private static BetSelector CreateSelector(
    IEnumerable<string>? publishedKeys = null,
    PosterOptions? options = null)
    => new(
        new FakeHistoryManager(publishedKeys),
        Options.Create(options ?? new PosterOptions()),
        NullLogger<BetSelector>.Instance);
```

Les tests existants passent `null` pour options → `PosterOptions` par défaut → aucun filtrage → **rétrocompatibilité assurée**.

Les `MakeBet` helper doit être étendu pour inclure `Price` et `Event.Starts` :
```csharp
private static PendingBet MakeBet(int id, decimal price = 2.0m, DateTime? starts = null) => new()
{
    Id = id,
    Team = "TEAM1",
    Price = price,
    Event = new BetEvent { Starts = starts ?? DateTime.UtcNow.AddHours(12) },
    Market = new PendingBetMarket { MatchupId = $"{id}", Key = "s;0;m" }
};
```

### Fichiers à modifier

| Fichier | Action |
|---|---|
| `src/Bet2InvestPoster/Configuration/PosterOptions.cs` | Ajouter `MinOdds`, `MaxOdds`, `EventHorizonHours` |
| `src/Bet2InvestPoster/Services/BetSelector.cs` | Injecter `IOptions<PosterOptions>`, ajouter filtrage |
| `src/Bet2InvestPoster/appsettings.json` | Ajouter les nouvelles clés |
| `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs` | Mettre à jour constructeur + nouveaux tests |

**Fichiers potentiellement à modifier** (selon implémentation AC #4) :
| `src/Bet2InvestPoster/Services/PostingCycleService.cs` | Notification si zéro candidats après filtrage |

**Aucun nouveau fichier à créer.**

### Project Structure Notes

- Aucun nouveau service/interface à créer — extension de `BetSelector` existant
- Pas de changement DI dans `Program.cs` — `IOptions<PosterOptions>` déjà disponible
- Le filtrage est dans la couche sélection (Services/BetSelector) conformément à l'architecture
- Les filtres utilisent des propriétés héritées du submodule (`Price`, `Event.Starts`) — **ne pas modifier le submodule**

### Testing Standards

- Tests xUnit avec le pattern existant de `BetSelectorTests.cs`
- Utiliser `FakeHistoryManager` existant (pas de changement nécessaire)
- Pour tester le filtrage horaire, utiliser `DateTime.UtcNow.AddHours(x)` dans les fixtures
- Pattern : Arrange → Act → Assert, un assert logique par test
- Vérifier la rétrocompatibilité : tous les tests existants doivent passer sans modification de comportement

### Learnings Story 8.2

1. SemaphoreSlim statique pour protéger les fichiers en Scoped — pattern validé
2. `TryExtractSlug()` pour validation/extraction depuis URLs bet2invest
3. Fakes doivent être mis à jour quand les interfaces changent
4. 226 tests passent actuellement — ne pas en casser

### Learnings Epic 7 (Rétrospective)

1. Le pattern CommandHandler scale bien — pas pertinent ici
2. Tests async : signaling déterministe, JAMAIS `Task.Delay`
3. Mettre à jour story file et sprint-status en fin d'implémentation

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 9.1]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation Patterns]
- [Source: src/Bet2InvestPoster/Services/BetSelector.cs — service à modifier]
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs — options à étendre]
- [Source: src/Bet2InvestPoster/Models/PendingBet.cs — modèle avec Price et Market]
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs — SettledBet.Price, BetEvent.Starts]
- [Source: tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs — tests à étendre]
- [Source: .bmadOutput/implementation-artifacts/8-2-commandes-tipsters-add-et-tipsters-remove-crud-tipsters.md]

## Dev Agent Record

### Agent Model Used

claude-opus-4-6

### Debug Log References

_Aucun._

### Completion Notes List

- ✅ Task 1 : PosterOptions étendu avec MinOdds?, MaxOdds?, EventHorizonHours? (nullable pour rétrocompatibilité)
- ✅ Task 1.4 : appsettings.json mis à jour avec les 3 nouvelles clés à null
- ✅ Task 2 : BetSelector modifié — injection IOptions<PosterOptions>, filtrage avancé inséré APRÈS dedup et AVANT sélection aléatoire
- ✅ Task 2.5 : Logging avant/après filtrage avec Step="Select"
- ✅ Task 3 : INotificationService étendu avec NotifyNoFilteredCandidatesAsync; PostingCycleService injecte PosterOptions et envoie notification si zéro candidats et filtres actifs
- ✅ Task 4 : 10 nouveaux tests dans BetSelectorTests (236 total, 0 échec); MakeBet étendu avec price et starts
- ✅ Tous les Fakes INotificationService mis à jour (PostingCycleServiceTests, NotificationTests, SchedulerWorkerTests, SchedulerWorkerPollyTests)

### File List

- `src/Bet2InvestPoster/Configuration/PosterOptions.cs`
- `src/Bet2InvestPoster/Services/BetSelector.cs`
- `src/Bet2InvestPoster/Services/INotificationService.cs`
- `src/Bet2InvestPoster/Services/NotificationService.cs`
- `src/Bet2InvestPoster/Services/PostingCycleService.cs`
- `src/Bet2InvestPoster/appsettings.json`
- `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs`
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs`
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs`

## Change Log

- 2026-02-25 : Implémentation story 9.1 — filtrage par cotes (MinOdds, MaxOdds) et plage horaire (EventHorizonHours) dans BetSelector; notification Telegram si zéro candidats après filtrage; 10 nouveaux tests (236 total)
