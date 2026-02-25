# Story 15.2: Enregistrement des commandes via setMyCommands

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want que les commandes du bot apparaissent dans le menu natif de Telegram (bouton "/" ou autocomplétion),
so that je puisse découvrir et saisir les commandes facilement sans les mémoriser.

## Acceptance Criteria

1. **Given** le `TelegramBotService` démarre
   **When** le bot se connecte à l'API Telegram
   **Then** `SetMyCommandsAsync` est appelé avec la liste complète des commandes et leurs descriptions (FR41)
   **And** les commandes sont enregistrées une seule fois au démarrage

2. **Given** la liste des commandes enregistrées
   **When** l'utilisateur tape "/" dans le chat Telegram
   **Then** le menu natif Telegram affiche toutes les commandes avec leur description

3. **Given** le `SetMyCommandsAsync` échoue (API Telegram indisponible)
   **When** l'erreur est détectée
   **Then** le bot logue l'erreur avec le Step `Notify` et continue son démarrage normalement (non bloquant)
   **And** les commandes restent utilisables manuellement

## Tasks / Subtasks

- [x] Task 1 : Appeler `SetMyCommandsAsync` au démarrage du bot (AC: #1)
  - [x] 1.1 Dans `TelegramBotService.ExecuteAsync()`, ajouter l'appel `SetMyCommandsAsync` après le démarrage du polling
  - [x] 1.2 Construire la liste des `BotCommand` avec les 9 commandes et leurs descriptions
  - [x] 1.3 L'appel doit être non-bloquant (pattern `_ = Task.Run(async () => ...)` comme le onboarding existant)

- [x] Task 2 : Gestion d'erreur non-bloquante (AC: #3)
  - [x] 2.1 Encadrer l'appel dans un try/catch
  - [x] 2.2 Loguer l'erreur avec `LogContext.PushProperty("Step", "Notify")` et `_logger.LogWarning`
  - [x] 2.3 Ne pas empêcher le démarrage du bot en cas d'échec

- [x] Task 3 : Tests (AC: #1, #3)
  - [x] 3.1 Test que `SetMyCommandsAsync` est appelé au démarrage avec les 9 commandes attendues
  - [x] 3.2 Test que si `SetMyCommandsAsync` lève une exception, le bot continue son exécution normalement
  - [x] 3.3 Test que les descriptions des commandes correspondent à celles du `/help`

## Dev Notes

### Pattern d'exécution non-bloquante existant

Dans `TelegramBotService.ExecuteAsync()`, un pattern de tâche asynchrone non-bloquante est déjà utilisé pour le onboarding (lignes 62-72). **Utiliser le même pattern** pour `SetMyCommandsAsync` :

```csharp
_ = Task.Run(async () =>
{
    try
    {
        using (LogContext.PushProperty("Step", "Notify"))
        {
            // appel SetMyCommandsAsync ici
        }
    }
    catch (Exception ex)
    {
        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogWarning(ex, "Échec de l'enregistrement des commandes Telegram");
        }
    }
}, stoppingToken);
```

### Liste des commandes à enregistrer

Les 9 commandes doivent correspondre exactement à celles listées dans `HelpCommandHandler.HelpMessage` :

| Commande | Description |
|---|---|
| `run` | Exécuter un cycle de publication |
| `status` | Afficher l'état du système |
| `start` | Activer le scheduling automatique |
| `stop` | Suspendre le scheduling automatique |
| `history` | Historique des publications récentes |
| `schedule` | Configurer les horaires d'exécution |
| `tipsters` | Gérer la liste des tipsters |
| `report` | Tableau de bord des performances |
| `help` | Afficher cette aide |

**IMPORTANT** : Les `BotCommand.Command` ne doivent PAS contenir le "/" — Telegram l'ajoute automatiquement.

### API Telegram.Bot 22.9.0

```csharp
await _botClient.SetMyCommands(
    commands: new[]
    {
        new BotCommand { Command = "run", Description = "Exécuter un cycle de publication" },
        // ... autres commandes
    },
    cancellationToken: stoppingToken
);
```

Note : la méthode s'appelle `SetMyCommands` (sans suffixe Async) dans Telegram.Bot 22.9.0.

Classe `BotCommand` disponible dans namespace `Telegram.Bot.Types` (Telegram.Bot 22.9.0).

### Fichiers à modifier

- **`src/Bet2InvestPoster/Telegram/TelegramBotService.cs`** — Ajouter l'appel `SetMyCommandsAsync` dans `ExecuteAsync()` après le polling

### Fichiers à créer

- **`tests/Bet2InvestPoster.Tests/Telegram/TelegramBotServiceSetCommandsTests.cs`** — Tests pour `SetMyCommandsAsync`

### Logging

Utiliser le pattern Serilog structuré existant :
```csharp
using (LogContext.PushProperty("Step", "Notify"))
{
    _logger.LogInformation("Commandes du bot enregistrées via setMyCommands ({Count} commandes)", commands.Length);
}
```

### Sécurité

Aucun impact sécurité — `SetMyCommandsAsync` ne modifie que l'affichage du menu, pas le comportement d'autorisation.

### Intelligence story précédente (15.1)

- Story 15.1 a créé `HelpCommandHandler` avec la liste des commandes en dur
- La liste des commandes dans `SetMyCommandsAsync` doit être **cohérente** avec celle du `/help`
- Pattern de handler simple sans dépendance métier — pas de modification nécessaire pour 15.2
- 375 tests passés après story 15.1, 0 échec

### Git Intelligence

Derniers commits pertinents :
- `4157861` fix(telegram): corriger toutes les issues code review story 15.1
- `74f3299` fix(scheduling): corriger toutes les issues code review story 14.1

### Project Structure Notes

- Modification minimale : un seul fichier production (`TelegramBotService.cs`)
- Un nouveau fichier test
- Pas de nouveau service, pas de nouvelle interface
- Aligné avec la structure existante `src/Bet2InvestPoster/Telegram/`

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase4.md#Epic 15] — Définition epic et story 15.2
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs] — Service principal du bot, pattern non-bloquant
- [Source: src/Bet2InvestPoster/Telegram/Commands/HelpCommandHandler.cs] — Liste des commandes existantes
- [Source: .bmadOutput/implementation-artifacts/15-1-commande-help-liste-des-commandes-disponibles.md] — Story précédente, context

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- Telegram.Bot 22.9.0 : la méthode d'extension s'appelle `SetMyCommands` (pas `SetMyCommandsAsync`)
- `SetMyCommandsRequest` est `internal` dans Telegram.Bot 22.9.0 — vérification via `request.GetType().Name` dans le fake client de test

### Completion Notes List

- ✅ Task 1 : `SetMyCommands` ajouté dans `TelegramBotService.ExecuteAsync()` après le bloc onboarding, via pattern `_ = Task.Run(async () => ...)` non-bloquant. 9 commandes construites sans barre oblique, cohérentes avec `HelpCommandHandler`.
- ✅ Task 2 : try/catch avec `LogContext.PushProperty("Step", "Notify")` + `_logger.LogWarning` en cas d'échec. Démarrage non bloqué.
- ✅ Task 3 : 5 tests créés dans `TelegramBotServiceSetCommandsTests.cs` — vérification du nombre de commandes (9), des noms sans "/", des descriptions non-vides, de la cohérence avec /help, et de la non-propagation d'exception. 384 tests total, 0 échec.

### File List

- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (modifié)
- `tests/Bet2InvestPoster.Tests/Telegram/TelegramBotServiceSetCommandsTests.cs` (créé)

## Change Log

- 2026-02-25 : Story 15.2 implémentée — enregistrement des commandes via `SetMyCommands` au démarrage du bot, non-bloquant avec gestion d'erreur. 5 nouveaux tests (384 total).
- 2026-02-25 : Code review adversarial — 6 issues trouvées (1 HIGH, 3 MEDIUM, 2 LOW). Fix appliqués : descriptions /schedule et /report alignées avec /help (ajout paramètres [HH:mm,...] et [jours]), assertion supplémentaire dans test d'échec. Action items restants : extraction constante partagée CommandDefinitions (DRY), remplacement Task.Delay(200) par synchronisation explicite dans tests.
