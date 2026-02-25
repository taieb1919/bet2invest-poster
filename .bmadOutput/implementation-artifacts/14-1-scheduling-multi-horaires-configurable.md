# Story 14.1: Scheduling multi-horaires configurable

Status: review

## Story

As a l'utilisateur,
I want configurer plusieurs horaires d'exécution par jour au lieu d'un seul,
so that mes pronostics soient publiés à différents moments de la journée pour couvrir plus d'événements.

## Acceptance Criteria

1. **Given** `PosterOptions.ScheduleTimes` configuré avec `["08:00", "13:00", "19:00"]` dans `appsettings.json`
   **When** le `SchedulerWorker` calcule les prochains runs
   **Then** le cycle s'exécute à chacun des 3 horaires configurés chaque jour (FR39)
   **And** chaque exécution est un cycle complet indépendant (scrape → select → publish → notify)

2. **Given** l'ancien paramètre `ScheduleTime` (string unique) est présent
   **When** le service démarre
   **Then** le système utilise `ScheduleTimes` (tableau) en priorité si défini, sinon fait un fallback sur `ScheduleTime` converti en tableau d'un élément (rétrocompatibilité)

3. **Given** `ScheduleTimes` non configuré et `ScheduleTime` non configuré
   **When** le service démarre
   **Then** la valeur par défaut est `["08:00", "13:00", "19:00"]` (3 exécutions/jour)

4. **Given** la commande `/status` envoyée
   **When** le bot répond
   **Then** tous les prochains horaires de la journée sont affichés (pas seulement le prochain)

5. **Given** la commande `/schedule` existante
   **When** l'utilisateur envoie `/schedule 08:00,13:00,19:00`
   **Then** les horaires sont mis à jour avec les nouvelles valeurs (séparées par virgule)
   **And** le bot confirme `"⏰ Horaires mis à jour : 08:00, 13:00, 19:00. Prochain run : {date/heure}."`

6. **Given** un horaire invalide dans la liste (ex: `08:00,25:00,19:00`)
   **When** le bot reçoit la commande `/schedule`
   **Then** le bot rejette la commande entière : `"❌ Horaire invalide : 25:00. Usage : /schedule HH:mm[,HH:mm,...]"`

7. **Given** le cycle s'exécute à 13:00
   **When** les doublons sont vérifiés
   **Then** les pronostics publiés à 08:00 le même jour sont inclus dans la détection de doublons (pas de republication intra-jour)

## Tasks / Subtasks

- [x] Task 1 : Étendre `PosterOptions` avec `ScheduleTimes` (AC: #2, #3)
  - [x] 1.1 Ajouter propriété `string[]? ScheduleTimes` à `PosterOptions`
  - [x] 1.2 Ajouter méthode helper `GetEffectiveScheduleTimes()` : retourne `ScheduleTimes` si non-null/non-vide, sinon `[ScheduleTime]`, sinon `["08:00", "13:00", "19:00"]`
  - [x] 1.3 Mettre à jour `appsettings.json` : ajouter `"ScheduleTimes": ["08:00", "13:00", "19:00"]`

- [x] Task 2 : Étendre `IExecutionStateService` / `ExecutionStateService` pour multi-horaires (AC: #2, #5)
  - [x] 2.1 Ajouter `string[] GetScheduleTimes()` et `void SetScheduleTimes(string[] times)` à l'interface
  - [x] 2.2 Modifier `ExecutionState` record : `string ScheduleTime` → `string[] ScheduleTimes` (défaut `["08:00", "13:00", "19:00"]`)
  - [x] 2.3 Conserver `GetScheduleTime()` / `SetScheduleTime()` pour rétrocompatibilité (déléguer vers `ScheduleTimes[0]` ou joined)
  - [x] 2.4 Mettre à jour `LoadSchedulingState()` : lire `"scheduleTimes"` (array JSON) avec fallback sur `"scheduleTime"` (string)
  - [x] 2.5 Mettre à jour `PersistSchedulingState()` : écrire `"scheduleTimes"` (array JSON)
  - [x] 2.6 Mettre à jour le constructeur : accepter `string[] defaultScheduleTimes` via `PosterOptions.GetEffectiveScheduleTimes()`

- [x] Task 3 : Refactorer `SchedulerWorker.CalculateNextRun()` pour multi-horaires (AC: #1)
  - [x] 3.1 Modifier `CalculateNextRun()` → prendre la liste depuis `_executionStateService.GetScheduleTimes()`
  - [x] 3.2 Algorithme : pour chaque horaire, calculer `todayAt` ; si passé, `tomorrowAt`. Retourner le plus proche dans le futur
  - [x] 3.3 Mettre à jour le log de démarrage dans `ExecuteAsync` pour afficher tous les horaires
  - [x] 3.4 Mettre à jour la détection de changement `_lastScheduleTime` → `_lastScheduleTimes` (comparer arrays)

- [x] Task 4 : Mettre à jour `ScheduleCommandHandler` pour multi-horaires (AC: #5, #6)
  - [x] 4.1 Parser `/schedule HH:mm[,HH:mm,...]` — split par virgule, trim chaque valeur
  - [x] 4.2 Valider chaque horaire individuellement ; rejeter la commande entière si un invalide
  - [x] 4.3 Trier les horaires et dédupliquer
  - [x] 4.4 Appeler `_stateService.SetScheduleTimes(times)`
  - [x] 4.5 Mettre à jour le message sans argument : afficher tous les horaires courants
  - [x] 4.6 Message de confirmation : `"⏰ Horaires mis à jour : 08:00, 13:00, 19:00. Prochain run : {date/heure}."`

- [x] Task 5 : Mettre à jour `FormatStatus` dans `MessageFormatter` (AC: #4)
  - [x] 5.1 Afficher les horaires configurés dans le status
  - [x] 5.2 Afficher le prochain run (déjà existant via `NextRunAt`)

- [x] Task 6 : Mettre à jour `FormatOnboardingMessage` (impact collatéral)
  - [x] 6.1 Afficher les horaires multiples au lieu d'un seul

- [x] Task 7 : Tests (AC: #1-#7)
  - [x] 7.1 Tests `SchedulerWorker` multi-horaires : exécution séquentielle 08:00 → 13:00 → 19:00 → lendemain 08:00
  - [x] 7.2 Test `CalculateNextRun` avec 3 horaires à différents moments de la journée
  - [x] 7.3 Tests `ScheduleCommandHandler` : parsing comma-separated, validation, horaire invalide
  - [x] 7.4 Tests `ExecutionStateService` : persistence array, migration string→array
  - [x] 7.5 Tests `PosterOptions.GetEffectiveScheduleTimes()` : priorité ScheduleTimes > ScheduleTime > défaut
  - [x] 7.6 Vérifier que les tests existants passent toujours (rétrocompatibilité)

- [x] Task 8 : Détection doublons intra-jour (AC: #7)
  - [x] 8.1 Vérifier que `HistoryManager.IsBetAlreadyPublished()` couvre les publications intra-jour (devrait fonctionner nativement car l'historique est vérifié à chaque cycle)

## Dev Notes

### Patterns existants à réutiliser

- **Configuration pattern** : `IOptions<PosterOptions>` injecté via DI, bind depuis `appsettings.json` section `"Poster"`
- **State persistence** : écriture atomique (write-to-temp + rename) dans `scheduling-state.json`
- **Time parsing** : `TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)`
- **Logging** : `LogContext.PushProperty("Step", "Schedule")` pour toutes les opérations scheduler
- **Test pattern** : `FakeTimeProvider` avec `Advance()`, `FakeExecutionStateService`, `FakePostingCycleService`

### Architecture clé

- `SchedulerWorker` (`Workers/SchedulerWorker.cs`) : seul `BackgroundService` pour le scheduling. Boucle : check enabled → calcule next run → attend → exécute cycle → loop
- `ExecutionStateService` (`Services/ExecutionStateService.cs`) : source de vérité pour l'état scheduling, persiste dans `scheduling-state.json`
- `ScheduleCommandHandler` (`Telegram/Commands/ScheduleCommandHandler.cs`) : commande `/schedule`
- `MessageFormatter` (`Telegram/Formatters/MessageFormatter.cs`) : formatage `/status` via `FormatStatus(ExecutionState)`
- `PosterOptions` (`Configuration/PosterOptions.cs`) : configuration avec `ScheduleTime = "08:00"` actuel

### Algorithme CalculateNextRun multi-horaires

```
Input: string[] scheduleTimes (ex: ["08:00", "13:00", "19:00"])
       DateTimeOffset now

Pour chaque time dans scheduleTimes:
    todayAt = today à time UTC
    si todayAt > now → candidat = todayAt
    sinon → candidat = todayAt + 1 jour

Retourner le candidat le plus proche (Min)
```

### Rétrocompatibilité scheduling-state.json

Format actuel :
```json
{"schedulingEnabled": true, "scheduleTime": "08:00"}
```

Nouveau format :
```json
{"schedulingEnabled": true, "scheduleTimes": ["08:00", "13:00", "19:00"]}
```

Migration : `LoadSchedulingState()` lit `scheduleTimes` (array) d'abord, fallback sur `scheduleTime` (string) converti en `[scheduleTime]`.

### Détection doublons intra-jour

`HistoryManager` vérifie les `betId` dans `history.json` qui contient toutes les publications (avec timestamps). Les publications de 08:00 sont déjà dans l'historique quand le cycle de 13:00 s'exécute → **aucun changement nécessaire** dans `HistoryManager`.

### Project Structure Notes

- Pas de nouveaux fichiers à créer — modifications uniquement sur fichiers existants
- Pattern DI inchangé : services en Scoped, `ExecutionStateService` en Singleton

### Fichiers à modifier

| Fichier | Changement |
|---|---|
| `src/Bet2InvestPoster/Configuration/PosterOptions.cs` | Ajouter `ScheduleTimes`, `GetEffectiveScheduleTimes()` |
| `src/Bet2InvestPoster/Services/IExecutionStateService.cs` | Ajouter `GetScheduleTimes()`, `SetScheduleTimes()`, modifier `ExecutionState` |
| `src/Bet2InvestPoster/Services/ExecutionStateService.cs` | Implémenter multi-horaires, migration persistence |
| `src/Bet2InvestPoster/Workers/SchedulerWorker.cs` | Refactorer `CalculateNextRun()`, logs multi-horaires |
| `src/Bet2InvestPoster/Telegram/Commands/ScheduleCommandHandler.cs` | Parser comma-separated, multi-validation |
| `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` | `FormatStatus` et `FormatOnboardingMessage` multi-horaires |
| `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` | Si signature change |
| `src/Bet2InvestPoster/appsettings.json` | Ajouter `ScheduleTimes` |
| `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs` | Tests multi-horaires |
| `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs` | Adapter si FakeExecutionStateService change |

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase4.md#Epic 14] — Définition epic et story
- [Source: .bmadOutput/planning-artifacts/architecture.md#Scheduling] — Pattern SchedulerWorker
- [Source: src/Bet2InvestPoster/Workers/SchedulerWorker.cs] — Implémentation actuelle
- [Source: src/Bet2InvestPoster/Services/ExecutionStateService.cs] — Persistence scheduling-state.json
- [Source: .bmadOutput/implementation-artifacts/7-3-commande-schedule-configuration-horaire-via-telegram.md] — Story d'origine /schedule

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6

### Debug Log References

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created
- Détection doublons intra-jour fonctionne nativement (HistoryManager vérifie betId dans history.json)
- Rétrocompatibilité critique : `ScheduleTime` (string) doit rester fonctionnel pour les utilisateurs existants
- FakeExecutionStateService dans les tests doit être étendu pour supporter `GetScheduleTimes()` / `SetScheduleTimes()`

### File List

**Production (src/Bet2InvestPoster/)**

- `Configuration/PosterOptions.cs` — Ajout `ScheduleTimes` (string[]), `GetEffectiveScheduleTimes()`, `DefaultScheduleTimes`
- `Services/ExecutionStateService.cs` — Ajout `GetScheduleTimes()`, `SetScheduleTimes()`, migration old format, persistence JSON
- `Services/IExecutionStateService.cs` — Ajout `GetScheduleTimes()`, `SetScheduleTimes()` à l'interface
- `Workers/SchedulerWorker.cs` — `CalculateNextRun()` itère sur multi-horaires, détection changement d'horaires, skip horaires invalides
- `Telegram/Commands/ScheduleCommandHandler.cs` — Support multi-horaires via virgule, tri chronologique, dédupliquer, validation
- `Telegram/Formatters/MessageFormatter.cs` — `FormatStatus()` affiche tous les horaires, `FormatOnboardingMessage()` multi-horaires
- `Telegram/Formatters/IMessageFormatter.cs` — Signature `FormatOnboardingMessage(bool, int, string[])` multi-horaires
- `Program.cs` — Validation `ScheduleTimes` au démarrage (fast-fail), injection `GetEffectiveScheduleTimes()`
- `appsettings.json` — Ajout `ScheduleTimes: ["08:00","13:00","19:00"]`
- `Services/NotificationService.cs` — Adaptations story 13.1 (non liées à 14.1)
- `Services/OnboardingService.cs` — Passage `GetScheduleTimes()` au formatter
- `Services/BetPublisher.cs` — Résidus story 13.1 (non liés à 14.1)
- `Services/BetSelector.cs` — Résidus story 13.1 (non liés à 14.1)
- `Services/IBetPublisher.cs` — Résidus story 13.1 (non liés à 14.1)
- `Services/IBetSelector.cs` — Résidus story 13.1 (non liés à 14.1)
- `Services/INotificationService.cs` — Résidus story 13.1 (non liés à 14.1)
- `Services/IPostingCycleService.cs` — Résidus story 13.1 (non liés à 14.1)

**Tests (tests/Bet2InvestPoster.Tests/)**

- `MultiScheduleTests.cs` — Tests story 14.1 : PosterOptionsScheduleTimesTests, ExecutionStateServiceMultiScheduleTests, SchedulerWorkerMultiScheduleTests, ScheduleCommandHandlerMultiTests, MessageFormatterMultiScheduleTests
- `Helpers/FakeExecutionStateService.cs` — NOUVEAU : classe partagée remplaçant les 10 implémentations locales dupliquées
- `Services/Bet2InvestHealthCheckTests.cs` — Migration vers FakeExecutionStateService partagée
- `Services/BetPublisherTests.cs` — Adaptations story 13.1
- `Services/BetSelectorTests.cs` — Adaptations story 13.1
- `Services/OnboardingServiceTests.cs` — Migration vers FakeExecutionStateService partagée
- `Services/PostingCycleServiceNotificationTests.cs` — Adaptations story 13.1
- `Services/PostingCycleServiceTests.cs` — Migration vers FakeExecutionStateService partagée
- `Telegram/Commands/ScheduleCommandHandlerTests.cs` — Migration vers FakeExecutionStateService partagée
- `Telegram/Commands/StartCommandHandlerTests.cs` — Migration vers FakeExecutionStateService partagée
- `Telegram/Commands/StatusCommandHandlerTests.cs` — Migration vers FakeExecutionStateService partagée
- `Telegram/Commands/StopCommandHandlerTests.cs` — Migration vers FakeExecutionStateService partagée
- `Workers/SchedulerWorkerPollyTests.cs` — Migration vers FakeExecutionStateService partagée
- `Workers/SchedulerWorkerTests.cs` — Migration vers FakeExecutionStateService partagée, suppression DynamicScheduleExecutionStateService
