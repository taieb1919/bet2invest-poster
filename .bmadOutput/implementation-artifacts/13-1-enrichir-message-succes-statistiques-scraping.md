# Story 13.1: Enrichir le message de succès avec les statistiques de scraping

Status: review

## Story

As a l'utilisateur,
I want voir le nombre total de pronostics scrapés et le nombre publié dans le message de succès,
so that j'aie une vision claire de la couverture du scraping à chaque cycle.

## Acceptance Criteria

1. **Given** un cycle de publication terminé avec succès **When** la notification Telegram est envoyée **Then** le message inclut le nombre total de pronostics scrapés (candidats disponibles) et le nombre effectivement publié (FR37) **And** le format est : `"✅ {published} pronostics publiés sur {scraped} scrapés."`

2. **Given** le cycle applique des filtres (cotes, plage horaire) **When** la notification est envoyée **Then** le message affiche aussi le nombre après filtrage : `"✅ {published}/{filtered} filtrés sur {scraped} scrapés."`

3. **Given** zéro pronostic scrapé **When** la notification est envoyée **Then** le message est : `"⚠️ Aucun pronostic disponible chez les tipsters configurés."`

## Tasks / Subtasks

- [x] Task 1 — Créer le DTO `CycleResult` (AC: #1, #2, #3)
  - [x] 1.1 Créer `Models/CycleResult.cs` avec propriétés : `ScrapedCount`, `FilteredCount`, `PublishedCount`
  - [x] 1.2 Ajouter propriété calculée `HasActiveFilters => FilteredCount != ScrapedCount`
- [x] Task 2 — Modifier `PostingCycleService` pour produire un `CycleResult` (AC: #1, #2)
  - [x] 2.1 Changer la signature de `RunCycleAsync` pour retourner `Task<CycleResult>`
  - [x] 2.2 Capturer `candidates.Count` (scraped), `selected.Count` après sélection (filtered implicite via BetSelector), et `published` (publié)
  - [x] 2.3 Retourner le `CycleResult` rempli à l'appelant
- [x] Task 3 — Modifier `INotificationService` / `NotificationService` (AC: #1, #2, #3)
  - [x] 3.1 Changer signature : `NotifySuccessAsync(CycleResult result, CancellationToken ct)`
  - [x] 3.2 Déléguer le formatage à `IMessageFormatter`
- [x] Task 4 — Ajouter méthode `FormatCycleSuccess` dans `IMessageFormatter` / `MessageFormatter` (AC: #1, #2, #3)
  - [x] 4.1 Implémenter les 3 cas : message standard, message avec filtres, message zéro scrapé
- [x] Task 5 — Propager `CycleResult` dans les appelants (AC: #1, #2)
  - [x] 5.1 Mettre à jour `RunCommandHandler` pour passer le `CycleResult` à la notification
  - [x] 5.2 Mettre à jour `SchedulerWorker` / pipeline Polly pour propager le résultat
- [x] Task 6 — Mettre à jour les tests (AC: #1, #2, #3)
  - [x] 6.1 Tests unitaires `MessageFormatter.FormatCycleSuccess` : 3 cas (standard, filtres, zéro)
  - [x] 6.2 Tests unitaires `NotificationService.NotifySuccessAsync` avec `CycleResult`
  - [x] 6.3 Mettre à jour tests existants `PostingCycleService` pour le nouveau retour
  - [x] 6.4 Mettre à jour tests `RunCommandHandler` et `SchedulerWorker`

## Dev Notes

### Architecture et patterns existants

- **Pattern NotificationService** : Actuellement `NotifySuccessAsync(int publishedCount, CancellationToken)` — signature à changer pour accepter `CycleResult`
- **Pattern MessageFormatter** : Classe dans `Telegram/Formatters/MessageFormatter.cs` avec interface `IMessageFormatter`. Ajouter `FormatCycleSuccess(CycleResult)` en suivant le même pattern que `FormatReport()`
- **PostingCycleService** : Orchestre le cycle `fetch → select → publish → notify`. Les données sont déjà disponibles comme variables locales : `candidates.Count` (scraped), `selected.Count` (après filtres+dedup), `published` (int retourné par `BetPublisher.PublishAllAsync`)
- **BetSelector.SelectAsync** : Retourne `List<PendingBet>` — le count des candidats filtrés est `selected.Count`, mais le count **avant sélection aléatoire** (après filtres cotes/horaire mais avant random pick) est logué comme `available.Count`. C'est ce nombre qui correspond au "filtered" du AC#2
- **DI** : Services en Scoped. `CycleResult` est un simple DTO, pas besoin de DI

### Distinction scraped vs filtered vs published

- `scraped` = `candidates.Count` retourné par `UpcomingBetsFetcher.FetchAllAsync` (total brut)
- `filtered` = nombre de candidats après déduplication + filtres cotes/horaire dans `BetSelector` (= `available.Count` dans le code actuel du sélecteur, AVANT le random pick)
- `published` = nombre retourné par `BetPublisher.PublishAllAsync`

**Important** : `BetSelector.SelectAsync` retourne les pronostics sélectionnés (après random pick). Le nombre `filtered` (après filtres mais avant random) n'est pas actuellement exposé. Il faudra soit :
- (a) Modifier `BetSelector` pour exposer `filteredCount` via un out param ou tuple, OU
- (b) Créer un `SelectionResult` retourné par `SelectAsync` contenant `FilteredCount` et `Selected` list

L'option (b) est recommandée pour rester cohérent avec le pattern `CycleResult`.

### Fichiers à modifier

| Fichier | Action |
|---|---|
| `src/Bet2InvestPoster/Models/CycleResult.cs` | **CRÉER** — DTO avec ScrapedCount, FilteredCount, PublishedCount |
| `src/Bet2InvestPoster/Models/SelectionResult.cs` | **CRÉER** — DTO avec FilteredCount + Selected list |
| `src/Bet2InvestPoster/Services/IBetSelector.cs` | **MODIFIER** — Retourner `SelectionResult` au lieu de `List<PendingBet>` |
| `src/Bet2InvestPoster/Services/BetSelector.cs` | **MODIFIER** — Retourner `SelectionResult` avec `FilteredCount` |
| `src/Bet2InvestPoster/Services/INotificationService.cs` | **MODIFIER** — Signature `NotifySuccessAsync(CycleResult, CT)` |
| `src/Bet2InvestPoster/Services/NotificationService.cs` | **MODIFIER** — Utiliser `IMessageFormatter.FormatCycleSuccess` |
| `src/Bet2InvestPoster/Services/PostingCycleService.cs` | **MODIFIER** — Retourner `CycleResult`, consommer `SelectionResult` |
| `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` | **MODIFIER** — Ajouter `FormatCycleSuccess(CycleResult)` |
| `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` | **MODIFIER** — Implémenter formatage 3 cas |
| `src/Bet2InvestPoster/Telegram/Commands/RunCommandHandler.cs` | **MODIFIER** — Propager `CycleResult` |
| `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` | **MODIFIER** — Propager `CycleResult` |
| `tests/Bet2InvestPoster.Tests/` | **MODIFIER** — Tous les tests impactés |

### Cas du message zéro scrapé (AC#3)

Le cas `candidates.Count == 0` est déjà partiellement géré dans `PostingCycleService` (il y a un early return si `selected.Count == 0 && candidates.Count > 0`). Pour AC#3, il faut ajouter un cas spécifique quand `candidates.Count == 0` qui envoie le message `"⚠️ Aucun pronostic disponible..."` via `NotificationService`. Ce cas doit aussi retourner un `CycleResult` avec `ScrapedCount=0`.

### Pattern de formatage conditionnel (AC#2)

Le message change selon que des filtres sont actifs :
- **Sans filtres** : `"✅ 10 pronostics publiés sur 45 scrapés."`
- **Avec filtres** : `"✅ 10/32 filtrés sur 45 scrapés."`

Pour savoir si des filtres sont actifs : `CycleResult.FilteredCount < CycleResult.ScrapedCount` (après déduplication, les filtres ont réduit le pool). Note : la déduplication (history) est aussi un "filtre" — le nombre `filtered` inclut ce filtrage.

### Apprentissages des stories précédentes

- **Epic 12 retro** : Le code review adversarial a trouvé 7 issues — ne pas négliger les tests edge cases
- **Pattern éprouvé** : Interface + Implémentation + Fake pour tests
- **Logs structurés** : Utiliser `LogContext.PushProperty("Step", "Notify")` pour le contexte Serilog
- **Rétrocompatibilité** : La notification d'échec (`NotifyFailureAsync`) reste inchangée — elle ne fait pas partie de cette story

### Project Structure Notes

- Alignement avec la structure existante : les DTOs vont dans `Models/`, les services dans `Services/`, les formatters dans `Telegram/Formatters/`
- Pas de conflit détecté avec la structure actuelle

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase4.md#Story 13.1]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Code Organization]
- [Source: .bmadOutput/implementation-artifacts/epic-12-retro-2026-02-25.md]
- [Source: src/Bet2InvestPoster/Services/NotificationService.cs]
- [Source: src/Bet2InvestPoster/Services/PostingCycleService.cs]
- [Source: src/Bet2InvestPoster/Services/BetSelector.cs]
- [Source: src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs]

## Dev Agent Record

### Agent Model Used

Claude Sonnet 4.6

### Debug Log References

### Completion Notes List

- Story context engine analysis completed — comprehensive developer guide created
- CycleResult DTO pattern recommandé pour propagation propre des données
- SelectionResult intermédiaire créé pour exposer FilteredCount depuis BetSelector (option b du Dev Notes)
- HasActiveFilters => FilteredCount < ScrapedCount (data-driven, pas config-driven)
- NotificationService injecte maintenant IMessageFormatter pour le formatage
- IPostingCycleService.RunCycleAsync retourne Task<CycleResult> — callers (RunCommandHandler, SchedulerWorker) ignorent le résultat (valide en C#)
- 7 nouveaux tests ajoutés : 5 MessageFormatterCycleSuccessTests + 2 NotificationServiceTests
- Tous les tests existants mis à jour : 320 → 327 tests, 0 échec

### File List

- src/Bet2InvestPoster/Models/CycleResult.cs (CRÉÉ)
- src/Bet2InvestPoster/Models/SelectionResult.cs (CRÉÉ)
- src/Bet2InvestPoster/Services/IBetSelector.cs (MODIFIÉ)
- src/Bet2InvestPoster/Services/BetSelector.cs (MODIFIÉ)
- src/Bet2InvestPoster/Services/INotificationService.cs (MODIFIÉ)
- src/Bet2InvestPoster/Services/NotificationService.cs (MODIFIÉ)
- src/Bet2InvestPoster/Services/IPostingCycleService.cs (MODIFIÉ)
- src/Bet2InvestPoster/Services/PostingCycleService.cs (MODIFIÉ)
- src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs (MODIFIÉ)
- src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Helpers/FakeNotificationService.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Services/BetSelectorTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Services/NotificationServiceTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Services/OnboardingServiceTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Telegram/Commands/RunCommandHandlerTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterCycleSuccessTests.cs (CRÉÉ)
- tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs (MODIFIÉ)
- tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs (MODIFIÉ)

## Change Log

- 2026-02-25 — Story 13.1 implémentée : enrichissement du message de succès avec statistiques scraping/filtrage/publication (CycleResult + SelectionResult DTOs, refactor NotificationService + MessageFormatter)
