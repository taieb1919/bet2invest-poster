# Story 11.1: Commande /tipsters update — Scraping et Suggestion Automatique

Status: review

## Story

As a l'utilisateur,
I want que le système scrape automatiquement les tipsters free de bet2invest et me propose une liste mise à jour,
so that ma liste de tipsters reste optimale sans recherche manuelle sur le site.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé **When** l'utilisateur envoie `/tipsters update` **Then** le système utilise `ExtendedBet2InvestClient` pour scraper la liste des tipsters free triés par ROI descendant (FR32) **And** le bot affiche la liste proposée avec : nom, ROI, nombre de pronostics, sport principal **And** le bot demande confirmation : `"Voulez-vous remplacer votre liste actuelle ? [Oui / Non / Fusionner]"`

2. **Given** l'utilisateur répond "Oui" **When** la confirmation est reçue **Then** `tipsters.json` est remplacé par la nouvelle liste avec écriture atomique **And** le bot confirme `"✅ Liste mise à jour : {count} tipsters."`

3. **Given** l'utilisateur répond "Fusionner" **When** la confirmation est reçue **Then** les nouveaux tipsters sont ajoutés aux existants (sans doublons) **And** le bot confirme `"✅ {added} tipsters ajoutés. Total : {count}."`

4. **Given** l'utilisateur répond "Non" **When** la confirmation est reçue **Then** aucune modification n'est effectuée **And** le bot confirme `"❌ Mise à jour annulée."`

5. **Given** le scraping échoue (API indisponible) **When** l'erreur est détectée **Then** le bot répond `"❌ Impossible de récupérer la liste des tipsters — {raison}."`

## Tasks / Subtasks

- [x] Task 1 : Ajouter `GetFreeTipstersAsync()` à `IExtendedBet2InvestClient` et `ExtendedBet2InvestClient` (AC: #1)
  - [x] 1.1 Ajouter la méthode à l'interface `IExtendedBet2InvestClient`
  - [x] 1.2 Implémenter dans `ExtendedBet2InvestClient` : appel API `/tipsters` paginé, filtre `Pro == false`, tri par ROI descendant
  - [x] 1.3 Retourner `List<ScrapedTipster>` avec : Username, ROI, BetsNumber, MostBetSport
  - [x] 1.4 Respecter le rate limiting 500ms entre requêtes paginées
- [x] Task 2 : Créer le modèle `ScrapedTipster` (AC: #1)
  - [x] 2.1 Créer `Models/ScrapedTipster.cs` avec les propriétés nécessaires à l'affichage et à la conversion en `TipsterConfig`
- [x] Task 3 : Implémenter la conversation stateful pour confirmation utilisateur (AC: #2, #3, #4)
  - [x] 3.1 Créer `Services/IConversationStateService.cs` + `Services/ConversationStateService.cs` — ConcurrentDictionary par chatId
  - [x] 3.2 Intégrer dans `TelegramBotService` : si un état de conversation existe pour le chatId, router le message vers le handler en attente au lieu du dispatch normal
  - [x] 3.3 Timeout automatique 60s — nettoyer l'état si pas de réponse
- [x] Task 4 : Étendre `TipstersCommandHandler` avec le sous-commande `update` (AC: #1-#5)
  - [x] 4.1 Ajouter le case `"update"` dans le switch de `HandleAsync`
  - [x] 4.2 Appeler `GetFreeTipstersAsync()` via scope DI
  - [x] 4.3 Formatter et afficher la liste proposée avec `IMessageFormatter`
  - [x] 4.4 Enregistrer un état de conversation avec callback pour traiter la réponse
  - [x] 4.5 Implémenter les 3 branches : Oui (remplacer), Fusionner (merge sans doublons), Non (annuler)
  - [x] 4.6 Utiliser `ITipsterService` pour les opérations d'écriture (réutiliser l'atomicité existante)
- [x] Task 5 : Ajouter `ReplaceTipstersAsync()` à `ITipsterService` et `TipsterService` (AC: #2)
  - [x] 5.1 Nouvelle méthode pour remplacer toute la liste — réutiliser `SaveAtomicAsync` existant
- [x] Task 6 : Étendre `IMessageFormatter` et `MessageFormatter` (AC: #1)
  - [x] 6.1 Ajouter `FormatScrapedTipsters(List<ScrapedTipster> tipsters)` — affichage avec ROI, pronostics, sport
  - [x] 6.2 Ajouter le message de confirmation avec les 3 options
- [x] Task 7 : Tests unitaires (AC: #1-#5)
  - [x] 7.1 Tests `GetFreeTipstersAsync` — filtrage free, tri ROI, pagination
  - [x] 7.2 Tests `TipstersCommandHandler` update — scénarios Oui/Fusionner/Non/Erreur
  - [x] 7.3 Tests `ConversationState` — timeout, nettoyage, routage
  - [x] 7.4 Tests `ReplaceTipstersAsync` — remplacement atomique
- [x] Task 8 : `dotnet build` + `dotnet test` passent sans erreur

## Dev Notes

### Architecture et Patterns Critiques

**Pattern commande existant** — Le `TipstersCommandHandler` gère déjà `add`/`remove` via un switch sur `parts[1]`. Ajouter le case `"update"` au même endroit. Ne PAS créer un nouveau handler.

**Conversation stateful** — C'est le point le plus délicat. Le bot actuel est stateless (une commande = une réponse). Pour `/tipsters update`, il faut :
- Un mécanisme de conversation multi-tour (question → attente réponse → traitement)
- Solution recommandée : `ConcurrentDictionary<long, PendingConversation>` dans un service singleton `IConversationStateService`
- Le `TelegramBotService` doit vérifier si un état de conversation existe AVANT de dispatcher au handler de commande normal
- Timeout de 60s avec `CancellationTokenSource` + nettoyage automatique

**API `/tipsters` du submodule** — Le `Bet2InvestClient` du submodule a déjà `GetTipstersAsync(int maxTipsters, int minBets)` qui retourne `List<Tipster>`. Le modèle `Tipster` du submodule contient : `Id`, `Username`, `Pro`, `Grade`, `GeneralStatistics` (ROI, Profit, BetsNumber, MostBetSport). CEPENDANT, ne PAS appeler directement le client submodule — passer par `ExtendedBet2InvestClient` qui gère l'auth et le rate limiting.

**Filtrage free** — Le champ `Tipster.Pro == false` identifie les tipsters gratuits. Alternativement, `Tipster.Tier` peut être `null` ou `"free"` pour les free.

**Merge sans doublons** — Comparer par slug (username) en case-insensitive, exactement comme `AddTipsterAsync` le fait déjà. Utiliser `TipsterConfig.TryExtractSlug()` pour normaliser.

**Écriture atomique** — `TipsterService.SaveAtomicAsync()` existe déjà (write-to-temp + rename + SemaphoreSlim). Réutiliser ce pattern.

### Composants Existants à Réutiliser

| Composant | Fichier | Utilisation |
|---|---|---|
| `TipstersCommandHandler` | `Telegram/Commands/TipstersCommandHandler.cs` | Étendre avec case `"update"` |
| `TipsterService` | `Services/TipsterService.cs` | `LoadTipstersAsync`, `AddTipsterAsync`, nouveau `ReplaceTipstersAsync` |
| `ExtendedBet2InvestClient` | `Services/ExtendedBet2InvestClient.cs` | Nouveau `GetFreeTipstersAsync` |
| `MessageFormatter` | `Telegram/Formatters/MessageFormatter.cs` | Nouveau `FormatScrapedTipsters` |
| `TelegramBotService` | `Telegram/TelegramBotService.cs` | Intégrer le routage conversation stateful |
| `TipsterConfig` | `Models/TipsterConfig.cs` | Modèle existant + `TryExtractSlug()` |

### Modèles du Submodule (lecture seule, NE PAS MODIFIER)

```csharp
// jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs
public class Tipster {
    public int Id { get; set; }
    public string Username { get; set; }
    public bool Pro { get; set; }
    public string? Tier { get; set; }
    public TipsterStatistics? GeneralStatistics { get; set; }
}

public class TipsterStatistics {
    public decimal Roi { get; set; }
    public int BetsNumber { get; set; }
    public string MostBetSport { get; set; }
}
```

### DTOs Existants dans ExtendedBet2InvestClient

```csharp
// Classes privées existantes dans ExtendedBet2InvestClient.cs
private class TipstersResponse {
    public List<ApiTipster> Tipsters { get; set; }
    public ApiPagination? Pagination { get; set; }
}
private class ApiTipster {
    public int Id { get; set; }
    public string Username { get; set; }
}
```

Ces DTOs sont privés et insuffisants pour story 11.1 (pas de ROI, Pro, etc.). Options :
1. **Recommandé** : Utiliser `Bet2InvestClient.GetTipstersAsync()` du submodule via composition (il gère déjà la désérialisation complète du modèle `Tipster`)
2. Alternative : Enrichir les DTOs privés de `ExtendedBet2InvestClient`

### DI Registration

- `IConversationStateService` → Singleton (état partagé entre scopes)
- Pas de nouveau handler à enregistrer — on étend `TipstersCommandHandler` existant

### Serilog Step

- Utiliser le Step `Scrape` pour le scraping des tipsters free
- Loguer : nombre de tipsters trouvés, nombre après filtrage free, action utilisateur (Oui/Non/Fusionner)

### Project Structure Notes

Fichiers à créer :
- `src/Bet2InvestPoster/Models/ScrapedTipster.cs`
- `src/Bet2InvestPoster/Telegram/ConversationState.cs` (ou `Services/IConversationStateService.cs` + `Services/ConversationStateService.cs`)

Fichiers à modifier :
- `src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs`
- `src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs`
- `src/Bet2InvestPoster/Services/ITipsterService.cs`
- `src/Bet2InvestPoster/Services/TipsterService.cs`
- `src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs`
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs`
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs`
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs`
- `src/Bet2InvestPoster/Program.cs` (DI registration)

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Epic 11 — Story 11.1]
- [Source: .bmadOutput/planning-artifacts/architecture.md#API Patterns, DI Pattern, Data Boundary]
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs#Tipster, TipsterStatistics]
- [Source: jtdev-bet2invest-scraper/Api/Bet2InvestClient.cs#GetTipstersAsync]
- [Source: src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs#ResolveTipsterIdsAsync, TipstersResponse]
- [Source: src/Bet2InvestPoster/Services/TipsterService.cs#SaveAtomicAsync, AddTipsterAsync]
- [Source: src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs#HandleAsync]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

Aucun blocage — implémentation directe conforme aux Dev Notes.

### Completion Notes List

- ✅ `ScrapedTipster` créé avec `ToTipsterConfig()` utilisant le pattern URL bet2invest standard
- ✅ `IConversationStateService` + `ConversationStateService` : ConcurrentDictionary, timeout 60s via CancellationTokenSource, nettoyage automatique
- ✅ `GetFreeTipstersAsync` : enrichissement des DTOs privés `ApiTipster` (Pro, Tier, GeneralStatistics) sans dépendance sur `_scraperClient` (testable via constructeur interne HttpClient)
- ✅ `TipstersCommandHandler` : refactorisé en méthodes privées isolées (HandleAddAsync, HandleRemoveAsync, HandleUpdateAsync, HandleUpdateConfirmationAsync) — chaque opération crée son propre scope DI
- ✅ `TelegramBotService` : routage conversation stateful avant dispatch commandes, `IConversationStateService` injecté
- ✅ 282 tests passent (dont 20+ nouveaux pour story 11.1)
- ✅ Décision architecture : option 2 (enrichissement DTOs privés) choisie pour testabilité — pas de dépendance sur `_scraperClient`

### File List

src/Bet2InvestPoster/Models/ScrapedTipster.cs (nouveau)
src/Bet2InvestPoster/Services/IConversationStateService.cs (nouveau)
src/Bet2InvestPoster/Services/ConversationStateService.cs (nouveau)
src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs (modifié — GetFreeTipstersAsync)
src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs (modifié — GetFreeTipstersAsync + DTOs enrichis)
src/Bet2InvestPoster/Services/ITipsterService.cs (modifié — ReplaceTipstersAsync)
src/Bet2InvestPoster/Services/TipsterService.cs (modifié — ReplaceTipstersAsync)
src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs (modifié — case update + conversation)
src/Bet2InvestPoster/Telegram/TelegramBotService.cs (modifié — routage conversation stateful)
src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs (modifié — FormatScrapedTipsters, FormatScrapedTipstersConfirmation)
src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs (modifié — implémentations)
src/Bet2InvestPoster/Program.cs (modifié — DI IConversationStateService Singleton)
tests/Bet2InvestPoster.Tests/Services/ConversationStateServiceTests.cs (nouveau)
tests/Bet2InvestPoster.Tests/Services/ExtendedBet2InvestClientTests.cs (modifié — tests GetFreeTipstersAsync)
tests/Bet2InvestPoster.Tests/Services/TipsterServiceTests.cs (modifié — tests ReplaceTipstersAsync)
tests/Bet2InvestPoster.Tests/Telegram/Commands/TipstersCommandHandlerTests.cs (modifié — fakes mis à jour + tests update)
tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs (modifié — fakes mis à jour)
tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs (modifié — fakes mis à jour)
tests/Bet2InvestPoster.Tests/Services/BetPublisherTests.cs (modifié — fake mis à jour)
tests/Bet2InvestPoster.Tests/Services/UpcomingBetsFetcherTests.cs (modifié — fake mis à jour)
tests/Bet2InvestPoster.Tests/Services/OnboardingServiceTests.cs (modifié — fakes mis à jour)
tests/Bet2InvestPoster.Tests/Telegram/AuthorizationFilterTests.cs (modifié — DI IConversationStateService)
