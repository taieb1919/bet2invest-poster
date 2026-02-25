# Story 13.2: Détail des pronostics publiés dans le message

Status: review

## Story

As a l'utilisateur,
I want voir le détail de chaque pronostic publié (match, cote, tipster) dans le message Telegram,
so that je sache exactement ce qui a été publié sans aller sur bet2invest.

## Acceptance Criteria

1. **Given** un cycle de publication terminé avec succès **When** la notification Telegram est envoyée **Then** le message inclut la liste des pronostics publiés, chacun avec : description du match, cote, nom du tipster (FR38) **And** le format de chaque ligne est : `"• {matchDescription} — {odds} ({tipsterName})"`

2. **Given** plus de 15 pronostics publiés **When** le message est formaté **Then** seuls les 15 premiers sont affichés avec une note `"... et {n} autres"`

3. **Given** un pronostic sans description de match disponible **When** le message est formaté **Then** la ligne affiche `"• (sans description) — {odds} ({tipsterName})"`

## Tasks / Subtasks

- [x] Task 1 — Ajouter `PublishedBets` dans `CycleResult` (AC: #1)
  - [x] 1.1 Ajouter propriété `IReadOnlyList<PendingBet> PublishedBets` dans `CycleResult.cs`
  - [x] 1.2 Initialiser à `Array.Empty<PendingBet>()` par défaut (rétrocompatibilité)
- [x] Task 2 — Modifier `BetPublisher.PublishAllAsync` pour retourner la liste publiée (AC: #1)
  - [x] 2.1 Changer `IBetPublisher` : retour `Task<IReadOnlyList<PendingBet>>` au lieu de `Task<int>`
  - [x] 2.2 Dans `BetPublisher.PublishAllAsync`, collecter les bets publiés avec succès dans une `List<PendingBet>` et la retourner
- [x] Task 3 — Propager `PublishedBets` dans `PostingCycleService` (AC: #1)
  - [x] 3.1 Adapter `RunCycleAsync` : utiliser `publishedBets.Count` pour `PublishedCount` et passer la liste dans `CycleResult.PublishedBets`
- [x] Task 4 — Enrichir `FormatCycleSuccess` dans `MessageFormatter` (AC: #1, #2, #3)
  - [x] 4.1 Après la ligne résumé existante, ajouter le détail de chaque bet publié
  - [x] 4.2 Format par ligne : `"• {Home} vs {Away} — {Price:F2} ({TipsterUsername})"`
  - [x] 4.3 Si `bet.Event == null` : `"• (sans description) — {Price:F2} ({TipsterUsername})"`
  - [x] 4.4 Tronquer à 15 lignes max avec `"... et {n} autres"` si > 15
- [x] Task 5 — Mettre à jour les tests (AC: #1, #2, #3)
  - [x] 5.1 Tests `MessageFormatter.FormatCycleSuccess` : cas avec détails bets (3-5 bets), cas > 15 bets troncature, cas Event null
  - [x] 5.2 Tests `BetPublisher` : vérifier retour `IReadOnlyList<PendingBet>` (liste des publiés, pas le count)
  - [x] 5.3 Mettre à jour tous les tests existants qui utilisent `BetPublisher` mock (retour `int` → `IReadOnlyList<PendingBet>`)
  - [x] 5.4 Mettre à jour tests `PostingCycleService` pour vérifier `CycleResult.PublishedBets`
  - [x] 5.5 Mettre à jour `FakeNotificationService` si nécessaire (inchangé — CycleResult passe à travers)

## Dev Notes

### Architecture et patterns existants

- **CycleResult** (`Models/CycleResult.cs`) : record avec `ScrapedCount`, `FilteredCount`, `PublishedCount`, `FiltersWereActive`. Ajouter `PublishedBets` ici — c'est le DTO qui traverse NotificationService → MessageFormatter.
- **MessageFormatter.FormatCycleSuccess(CycleResult)** : Déjà implémenté en story 13.1 pour le résumé (scraped/filtered/published). Enrichir cette méthode pour ajouter le détail des bets **après** la ligne résumé existante.
- **BetPublisher.PublishAllAsync** : Retourne actuellement `Task<int>`. Le code interne collecte déjà chaque bet publié avec succès (boucle foreach + `published++`). Il suffit de collecter dans une `List<PendingBet>` au lieu de compter.
- **PostingCycleService.RunCycleAsync** : Ligne 94 appelle `_betPublisher.PublishAllAsync(selectionResult.Selected, ct)` et stocke le résultat dans `var published` (int). Adapter pour recevoir la liste.
- **NotificationService.NotifySuccessAsync(CycleResult, ct)** : Pas de changement — il délègue déjà à `IMessageFormatter.FormatCycleSuccess(result)`.
- **IMessageFormatter.FormatCycleSuccess(CycleResult)** : Pas de changement de signature — CycleResult est enrichi.

### Données PendingBet disponibles pour le formatage

Les champs utiles dans `PendingBet` (hérite `SettledBet` du scraper) :
- `Event.Home` / `Event.Away` → description du match (peut être null si Event == null)
- `Price` (decimal) → cote du bet
- `TipsterUsername` (string) → nom du tipster (enrichi par BetSelector depuis ScrapedTipster)

**Pattern déjà utilisé dans BetPublisher** (ligne 104-106) :
```csharp
var description = bet.Event != null
    ? $"{bet.Event.Home} vs {bet.Event.Away}"
    : $"Bet#{bet.Id}";
```
→ Réutiliser cette logique dans MessageFormatter, mais avec `"(sans description)"` au lieu de `Bet#id` (AC#3).

### Détail du format attendu (exemple concret)

Message complet attendu :
```
✅ 3 pronostics publiés sur 45 scrapés.

• Arsenal vs Man City — 2.50 (john_tipster)
• Real Madrid vs Barcelona — 1.85 (alice_pro)
• PSG vs Lyon — 3.10 (bet_master)
```

Avec filtres actifs :
```
✅ 3/12 filtrés sur 45 scrapés.

• Arsenal vs Man City — 2.50 (john_tipster)
• (sans description) — 1.85 (alice_pro)
• PSG vs Lyon — 3.10 (bet_master)
```

Avec troncature (> 15) :
```
✅ 18 pronostics publiés sur 50 scrapés.

• Arsenal vs Man City — 2.50 (john_tipster)
[... 14 autres lignes ...]
... et 3 autres
```

### Cas zéro scrapé / zéro publié

- Si `ScrapedCount == 0` → message `"⚠️ Aucun pronostic disponible..."` (pas de détail, inchangé story 13.1)
- Si `PublishedCount == 0` avec filtres → message `"⚠️ 0/X filtrés..."` (pas de détail)
- Le détail n'est ajouté que si `PublishedBets.Count > 0`

### Changement IBetPublisher — Impact sur les mocks

Le changement de `Task<int>` → `Task<IReadOnlyList<PendingBet>>` impacte :
- `PostingCycleServiceTests.cs` — mock `IBetPublisher`
- `PostingCycleServiceNotificationTests.cs` — mock `IBetPublisher`
- `SchedulerWorkerTests.cs` / `SchedulerWorkerPollyTests.cs` — si mock `IBetPublisher` indirectement
- `RunCommandHandlerTests.cs` — si mock `IPostingCycleService` (pas `IBetPublisher` directement)

Pattern mock à adapter :
```csharp
// Avant
mockPublisher.Setup(p => p.PublishAllAsync(It.IsAny<IReadOnlyList<PendingBet>>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(3);

// Après
var publishedBets = new List<PendingBet> { bet1, bet2, bet3 };
mockPublisher.Setup(p => p.PublishAllAsync(It.IsAny<IReadOnlyList<PendingBet>>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(publishedBets);
```

### Apprentissages story 13.1

- **7 issues détectées en code review** : ne pas négliger les edge cases dans les tests
- **SelectionResult pattern** : la story 13.1 a créé `SelectionResult` pour exposer `FilteredCount` — même approche ici en enrichissant `CycleResult` plutôt que de créer un nouveau DTO
- **FiltersWereActive** : positionné explicitement par PostingCycleService selon PosterOptions (pas calculé)
- **FakeNotificationService** dans les tests helpers — peut nécessiter mise à jour si `CycleResult` change

### Fichiers à modifier

| Fichier | Action |
|---|---|
| `src/Bet2InvestPoster/Models/CycleResult.cs` | **MODIFIER** — Ajouter `PublishedBets` |
| `src/Bet2InvestPoster/Services/IBetPublisher.cs` | **MODIFIER** — Retour `Task<IReadOnlyList<PendingBet>>` |
| `src/Bet2InvestPoster/Services/BetPublisher.cs` | **MODIFIER** — Retourner liste au lieu de count |
| `src/Bet2InvestPoster/Services/PostingCycleService.cs` | **MODIFIER** — Propager `PublishedBets` dans `CycleResult` |
| `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` | **MODIFIER** — Enrichir `FormatCycleSuccess` avec détails bets |
| `tests/.../Services/BetPublisherTests.cs` | **MODIFIER** — Adapter assertions retour |
| `tests/.../Services/PostingCycleServiceTests.cs` | **MODIFIER** — Mock IBetPublisher retour liste |
| `tests/.../Services/PostingCycleServiceNotificationTests.cs` | **MODIFIER** — Mock IBetPublisher retour liste |
| `tests/.../Telegram/Formatters/MessageFormatterCycleSuccessTests.cs` | **MODIFIER** — Ajouter tests détails bets |
| `tests/.../Workers/SchedulerWorkerTests.cs` | **MODIFIER** — Si mock IBetPublisher |
| `tests/.../Workers/SchedulerWorkerPollyTests.cs` | **MODIFIER** — Si mock IBetPublisher |
| `tests/.../Helpers/FakeNotificationService.cs` | **VÉRIFIER** — Adapter si nécessaire |

### Project Structure Notes

- Pas de nouveaux fichiers à créer — uniquement des modifications
- Le changement est contenu dans les couches existantes : Models → Services → Formatters → Tests

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase4.md#Story 13.2]
- [Source: src/Bet2InvestPoster/Models/CycleResult.cs]
- [Source: src/Bet2InvestPoster/Services/IBetPublisher.cs]
- [Source: src/Bet2InvestPoster/Services/BetPublisher.cs#L32-L131]
- [Source: src/Bet2InvestPoster/Services/PostingCycleService.cs#L94-L111]
- [Source: src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs#L225-L237]
- [Source: .bmadOutput/implementation-artifacts/13-1-enrichir-message-succes-statistiques-scraping.md]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6

### Debug Log References

### Completion Notes List

- Story context engine analysis completed — comprehensive developer guide created
- CycleResult enrichi avec PublishedBets (pas de nouveau DTO)
- IBetPublisher retour change : int → IReadOnlyList<PendingBet> — impact mocks significatif
- FormatCycleSuccess enrichi : détail bets après résumé, troncature à 15, fallback "(sans description)"
- NotificationService et IMessageFormatter inchangés (CycleResult passe à travers)
- FakeNotificationService inchangé — CycleResult est passé tel quel
- SchedulerWorkerTests/PollyTests inchangés — mockent IPostingCycleService, pas IBetPublisher
- 336 tests passés (0 régressions) — build 0 warning

### Change Log

- 2026-02-25 : Implémentation story 13.2 — PublishedBets dans CycleResult, IBetPublisher retourne liste, FormatCycleSuccess avec détail des bets (troncature 15, sans description fallback)
- 2026-02-25 : Corrections code review — TipsterUsername/Event.Home/Away null guards, PublishedCount dérivé de PublishedBets.Count, constante MaxDisplayedBets, suppression hack LastSuccessCount, File List complétée

### File List

**Fichiers principaux story 13.2 :**
- `src/Bet2InvestPoster/Models/CycleResult.cs`
- `src/Bet2InvestPoster/Services/IBetPublisher.cs`
- `src/Bet2InvestPoster/Services/BetPublisher.cs`
- `src/Bet2InvestPoster/Services/PostingCycleService.cs`
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs`
- `tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterCycleSuccessTests.cs`

**Fichiers impactés transversalement (13.1 + 13.2 — signatures d'interface refactorisées) :**
- `src/Bet2InvestPoster/Services/IBetSelector.cs` — retour `SelectionResult` (13.1)
- `src/Bet2InvestPoster/Services/BetSelector.cs` — implémentation `SelectionResult` (13.1)
- `src/Bet2InvestPoster/Services/INotificationService.cs` — `NotifySuccessAsync(CycleResult)` (13.1)
- `src/Bet2InvestPoster/Services/NotificationService.cs` — dépendance `IMessageFormatter` + refacto (13.2)
- `src/Bet2InvestPoster/Services/IPostingCycleService.cs` — retour `Task<CycleResult>` (13.1)
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` — ajout `FormatCycleSuccess` (13.1)
- `tests/Bet2InvestPoster.Tests/Helpers/FakeNotificationService.cs` — signature `CycleResult`
- `tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs` — adaptation `.Selected` (13.1)
- `tests/Bet2InvestPoster.Tests/Services/OnboardingServiceTests.cs` — adaptation mock
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs` — adaptation mock
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs` — adaptation mock
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` — mise à jour statut
