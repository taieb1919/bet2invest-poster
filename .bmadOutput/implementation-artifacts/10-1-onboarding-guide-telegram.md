# Story 10.1: Onboarding Guidé via Telegram

Status: review

## Story

As a l'utilisateur,
I want être guidé au premier lancement du bot pour vérifier que tout est correctement configuré,
so that je puisse confirmer que le service est opérationnel sans connaissances techniques approfondies.

## Acceptance Criteria

1. **Given** le service démarre pour la première fois (aucun `history.json` existant) **When** le bot se connecte à Telegram **Then** le bot envoie un message d'onboarding à l'utilisateur autorisé (FR31) **And** le message inclut : confirmation de connexion API bet2invest, nombre de tipsters chargés, heure de scheduling configurée, liste des commandes disponibles **And** le bot propose `"Envoyez /run pour tester une première publication, ou /status pour vérifier l'état."`
2. **Given** le service a déjà fonctionné (`history.json` existe) **When** le service redémarre **Then** aucun message d'onboarding n'est envoyé
3. **Given** la connexion API bet2invest échoue au premier lancement **When** le bot envoie le message d'onboarding **Then** le message indique clairement l'erreur : `"⚠️ Connexion API bet2invest échouée — vérifiez vos credentials."`

## Tasks / Subtasks

- [x] Task 1 : Créer `IOnboardingService` / `OnboardingService` (AC: #1, #2, #3)
  - [x] 1.1 Interface `IOnboardingService` avec `Task TrySendOnboardingAsync(CancellationToken ct)`
  - [x] 1.2 Implémentation `OnboardingService` — injecte `IHistoryManager`, `ITipsterService`, `IExtendedBet2InvestClient`, `INotificationService`, `IExecutionStateService`, `IOptions<PosterOptions>`, `ILogger<OnboardingService>`
  - [x] 1.3 Détection premier lancement : vérifier si `history.json` existe via `IHistoryManager.GetPublishedKeysAsync()` — si collection vide = premier lancement
  - [x] 1.4 Test connexion API : appeler `IExtendedBet2InvestClient.LoginAsync()` dans un try/catch
  - [x] 1.5 Construire le message d'onboarding avec les 4 sections (connexion API, tipsters, scheduling, commandes)
  - [x] 1.6 En cas d'échec API : message d'onboarding dégradé avec avertissement credentials
  - [x] 1.7 Envoyer via `INotificationService` (méthode existante ou nouvelle `SendMessageAsync`)
- [x] Task 2 : Intégrer l'onboarding dans `TelegramBotService` (AC: #1, #2)
  - [x] 2.1 Appeler `IOnboardingService.TrySendOnboardingAsync()` dans `ExecuteAsync` de `TelegramBotService`, APRÈS le démarrage du polling
  - [x] 2.2 L'appel doit être fire-and-forget logué (ne pas bloquer le polling)
- [x] Task 3 : Enregistrement DI dans `Program.cs` (AC: #1)
  - [x] 3.1 Enregistrer `IOnboardingService` / `OnboardingService` en Singleton (accède à des services Singleton)
- [x] Task 4 : Formatter le message d'onboarding (AC: #1, #3)
  - [x] 4.1 Ajouter méthode `FormatOnboardingMessage(...)` dans `IMessageFormatter` / `MessageFormatter`
  - [x] 4.2 Format Telegram MarkdownV2 ou HTML cohérent avec les autres messages
- [x] Task 5 : Tests unitaires (AC: #1, #2, #3)
  - [x] 5.1 Test : premier lancement (history vide) → message d'onboarding envoyé
  - [x] 5.2 Test : service déjà fonctionné (history non vide) → pas de message
  - [x] 5.3 Test : connexion API échoue → message dégradé avec avertissement
  - [x] 5.4 Test : contenu du message inclut tipsters count, schedule time, commandes
  - [x] 5.5 Test : `TrySendOnboardingAsync` ne throw jamais (catch-all avec log)

## Dev Notes

### Architecture de la solution

L'onboarding est un service autonome qui s'exécute une seule fois au démarrage. Il n'y a **aucune logique d'onboarding existante** dans le codebase — tout est à créer.

### Détection premier lancement — via HistoryManager

**NE PAS vérifier l'existence du fichier `history.json` directement.** Utiliser `IHistoryManager` qui est le seul composant autorisé à accéder à ce fichier (boundary architecture).

`IHistoryManager` expose `GetPublishedKeysAsync()` qui retourne un `IReadOnlySet<string>`. Si le set est vide, c'est un premier lancement (ou un historique purgé après 30j sans activité — cas acceptable pour re-onboarder).

**ATTENTION** : `HistoryManager` est Singleton. `OnboardingService` doit aussi être Singleton pour éviter les problèmes de scope.

### Services à injecter dans OnboardingService

| Service | Lifetime | Usage |
|---------|----------|-------|
| `IHistoryManager` | Singleton | Vérifier si premier lancement |
| `INotificationService` | Singleton | Envoyer le message Telegram |
| `IExecutionStateService` | Singleton | Lire l'heure de scheduling |
| `IMessageFormatter` | Singleton | Formater le message |
| `IOptions<PosterOptions>` | Singleton | Lire DataPath (pour tipsters) |
| `ILogger<OnboardingService>` | — | Logging |

**Pour les services Scoped** (`ITipsterService`, `IExtendedBet2InvestClient`) : utiliser `IServiceScopeFactory` pour créer un scope temporaire, exactement comme le fait `TipstersCommandHandler` (pattern validé story 8.1).

```csharp
public class OnboardingService : IOnboardingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHistoryManager _historyManager;
    private readonly INotificationService _notificationService;
    // ...

    public async Task TrySendOnboardingAsync(CancellationToken ct)
    {
        try
        {
            var keys = await _historyManager.GetPublishedKeysAsync();
            if (keys.Count > 0) return; // Pas premier lancement

            using var scope = _scopeFactory.CreateScope();
            var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();
            var apiClient = scope.ServiceProvider.GetRequiredService<IExtendedBet2InvestClient>();
            // ...
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Onboarding check failed — non-blocking");
        }
    }
}
```

### Intégration dans TelegramBotService

`TelegramBotService` est un `BackgroundService`. Dans `ExecuteAsync`, après `bot.StartReceiving(...)` :

```csharp
// Après StartReceiving
_ = Task.Run(async () =>
{
    try
    {
        await _onboardingService.TrySendOnboardingAsync(stoppingToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Onboarding failed — non-blocking");
    }
}, stoppingToken);
```

**IMPORTANT** : L'onboarding ne doit JAMAIS bloquer le polling Telegram. Fire-and-forget avec logging.

### Message d'onboarding — Format

Le message doit suivre le style existant dans `MessageFormatter`. Exemple de format attendu :

```
🚀 Bienvenue sur bet2invest-poster !

📡 Connexion API : ✅ Connecté
👥 Tipsters chargés : 12
⏰ Publication quotidienne : 08:00

📋 Commandes disponibles :
/run — Lancer une publication manuelle
/status — État du système
/start — Activer le scheduling
/stop — Suspendre le scheduling
/history — Historique des publications
/schedule HH:mm — Changer l'heure
/tipsters — Gérer les tipsters

💡 Envoyez /run pour tester une première publication, ou /status pour vérifier l'état.
```

En cas d'échec API :
```
🚀 Bienvenue sur bet2invest-poster !

📡 Connexion API : ⚠️ Connexion API bet2invest échouée — vérifiez vos credentials.
👥 Tipsters chargés : 12
⏰ Publication quotidienne : 08:00

📋 Commandes disponibles :
[...]

⚠️ Corrigez vos credentials avant d'utiliser /run.
```

### NotificationService — méthode pour message libre

`INotificationService` expose déjà `NotifySuccessAsync`, `NotifyFailureAsync`, `NotifyFinalFailureAsync`, `NotifyNoFilteredCandidatesAsync`. Pour l'onboarding, utiliser le `ITelegramBotClient` directement depuis `NotificationService` ou ajouter une méthode `SendRawMessageAsync(string message)`.

Vérifier `NotificationService.cs` — il injecte `ITelegramBotClient` et `TelegramOptions`. Si une méthode générique n'existe pas, en ajouter une :
```csharp
Task SendMessageAsync(string message, CancellationToken ct = default);
```

### Fichiers à créer

| Fichier | Contenu |
|---------|---------|
| `src/Bet2InvestPoster/Services/IOnboardingService.cs` | Interface avec `TrySendOnboardingAsync` |
| `src/Bet2InvestPoster/Services/OnboardingService.cs` | Implémentation |
| `tests/Bet2InvestPoster.Tests/Services/OnboardingServiceTests.cs` | Tests unitaires |

### Fichiers à modifier

| Fichier | Modification |
|---------|-------------|
| `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` | Appel onboarding après StartReceiving |
| `src/Bet2InvestPoster/Program.cs` | Enregistrement DI `IOnboardingService` Singleton |
| `src/Bet2InvestPoster/Services/INotificationService.cs` | Ajouter `SendMessageAsync` si pas existant |
| `src/Bet2InvestPoster/Services/NotificationService.cs` | Implémenter `SendMessageAsync` si pas existant |
| `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` | Ajouter `FormatOnboardingMessage` |
| `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` | Implémenter `FormatOnboardingMessage` |

### Project Structure Notes

- `OnboardingService` va dans `Services/` conformément à l'architecture (logique métier dans Services/)
- Le service est Singleton (tous ses dépendances directes sont Singleton)
- Utilise `IServiceScopeFactory` pour les services Scoped (pattern TipstersCommandHandler)
- Pas de nouveau Worker — l'onboarding est déclenché depuis `TelegramBotService` existant

### Testing Standards

- Pattern xUnit existant : Arrange → Act → Assert
- Utiliser les Fakes existants : `FakeHistoryManager`, `FakeNotificationService`, `FakeTipsterService`
- Pour `IExtendedBet2InvestClient.LoginAsync()` : créer un mock simple ou un Fake
- `OnboardingService.TrySendOnboardingAsync` ne doit JAMAIS throw — vérifier le catch-all
- Vérifier que les Fakes existants dans d'autres tests n'ont pas besoin de mise à jour si `INotificationService` est étendu

### Learnings Story 9.1

1. Quand on étend une interface (`INotificationService`), il faut mettre à jour TOUS les Fakes dans les tests (PostingCycleServiceTests, NotificationTests, SchedulerWorkerTests, SchedulerWorkerPollyTests)
2. 236 tests passent actuellement — ne pas en casser
3. `IOptions<PosterOptions>` est disponible partout dans le DI, pas besoin de registration supplémentaire

### Learnings Epic 7 / 8

1. Pattern `IServiceScopeFactory` validé dans `TipstersCommandHandler` pour accéder aux services Scoped depuis un Singleton
2. `SemaphoreSlim` statique pour protéger les fichiers JSON en mode Scoped
3. Le format des messages Telegram doit utiliser `MessageFormatter` (pas de formatage inline)

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 10.1]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Project Structure]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation Patterns]
- [Source: src/Bet2InvestPoster/Services/IHistoryManager.cs]
- [Source: src/Bet2InvestPoster/Services/INotificationService.cs]
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs]
- [Source: src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs]
- [Source: src/Bet2InvestPoster/Program.cs — DI registration]
- [Source: .bmadOutput/implementation-artifacts/9-1-filtrage-par-cotes-et-plage-horaire.md — learnings]
- [Source: .bmadOutput/implementation-artifacts/8-1-commande-tipsters-consultation-de-la-liste.md — IServiceScopeFactory pattern]

## Dev Agent Record

### Agent Model Used

claude-opus-4-6

### Debug Log References

### Completion Notes List

- Implémentation complète de `IOnboardingService` / `OnboardingService` avec pattern `IServiceScopeFactory` pour les services Scoped
- `INotificationService.SendMessageAsync` ajouté pour l'envoi de messages libres
- `IMessageFormatter.FormatOnboardingMessage` ajouté avec message dégradé conditionnel (C2 fix)
- Footer conforme à l'AC #1 : "première publication" (M1 fix) et "Connecté" (M3 fix)
- `FakeNotificationService` extrait dans `tests/Helpers/` — partagé par tous les fichiers de tests (L1 fix)
- Test ajouté pour le chargement échoué des tipsters → `tipsterCount = 0` (M2 fix)

### File List

- `src/Bet2InvestPoster/Services/IOnboardingService.cs` (créé)
- `src/Bet2InvestPoster/Services/OnboardingService.cs` (créé)
- `src/Bet2InvestPoster/Services/INotificationService.cs` (modifié — ajout `SendMessageAsync`)
- `src/Bet2InvestPoster/Services/NotificationService.cs` (modifié — implémentation `SendMessageAsync`)
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` (modifié — ajout `FormatOnboardingMessage`)
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` (modifié — implémentation + fixes C2/M1/M3)
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (modifié — appel onboarding fire-and-forget)
- `src/Bet2InvestPoster/Program.cs` (modifié — registration DI `IOnboardingService`)
- `tests/Bet2InvestPoster.Tests/Services/OnboardingServiceTests.cs` (créé)
- `tests/Bet2InvestPoster.Tests/Helpers/FakeNotificationService.cs` (créé — L1 fix)
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs` (modifié — utilise fake partagé)
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs` (modifié — utilise fake partagé)
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerTests.cs` (modifié — utilise fake partagé)
- `tests/Bet2InvestPoster.Tests/Workers/SchedulerWorkerPollyTests.cs` (modifié — utilise fake partagé)

## Change Log

| Date | Version | Description | Author |
|------|---------|-------------|--------|
| 2026-02-25 | 1.0 | Implémentation initiale story 10.1 — onboarding guidé via Telegram | claude-opus-4-6 |
| 2026-02-25 | 1.1 | Fix code review : C2 message dégradé footer, M1 texte AC#1 conforme, M3 "Connecté" vs "OK", M2 test tipster load fails, L1 FakeNotificationService partagé | claude-sonnet-4-6 |
