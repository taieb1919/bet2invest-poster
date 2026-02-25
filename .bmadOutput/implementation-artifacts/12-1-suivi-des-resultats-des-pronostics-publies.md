# Story 12.1: Suivi des Résultats des Pronostics Publiés

Status: review

## Story

As a l'utilisateur,
I want que le système vérifie automatiquement les résultats (gagné/perdu) de mes pronostics publiés,
so that je dispose de données fiables pour évaluer la qualité de mes publications.

## Acceptance Criteria

1. **Given** des pronostics publiés enregistrés dans `history.json`
   **When** le cycle quotidien s'exécute
   **Then** `ResultTracker` vérifie les résultats des pronostics publiés dans les derniers 7 jours via l'API bet2invest

2. **Given** un pronostic publié dont le résultat est disponible
   **When** `ResultTracker` interroge l'API
   **Then** chaque entrée dans `history.json` est enrichie avec : `result` (won/lost/pending), `odds`, `sport`, `tipsterName`

3. **Given** l'écriture dans `history.json` modifiée
   **When** les résultats sont mis à jour
   **Then** l'écriture reste atomique (write-to-temp + rename)

4. **Given** l'API bet2invest ne retourne pas encore le résultat d'un pronostic
   **When** `ResultTracker` interroge l'API
   **Then** le pronostic reste en statut `pending` et sera revérifié au prochain cycle

5. **Given** le résultat d'un pronostic est résolu (won/lost)
   **When** `ResultTracker` met à jour `history.json`
   **Then** le résultat est définitif et ne sera plus revérifié

6. **Given** le cycle de vérification des résultats
   **When** les logs sont écrits
   **Then** chaque vérification est loguée avec le Step `Report` (nombre vérifié, nombre résolu, nombre pending)

## Tasks / Subtasks

- [x] Task 1 — Enrichir HistoryEntry avec les champs résultat (AC: #1, #2)
  - [x] 1.1 Ajouter `Result` (string? — "won"/"lost"/"pending", null = non vérifié), `Odds` (decimal?), `Sport` (string?), `TipsterName` (string?) à `HistoryEntry`
  - [x] 1.2 Les nouveaux champs sont nullable pour rétrocompatibilité avec les entrées existantes
  - [x] 1.3 Remplir `Odds`, `Sport`, `TipsterName` dès la publication dans `BetPublisher` (données déjà disponibles sur `PendingBet`)

- [x] Task 2 — Implémenter IResultTracker / ResultTracker (AC: #1, #4, #5)
  - [x] 2.1 Créer `Services/IResultTracker.cs` + `Services/ResultTracker.cs`
  - [x] 2.2 Méthode `TrackResultsAsync(CancellationToken)` : charge les entrées history des 7 derniers jours où `Result` est null ou "pending"
  - [x] 2.3 Pour chaque entrée à vérifier, appeler l'API pour obtenir le statut du bet
  - [x] 2.4 Si résultat disponible (won/lost) → mettre à jour l'entrée et sauvegarder
  - [x] 2.5 Si résultat non disponible → laisser en "pending"
  - [x] 2.6 Respecter le rate limiting 500ms entre requêtes API

- [x] Task 3 — Ajouter méthode API pour vérifier les résultats (AC: #1)
  - [x] 3.1 Ajouter `GetBetResultAsync(int betId, CancellationToken)` à `IExtendedBet2InvestClient`
  - [x] 3.2 Implémenter dans `ExtendedBet2InvestClient` — appel GET sur l'endpoint approprié (à explorer dans l'API, probablement via `/v1/statistics/{tipsterNumericId}` en vérifiant le statut resolved des bets, ou endpoint dédié)
  - [x] 3.3 Retourner un record/DTO avec le statut (won/lost/pending)

- [x] Task 4 — Ajouter méthode de mise à jour dans HistoryManager (AC: #3)
  - [x] 4.1 Ajouter `UpdateEntriesAsync(List<HistoryEntry> updated)` à `IHistoryManager`
  - [x] 4.2 Implémenter avec écriture atomique (write-to-temp + rename) et SemaphoreSlim existant

- [x] Task 5 — Intégrer dans PostingCycleService (AC: #1)
  - [x] 5.1 Injecter `IResultTracker` dans `PostingCycleService`
  - [x] 5.2 Appeler `_resultTracker.TrackResultsAsync()` dans `RunCycleAsync` APRÈS la purge et AVANT le fetch (pour ne pas retarder la publication)

- [x] Task 6 — Logging structuré (AC: #6)
  - [x] 6.1 Ajouter le Step `Report` aux logs du ResultTracker
  - [x] 6.2 Loguer : nombre d'entrées vérifiées, nombre résolues (won/lost), nombre pending restant

- [x] Task 7 — Tests unitaires
  - [x] 7.1 `ResultTrackerTests.cs` : vérifier le tracking normal (résultats disponibles → mise à jour)
  - [x] 7.2 Test : résultat non disponible → reste pending
  - [x] 7.3 Test : résultat déjà résolu → pas de re-vérification
  - [x] 7.4 Test : entrées > 7 jours ignorées
  - [x] 7.5 Test : écriture atomique via HistoryManager mock
  - [x] 7.6 Mettre à jour `PostingCycleServiceTests` pour vérifier l'appel au ResultTracker

## Dev Notes

### Modèle de données — Enrichissement HistoryEntry

Le fichier `src/Bet2InvestPoster/Models/HistoryEntry.cs` contient actuellement :
- `BetId` (int), `MatchupId`, `MarketKey`, `Designation`, `PublishedAt`, `MatchDescription`, `TipsterUrl`

**Ajouter** (tous nullable, `[JsonPropertyName("...")]`) :
- `result` (string?) — "won", "lost", "pending", null (non vérifié)
- `odds` (decimal?) — cote au moment de la publication
- `sport` (string?) — nom du sport
- `tipsterName` (string?) — nom/slug du tipster

**Rétrocompatibilité** : les entrées existantes sans ces champs auront null = "non vérifié" = à vérifier.

### Stratégie de vérification des résultats

L'API bet2invest ne fournit pas d'endpoint direct "get bet result by ID". La stratégie recommandée :

1. **Option A (recommandée)** : Utiliser le scraper submodule `Bet2InvestClient.GetSettledBetsAsync()` qui retourne les paris résolus d'un tipster. Comparer les `betId` des HistoryEntry avec les settled bets pour déterminer won/lost. Le `SettledBet` du scraper a un champ `Status` qui indique le résultat.

2. **Option B** : Explorer si `/v1/statistics/{tipsterNumericId}` retourne aussi les bets résolus récents.

L'option A est préférable car le scraper existe déjà et gère la pagination + auth. Il faut :
- Regrouper les entrées history par tipster (via `TipsterUrl` → `TipsterConfig`)
- Pour chaque tipster, appeler `GetSettledBetsAsync()` (méthode du scraper submodule)
- Matcher les betId entre settled bets et history entries
- Un bet settled avec `Status` correspondant → won ou lost

**IMPORTANT** : Vérifier si `Bet2InvestClient.GetSettledBetsAsync()` du submodule est accessible via le `ExtendedBet2InvestClient`. Si non, ajouter une méthode wrapper `GetSettledBetsForTipsterAsync()`.

### Pattern existant à suivre

- **Interface + Implémentation** : `IResultTracker` + `ResultTracker` dans `Services/`
- **DI** : Enregistrer en **Scoped** dans `Program.cs` (un scope par cycle)
- **Logging** : `ILogger<ResultTracker>`, Step = "Report"
- **Rate limiting** : 500ms entre requêtes API (déjà géré dans `ExtendedBet2InvestClient`)
- **Tests** : Pattern Fake avec sealed classes, `NullLogger<T>.Instance`, `OptionsWrapper<T>`

### Fichiers à créer

| Fichier | Description |
|---------|-------------|
| `src/Bet2InvestPoster/Services/IResultTracker.cs` | Interface du service |
| `src/Bet2InvestPoster/Services/ResultTracker.cs` | Implémentation |
| `tests/Bet2InvestPoster.Tests/Services/ResultTrackerTests.cs` | Tests unitaires |

### Fichiers à modifier

| Fichier | Modification |
|---------|-------------|
| `src/Bet2InvestPoster/Models/HistoryEntry.cs` | Ajouter champs result, odds, sport, tipsterName |
| `src/Bet2InvestPoster/Services/IHistoryManager.cs` | Ajouter `UpdateEntriesAsync()` |
| `src/Bet2InvestPoster/Services/HistoryManager.cs` | Implémenter `UpdateEntriesAsync()` |
| `src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs` | Ajouter méthode pour récupérer settled bets |
| `src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs` | Implémenter récupération settled bets |
| `src/Bet2InvestPoster/Services/PostingCycleService.cs` | Injecter et appeler ResultTracker |
| `src/Bet2InvestPoster/Services/BetPublisher.cs` | Remplir odds/sport/tipsterName dans HistoryEntry lors de la publication |
| `src/Bet2InvestPoster/Program.cs` | Enregistrer IResultTracker en DI |
| `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs` | Ajouter FakeResultTracker |

### Données déjà disponibles sur PendingBet

Pour enrichir `HistoryEntry` dès la publication, les données sont déjà présentes :
- `PendingBet.Price` → odds (decimal via `SettledBet.Price`)
- `PendingBet.Sport?.Name` → sport
- `PendingBet.TipsterUsername` → tipsterName (ajouté story 11.2)
- `PendingBet.TipsterRoi` → non nécessaire pour l'histoire 12.1

### Submodule — NE JAMAIS MODIFIER

Le submodule `jtdev-bet2invest-scraper/` est en **lecture seule**. Utiliser ses types (`SettledBet`, `Bet2InvestClient`) mais ne jamais modifier ses fichiers. Si une méthode manque, créer un wrapper dans `ExtendedBet2InvestClient`.

### Architecture Boundary

- `HistoryManager` est le **seul** composant qui lit/écrit `history.json`
- `ResultTracker` ne doit PAS écrire directement dans le fichier — il passe par `HistoryManager.UpdateEntriesAsync()`
- `ExtendedBet2InvestClient` est le **seul** point de contact avec l'API bet2invest

### Steps de log autorisés

Auth, Scrape, Select, Publish, Notify, Purge, **Report** (nouveau pour cette story)

### Project Structure Notes

- Aucun conflit avec la structure existante
- Nouveaux fichiers dans `Services/` suivent le pattern interface + implémentation
- Tests dans `tests/Bet2InvestPoster.Tests/Services/`

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Epic 12 — Story 12.1]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Data Architecture]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation Patterns]
- [Source: .bmadOutput/implementation-artifacts/11-2-selection-multi-criteres-intelligente.md — PendingBet enrichment pattern]
- [Source: src/Bet2InvestPoster/Models/HistoryEntry.cs — current model]
- [Source: src/Bet2InvestPoster/Services/HistoryManager.cs — atomic write pattern]
- [Source: src/Bet2InvestPoster/Services/PostingCycleService.cs — cycle orchestration]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6

### Debug Log References

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created
- Stratégie de vérification via settled bets du scraper submodule identifiée
- Enrichissement HistoryEntry avec données déjà disponibles sur PendingBet

### File List

- `src/Bet2InvestPoster/Models/HistoryEntry.cs` — ajout champs Result, TipsterName, BetId
- `src/Bet2InvestPoster/Services/IResultTracker.cs` — nouvelle interface IResultTracker
- `src/Bet2InvestPoster/Services/ResultTracker.cs` — implémentation du suivi des résultats
- `src/Bet2InvestPoster/Services/IHistoryManager.cs` — ajout méthode UpdateEntriesAsync
- `src/Bet2InvestPoster/Services/HistoryManager.cs` — implémentation UpdateEntriesAsync
- `src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs` — ajout GetSettledBetsForTipsterAsync
- `src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs` — implémentation GetSettledBetsForTipsterAsync
- `src/Bet2InvestPoster/Services/BetPublisher.cs` — enrichissement HistoryEntry et TimeProvider
- `src/Bet2InvestPoster/Services/PostingCycleService.cs` — intégration appel TrackResultsAsync
- `src/Bet2InvestPoster/Program.cs` — enregistrement IResultTracker / ResultTracker
- `tests/Bet2InvestPoster.Tests/Services/ResultTrackerTests.cs` — tests unitaires du ResultTracker
- `tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs` — mise à jour suite enrichissement HistoryEntry
