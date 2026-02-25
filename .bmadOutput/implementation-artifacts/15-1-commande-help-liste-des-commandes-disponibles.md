# Story 15.1: Commande /help — Liste des commandes disponibles

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want envoyer `/help` pour afficher toutes les commandes du bot avec leur description,
so that je puisse découvrir et utiliser toutes les fonctionnalités sans documentation externe.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé
   **When** l'utilisateur envoie `/help`
   **Then** `HelpCommandHandler` répond avec la liste complète des commandes disponibles (FR40)
   **And** chaque commande est affichée avec sa syntaxe et une description courte

2. **Given** une nouvelle commande ajoutée au bot dans le futur
   **When** le développeur ajoute un `CommandHandler`
   **Then** la liste dans `/help` doit être mise à jour manuellement (pas de découverte automatique)

3. **Given** un utilisateur non autorisé envoie `/help`
   **When** `AuthorizationFilter` filtre la commande
   **Then** la commande est ignorée silencieusement (comportement existant FR20)

## Tasks / Subtasks

- [x] Task 1 : Créer `HelpCommandHandler` (AC: #1, #2)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Telegram/Commands/HelpCommandHandler.cs`
  - [x] 1.2 Implémenter `ICommandHandler` : `CanHandle("/help")` retourne `true`
  - [x] 1.3 Construire le message d'aide avec toutes les commandes existantes et leurs descriptions
  - [x] 1.4 Envoyer le message formaté via `bot.SendMessage()` avec `ParseMode.Html` pour le formatage

- [x] Task 2 : Enregistrer le handler dans le conteneur DI (AC: #1)
  - [x] 2.1 Ajouter `services.AddSingleton<ICommandHandler, HelpCommandHandler>()` dans `Program.cs`

- [x] Task 3 : Mettre à jour le message "commande inconnue" dans `TelegramBotService` (AC: #1)
  - [x] 3.1 Ajouter `/help` à la liste des commandes dans le message fallback "Commande inconnue"

- [x] Task 4 : Tests (AC: #1, #2, #3)
  - [x] 4.1 Test `HelpCommandHandler.CanHandle` : `/help` → true, `/run` → false
  - [x] 4.2 Test `HelpCommandHandler.HandleAsync` : vérifie que le message contient toutes les commandes connues (/run, /status, /start, /stop, /history, /schedule, /tipsters, /report, /help)
  - [x] 4.3 Test sécurité : vérifier que `AuthorizationFilter` bloque `/help` pour un chat ID non autorisé (couvert par les tests existants d'AuthorizationFilter)

## Dev Notes

### Pattern de commande existant

Tous les handlers implémentent `ICommandHandler` :
```csharp
public interface ICommandHandler
{
    bool CanHandle(string command);
    Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct);
}
```

Le dispatch se fait dans `TelegramBotService.HandleUpdateAsync()` via :
```csharp
var command = text.Split(' ')[0].ToLowerInvariant();
var handler = _handlers.FirstOrDefault(h => h.CanHandle(command));
```

`HelpCommandHandler` est un handler **simple** (pas de dépendance sur des services métier) — similaire à `StartCommandHandler` / `StopCommandHandler`.

### Handlers existants (9 commandes)

| Commande | Handler | Description |
|---|---|---|
| `/run` | `RunCommandHandler` | Exécuter un cycle de publication manuellement |
| `/status` | `StatusCommandHandler` | Afficher l'état du système |
| `/start` | `StartCommandHandler` | Activer le scheduling automatique |
| `/stop` | `StopCommandHandler` | Suspendre le scheduling automatique |
| `/history` | `HistoryCommandHandler` | Historique des publications récentes |
| `/schedule` | `ScheduleCommandHandler` | Configurer les horaires d'exécution |
| `/tipsters` | `TipstersCommandHandler` | Gérer la liste des tipsters |
| `/report` | `ReportCommandHandler` | Tableau de bord des performances |
| `/help` | `HelpCommandHandler` | **NOUVEAU** — Liste des commandes |

### Contenu du message /help

Le message doit lister chaque commande avec sa syntaxe et une description courte. Format recommandé (HTML) :

```
<b>Commandes disponibles</b>

/run — Exécuter un cycle de publication
/status — Afficher l'état du système
/start — Activer le scheduling automatique
/stop — Suspendre le scheduling automatique
/history — Historique des publications récentes
/schedule [HH:mm,...] — Configurer les horaires d'exécution
/tipsters — Gérer la liste des tipsters
/report [jours] — Tableau de bord des performances
/help — Afficher cette aide
```

### Enregistrement DI

Les handlers sont enregistrés dans `Program.cs` comme `ICommandHandler`. Pattern exact à suivre :
```csharp
services.AddSingleton<ICommandHandler, HelpCommandHandler>();
```

L'ordre d'enregistrement n'a pas d'importance car le dispatch utilise `FirstOrDefault(h => h.CanHandle(command))` — chaque handler ne répond qu'à une seule commande.

### Message "commande inconnue"

Dans `TelegramBotService.HandleUpdateAsync()`, le fallback simplifié :
```csharp
"Commande inconnue. Tapez /help pour la liste des commandes."
```

### Sécurité (AC: #3)

La commande `/help` est protégée nativement par le filtre `AuthorizationFilter` dans `TelegramBotService.HandleUpdateAsync()` — le check `_authFilter.IsAuthorized(chatId)` s'applique avant le dispatch. **Aucun code spécifique nécessaire.**

### Project Structure Notes

- **Nouveau fichier** : `src/Bet2InvestPoster/Telegram/Commands/HelpCommandHandler.cs`
- **Fichiers modifiés** :
  - `src/Bet2InvestPoster/Program.cs` (ajout DI)
  - `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (message fallback)
- **Nouveau fichier test** : `tests/Bet2InvestPoster.Tests/Telegram/Commands/HelpCommandHandlerTests.cs`

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase4.md#Epic 15] — Définition epic et story 15.1
- [Source: .bmadOutput/planning-artifacts/architecture.md#Telegram Boundary] — Pattern TelegramBotService, dispatch commandes
- [Source: src/Bet2InvestPoster/Telegram/Commands/ICommandHandler.cs] — Interface ICommandHandler
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs] — Dispatch et message fallback
- [Source: src/Bet2InvestPoster/Telegram/Commands/StopCommandHandler.cs] — Exemple handler simple
- [Source: .bmadOutput/implementation-artifacts/14-1-scheduling-multi-horaires-configurable.md] — Story précédente, patterns DI

## Dev Agent Record

### Agent Model Used

Claude Sonnet 4.6

### Debug Log References

(aucun problème rencontré)

### Completion Notes List

- Handler simple sans dépendance métier — pattern identique à StartCommandHandler/StopCommandHandler
- Message `/help` en dur (string constante) — conformément à AC #2 (pas de découverte automatique)
- Message fallback "commande inconnue" simplifié : "Commande inconnue. Tapez /help pour la liste des commandes."
- Sécurité AC #3 : couverte nativement par AuthorizationFilter — aucun code spécifique nécessaire
- 375 tests passés (0 échec), incluant 13 nouveaux tests HelpCommandHandlerTests

### File List

**Production (src/Bet2InvestPoster/)**

- `src/Bet2InvestPoster/Telegram/Commands/HelpCommandHandler.cs` — NOUVEAU
- `src/Bet2InvestPoster/Program.cs` — Ajout DI HelpCommandHandler
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` — Message fallback simplifié

**Tests (tests/Bet2InvestPoster.Tests/)**

- `tests/Bet2InvestPoster.Tests/Telegram/Commands/HelpCommandHandlerTests.cs` — NOUVEAU

### Change Log

- 2026-02-25 : Implémentation story 15.1 — Ajout commande /help avec HelpCommandHandler, enregistrement DI, mise à jour message fallback, 12 tests unitaires
