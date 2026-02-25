# Story 11.2: Sélection Multi-Critères Intelligente

Status: review

## Story

As a l'utilisateur,
I want que le système sélectionne les pronostics selon des critères intelligents (ROI, taux de réussite, sport) au lieu d'aléatoire,
so that la qualité de mes publications soit optimisée.

## Acceptance Criteria

1. **Given** `PosterOptions.SelectionMode` configuré à `"intelligent"` (défaut : `"random"` pour rétrocompatibilité) **When** `BetSelector` effectue la sélection **Then** les pronostics sont scorés selon : ROI du tipster (40%), taux de réussite du tipster (30%), diversité de sport (20%), fraîcheur de l'événement (10%) (FR33) **And** les pronostics avec le score le plus élevé sont sélectionnés en priorité **And** le nombre sélectionné reste 5, 10 ou 15 (aléatoire comme avant)

2. **Given** `SelectionMode` configuré à `"random"` **When** le cycle s'exécute **Then** le comportement est identique au MVP (sélection aléatoire pure)

3. **Given** le mode intelligent actif **When** les logs sont écrits **Then** chaque pronostic sélectionné est logué avec son score et les critères détaillés (Step `Select`)

4. **Given** les données de ROI ou taux de réussite ne sont pas disponibles pour un tipster **When** `BetSelector` calcule le score **Then** les critères manquants sont ignorés et le poids est redistribué sur les critères disponibles

5. **Given** l'utilisateur configure `SelectionMode` via variable d'environnement **When** le service démarre **Then** `Poster__SelectionMode=intelligent` active le mode intelligent

## Tasks / Subtasks

- [x] Task 1 : Ajouter `SelectionMode` à `PosterOptions` (AC: #2, #5)
  - [x] 1.1 Ajouter `public string SelectionMode { get; set; } = "random";` dans `PosterOptions`
  - [x] 1.2 Ajouter `"SelectionMode": "random"` dans `appsettings.json` section `Poster`

- [x] Task 2 : Enrichir `PendingBet` avec les métadonnées tipster nécessaires au scoring (AC: #1, #4)
  - [x] 2.1 Ajouter à `PendingBet` les propriétés : `TipsterRoi` (decimal?), `TipsterWinRate` (decimal?), `TipsterSport` (string?), `TipsterUsername` (string?)
  - [x] 2.2 Propager ces données dans `UpcomingBetsFetcher.FetchAllAsync()` — enrichir chaque `PendingBet` avec les stats du tipster source (disponibles via `ScrapedTipster` ou `TipsterConfig`)

- [x] Task 3 : Enrichir `UpcomingBetsFetcher` pour propager les stats tipster sur chaque bet (AC: #1)
  - [x] 3.1 Modifier `FetchAllAsync` pour récupérer les stats tipster (ROI, win rate, sport) et les setter sur chaque `PendingBet` retourné
  - [x] 3.2 Utiliser `GetFreeTipstersAsync()` existant (story 11.1) pour récupérer les stats via l'API ou passer les infos du `ScrapedTipster` cache
  - [x] 3.3 Alternative plus simple : ajouter un paramètre `Dictionary<int, ScrapedTipster>` à `FetchAllAsync` ou enrichir depuis `PostingCycleService`

- [x] Task 4 : Implémenter le scoring intelligent dans `BetSelector` (AC: #1, #3, #4)
  - [x] 4.1 Créer une méthode privée `double ScoreBet(PendingBet bet, Dictionary<string, int> sportCounts)` :
    - ROI du tipster (40%) : normaliser entre 0-1 via min-max sur les candidats
    - Taux de réussite (30%) : normaliser entre 0-1 via min-max
    - Diversité de sport (20%) : pénaliser les sports surreprésentés (1 / count pour ce sport)
    - Fraîcheur événement (10%) : plus l'événement est proche, plus le score est élevé
  - [x] 4.2 Quand un critère est manquant (null), redistribuer son poids proportionnellement sur les critères disponibles
  - [x] 4.3 Modifier `SelectAsync` : si `SelectionMode == "intelligent"`, trier par score descendant au lieu de `Random.Shared.Next()`
  - [x] 4.4 Loguer chaque pronostic sélectionné avec son score et ses composantes (Step `Select`)

- [x] Task 5 : Tests unitaires (AC: #1-#5)
  - [x] 5.1 Test `SelectionMode=random` → comportement identique à l'existant (pas de régression)
  - [x] 5.2 Test `SelectionMode=intelligent` → les bets avec meilleur ROI/winrate sont sélectionnés en priorité
  - [x] 5.3 Test redistribution des poids quand ROI ou WinRate est null
  - [x] 5.4 Test diversité de sport : vérifier que le scoring pénalise la surreprésentation
  - [x] 5.5 Test fraîcheur : les événements plus proches ont un score plus élevé
  - [x] 5.6 Test logging intelligent : vérifier que les scores sont logués en mode intelligent
  - [x] 5.7 Test `PosterOptions.SelectionMode` chargé depuis config

- [x] Task 6 : `dotnet build` + `dotnet test` passent sans erreur

## Dev Notes

### Architecture et Patterns Critiques

**Modification centrale : `BetSelector.SelectAsync()`** — Le BetSelector actuel (story 3.2 + 9.1) fait :
1. Exclusion doublons via `_historyManager.LoadPublishedKeysAsync()`
2. Filtrage avancé MinOdds/MaxOdds/EventHorizonHours (story 9.1)
3. Sélection aléatoire 5/10/15

Story 11.2 ajoute un branchement APRÈS le filtrage (étape 2) et AVANT la sélection (étape 3) :
- Si `SelectionMode == "random"` → comportement actuel inchangé (`Random.Shared.Next()`)
- Si `SelectionMode == "intelligent"` → trier par score descendant, prendre les N meilleurs

**NE PAS modifier le filtrage existant** (MinOdds, MaxOdds, EventHorizonHours). Le scoring intelligent s'applique uniquement à l'étape de sélection finale.

### Données Tipster Disponibles pour le Scoring

Le `PendingBet` actuel N'A PAS les données tipster. Il faut les propager. Deux approches possibles :

**Approche A (recommandée) : Enrichir PendingBet dans PostingCycleService**
- `PostingCycleService` a déjà accès aux tipsters chargés
- Après `FetchAllAsync`, mapper chaque bet à son tipster source et ajouter ROI/WinRate/Sport
- Nécessite d'ajouter des propriétés nullable sur `PendingBet`

**Approche B : Enrichir dans UpcomingBetsFetcher**
- Passer les stats tipster en paramètre ou les récupérer via API
- Plus couplé mais tout reste dans le fetcher

Le choix entre A et B est laissé au développeur. L'important est que les données soient disponibles dans `PendingBet` quand `BetSelector.SelectAsync()` est appelé.

### Données disponibles par tipster (via ScrapedTipster ou API /tipsters)

| Propriété | Type | Scoring |
|---|---|---|
| `Roi` | decimal | Poids 40% — normaliser min-max sur les candidats |
| `BetsNumber` | int | Proxy pour le taux de réussite (WinRate) si pas d'info directe |
| `MostBetSport` | string | Poids 20% — diversité de sport |

**Note sur WinRate** : L'API `/tipsters` retourne `BetsNumber` mais pas directement un taux de réussite. Options :
1. Utiliser `BetsNumber` comme proxy (plus d'expérience = meilleur score)
2. Calculer un WinRate depuis l'API `/v1/statistics/{id}` si les données y sont (nécessite investigation)
3. Si indisponible, redistribuer le poids 30% sur les autres critères

### Normalisation Min-Max

Pour chaque critère numérique, normaliser entre 0 et 1 :
```
normalized = (value - min) / (max - min)
```
Si `max == min` (tous les candidats ont la même valeur), score = 0.5 pour ce critère.

### Fraîcheur de l'événement

Score basé sur `PendingBet.Event.Starts` :
```
freshness = 1.0 - (hoursUntilStart / maxHoursInPool)
```
Plus l'événement est proche, plus le score est élevé. Si `Event.Starts` est null, score = 0.5.

### Redistribution des poids (AC #4)

Si un critère est indisponible (null) pour un bet :
```
Poids disponibles = somme des poids des critères non-null
Score final = somme(poids_critère * score_critère) / poids_disponibles
```
Cela normalise automatiquement le score final entre 0 et 1.

### Composants Existants à Réutiliser

| Composant | Fichier | Utilisation |
|---|---|---|
| `BetSelector` | `Services/BetSelector.cs` | Modifier `SelectAsync` — ajouter branche intelligent |
| `PosterOptions` | `Configuration/PosterOptions.cs` | Ajouter `SelectionMode` |
| `PendingBet` | `Models/PendingBet.cs` | Ajouter propriétés tipster nullable |
| `PostingCycleService` | `Services/PostingCycleService.cs` | Enrichir bets avec stats tipster |
| `UpcomingBetsFetcher` | `Services/UpcomingBetsFetcher.cs` | Alternative pour enrichissement |
| `ScrapedTipster` | `Models/ScrapedTipster.cs` | Données tipster (ROI, BetsNumber, MostBetSport) |
| `BetSelectorTests` | `Tests/Services/BetSelectorTests.cs` | Étendre avec tests intelligent |

### Apprentissages Story 11.1 (Précédente)

- **DTOs enrichis** dans `ExtendedBet2InvestClient` : les `ApiTipster` privés ont été enrichis (Pro, Tier, GeneralStatistics) — les stats ROI/BetsNumber/MostBetSport sont accessibles via `GetFreeTipstersAsync()`
- **Pattern scope DI** : chaque opération dans les handlers crée son propre scope — suivre le même pattern
- **ConversationStateService** singleton : déjà enregistré dans DI — ne pas dupliquer
- **282 tests passent** avant cette story — ne casser aucun test existant

### Serilog Step

- Utiliser `Step = "Select"` (déjà utilisé dans BetSelector)
- En mode intelligent, loguer pour chaque bet sélectionné : `"[Intelligent] {Tipster} score={Score:F3} (roi={RoiScore:F2}, wr={WrScore:F2}, sport={SportScore:F2}, fresh={FreshScore:F2})"`
- Loguer le mode utilisé : `"Mode de sélection : {Mode}"`

### DI — Aucun changement

Pas de nouveau service à enregistrer. `BetSelector` est déjà Scoped. `PosterOptions` est déjà bindé.

### Risques et Pièges

1. **Ne PAS créer un nouveau service `IntelligentBetSelector`** — modifier `BetSelector` existant avec un `if/else` sur `SelectionMode`
2. **Ne PAS supprimer la sélection aléatoire** — le mode `random` doit rester le défaut
3. **Ne PAS oublier les tests de non-régression** — le comportement `random` ne doit pas changer
4. **Gestion des null** — TipsterRoi, TipsterWinRate peuvent être null → redistribuer les poids
5. **Division par zéro** — si max == min dans la normalisation, retourner 0.5

### Project Structure Notes

Fichiers à modifier :
- `src/Bet2InvestPoster/Configuration/PosterOptions.cs` (ajouter SelectionMode)
- `src/Bet2InvestPoster/Models/PendingBet.cs` (ajouter propriétés tipster)
- `src/Bet2InvestPoster/Services/BetSelector.cs` (logique scoring intelligent)
- `src/Bet2InvestPoster/Services/PostingCycleService.cs` ou `UpcomingBetsFetcher.cs` (enrichissement tipster)
- `src/Bet2InvestPoster/appsettings.json` (SelectionMode)
- `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs` (nouveaux tests)

Aucun nouveau fichier à créer.

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Epic 11 — Story 11.2]
- [Source: src/Bet2InvestPoster/Services/BetSelector.cs#SelectAsync — logique actuelle]
- [Source: src/Bet2InvestPoster/Models/PendingBet.cs — modèle à enrichir]
- [Source: src/Bet2InvestPoster/Models/ScrapedTipster.cs — données tipster disponibles]
- [Source: .bmadOutput/implementation-artifacts/11-1-commande-tipsters-update-scraping-et-suggestion-automatique.md — story précédente]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation Patterns]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

Aucune issue de debug — implémentation directe.

### Completion Notes List

- ✅ Task 1 : `SelectionMode = "random"` ajouté à `PosterOptions` et `appsettings.json`
- ✅ Task 2 : `PendingBet` enrichi avec `TipsterRoi`, `TipsterWinRate`, `TipsterSport`, `TipsterUsername` (tous `[JsonIgnore]`)
- ✅ Task 3 : `TipsterConfig` enrichi avec stats runtime (`Roi?`, `BetsNumber?`, `MostBetSport?`) capturées lors de `ResolveTipsterIdsAsync`. `UpcomingBetsFetcher` propage ces stats sur chaque `PendingBet` pendant le fetch.
- ✅ Task 4 : `BetSelector` implémente `SelectIntelligent()` + `ScoreBet()` avec normalisation min-max, redistribution des poids null (AC#4), log détaillé par bet sélectionné (AC#3). Mode `random` préservé intégralement (AC#2).
- ✅ Task 5 : 12 nouveaux tests dans `BetSelectorTests.cs` couvrant AC#1-#5 : mode random non régressif, priorité ROI, redistribution poids null, diversité sport, fraîcheur, logging.
- ✅ Task 6 : `dotnet build` ✅ 0 erreur | `dotnet test` ✅ 294/294 passés.

### File List

- `src/Bet2InvestPoster/Configuration/PosterOptions.cs`
- `src/Bet2InvestPoster/appsettings.json`
- `src/Bet2InvestPoster/Models/PendingBet.cs`
- `src/Bet2InvestPoster/Models/TipsterConfig.cs`
- `src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs`
- `src/Bet2InvestPoster/Services/UpcomingBetsFetcher.cs`
- `src/Bet2InvestPoster/Services/BetSelector.cs`
- `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs`

### Change Log

- 2026-02-25 : Implémentation story 11.2 — sélection multi-critères intelligente (ROI 40%, WinRate 30%, Sport 20%, Fraîcheur 10%) avec mode `random` préservé pour rétrocompatibilité.
