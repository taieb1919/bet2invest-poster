# Story 8.1: Commande /tipsters — Consultation de la Liste

Status: review

## Story

As a l'utilisateur,
I want afficher la liste de mes tipsters actuels via `/tipsters`,
so that je puisse vérifier quels tipsters sont configurés sans accéder au VPS.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé **When** l'utilisateur envoie `/tipsters` **Then** `TipstersCommandHandler` lit `tipsters.json` et affiche la liste complète (FR28)
2. **And** chaque tipster affiche : nom, URL, statut (free/premium)
3. **And** le nombre total de tipsters est affiché en fin de message
4. **Given** `tipsters.json` vide ou inexistant **When** l'utilisateur envoie `/tipsters` **Then** le bot répond `"📭 Aucun tipster configuré. Utilisez /tipsters add <lien> pour en ajouter."`

## Tasks / Subtasks

- [x] Task 1 : Créer `TipstersCommandHandler` (AC: #1, #2, #3, #4)
  - [x] 1.1 Créer `src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs` implémentant `ICommandHandler`
  - [x] 1.2 `CanHandle()` retourne `true` pour `/tipsters` uniquement (PAS `/tipsters add` ni `/tipsters remove` — réservés story 8.2)
  - [x] 1.3 Injecter `IServiceScopeFactory` et `IMessageFormatter` via constructeur (ITipsterService est Scoped, handler est Singleton — pattern RunCommandHandler)
  - [x] 1.4 Appeler `_tipsterService.LoadTipstersAsync()` et formatter le résultat
  - [x] 1.5 Gérer le cas liste vide : message `"📭 Aucun tipster configuré. Utilisez /tipsters add <lien> pour en ajouter."`
- [x] Task 2 : Ajouter `FormatTipsters()` à `IMessageFormatter` / `MessageFormatter` (AC: #2, #3)
  - [x] 2.1 Ajouter `string FormatTipsters(List<TipsterConfig> tipsters)` à l'interface `IMessageFormatter`
  - [x] 2.2 Implémenter dans `MessageFormatter` : afficher nom, URL, nombre total
  - [x] 2.3 Format attendu : liste numérotée avec nom et URL, total en fin de message
- [x] Task 3 : Enregistrer le handler dans DI (AC: #1)
  - [x] 3.1 Ajouter `builder.Services.AddSingleton<ICommandHandler, TipstersCommandHandler>();` dans `Program.cs`
  - [x] 3.2 Mettre à jour le message "commande inconnue" dans `TelegramBotService` pour inclure `/tipsters`
- [x] Task 4 : Tests unitaires (AC: #1, #2, #3, #4)
  - [x] 4.1 Tests `TipstersCommandHandler` : commande reconnue, liste affichée, liste vide
  - [x] 4.2 Tests `MessageFormatter.FormatTipsters` : format correct, liste vide, un tipster, plusieurs tipsters
  - [x] 4.3 Tests `CanHandle` : `/tipsters` → true, `/tipsters add` → false, `/tipsters remove` → false
- [x] Task 5 : Mettre à jour story file et sprint-status

## Dev Notes

### Pattern CommandHandler — Copier exactement

Tous les handlers suivent ce pattern identique. NE PAS dévier :

```csharp
public class TipstersCommandHandler : ICommandHandler
{
    private readonly ITipsterService _tipsterService;
    private readonly IMessageFormatter _formatter;
    private readonly ILogger<TipstersCommandHandler> _logger;

    // Constructor avec injection DI

    public bool CanHandle(string command) => command == "/tipsters";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        using (LogContext.PushProperty("Step", "Notify"))
        {
            _logger.LogInformation("Commande /tipsters reçue");
            // ... logique
            await bot.SendMessage(chatId, text, cancellationToken: ct);
        }
    }
}
```

### Parsing commande `/tipsters` vs `/tipsters add`

**ATTENTION CRITIQUE** : Le dispatch dans `TelegramBotService.HandleUpdateAsync` extrait la commande ainsi :
```csharp
var command = text.Split(' ')[0].ToLowerInvariant();
```

Cela signifie que `/tipsters add https://...` produit `command = "/tipsters"`. Le `TipstersCommandHandler` de cette story 8.1 sera donc appelé pour TOUTES les variantes `/tipsters*`.

**Stratégie pour story 8.1** : `CanHandle` matche `/tipsters`. Dans `HandleAsync`, vérifier si `message.Text` contient des arguments (split par espace). Si pas d'arguments → afficher la liste. Si arguments (`add`, `remove`) → répondre `"Cette fonctionnalité sera disponible prochainement."` OU ne rien faire (la story 8.2 ajoutera la logique).

**Décision recommandée** : Dans cette story 8.1, traiter UNIQUEMENT le cas sans argument. Si des arguments sont détectés (`add`/`remove`), répondre avec un message d'aide : `"Usage : /tipsters | /tipsters add <lien> | /tipsters remove <lien>"`. La story 8.2 remplacera cette logique par le vrai CRUD.

### TipsterService — Réutiliser tel quel

`TipsterService.LoadTipstersAsync()` existe déjà et :
- Lit `tipsters.json` depuis `PosterOptions.DataPath`
- Valide chaque entrée (URL, nom, slug extractible)
- Retourne `List<TipsterConfig>` avec propriétés `Name`, `Url`, `Id` (slug)
- Logué dans le step "Scrape"

**NE PAS** modifier `TipsterService` dans cette story. L'utiliser en lecture seule.

### TipsterConfig — Modèle existant

Fichier : `src/Bet2InvestPoster/Models/TipsterConfig.cs`
- `Url` (string) — URL complète du tipster
- `Name` (string) — Nom d'affichage
- `Id` (string, JsonIgnore) — Slug extrait de l'URL via `TryExtractSlug()`
- `NumericId` (int, JsonIgnore) — ID numérique résolu par l'API

**Note** : Le modèle `TipsterConfig` n'a PAS de champ `statut (free/premium)`. L'AC #2 demande d'afficher le statut. Options :
1. Afficher "free" par défaut car `TipsterService` ne charge que des tipsters free (FR6)
2. Ne pas afficher de statut — le fichier ne contient que des free

**Recommandation** : Afficher simplement le nom et l'URL. Ajouter une note "(free)" en en-tête du message pour indiquer que tous les tipsters listés sont free.

### Format du message `/tipsters`

Suivre le style `MessageFormatter` existant (voir `FormatHistory`, `FormatStatus`) :

```
📋 Tipsters configurés (free)

1. NG1 — https://bet2invest.com/tipsters/performance-stats/NG1
2. Edge Analytics — https://bet2invest.com/tipsters/performance-stats/Edge_Analytics
3. ProTips — https://bet2invest.com/tipsters/performance-stats/ProTips

Total : 3 tipsters
```

### Enregistrement DI

Dans `Program.cs`, ajouter la ligne APRÈS les handlers existants (ligne ~118) :
```csharp
builder.Services.AddSingleton<ICommandHandler, TipstersCommandHandler>();
```

### Message commandes inconnues

Dans `TelegramBotService.HandleUpdateAsync`, mettre à jour le message d'erreur pour inclure `/tipsters` :
```csharp
"Commande inconnue. Commandes disponibles : /run, /status, /start, /stop, /history, /schedule, /tipsters"
```

### Project Structure Notes

- Nouveau fichier : `src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs`
- Modifié : `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` (ajout `FormatTipsters`)
- Modifié : `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` (implémentation)
- Modifié : `src/Bet2InvestPoster/Program.cs` (registration DI)
- Modifié : `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (message aide)
- Nouveau fichier tests : `tests/Bet2InvestPoster.Tests/Telegram/Commands/TipstersCommandHandlerTests.cs`
- Modifié fichier tests : `tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterTests.cs`

### Testing Standards

- Tests xUnit avec `Substitute.For<T>()` (NSubstitute) pour les mocks
- Pattern existant : voir `HistoryCommandHandlerTests`, `StatusCommandHandlerTests`
- Capturer le message envoyé via le mock `ITelegramBotClient` pour vérifier le contenu
- Tester : commande reconnue, liste avec tipsters, liste vide, `CanHandle` avec variantes

### Learnings Epic 7 (Rétrospective)

1. **Le pattern CommandHandler scale bien** — 6 commandes implémentées sans modifier le dispatch. Idem ici.
2. **Tests async** : utiliser signaling déterministe (`TaskCompletionSource`), JAMAIS `Task.Delay` arbitraire
3. **Mettre à jour le story file et sprint-status** en fin d'implémentation (action items rétro 7)
4. **Fakes à maintenir** : si `IMessageFormatter` gagne une méthode, mettre à jour les fakes dans les tests existants

### Préparer story 8.2

Cette story pose les fondations pour 8.2 (CRUD `/tipsters add` et `/tipsters remove`). Points à anticiper :
- Le `TipstersCommandHandler` devra être modifié en 8.2 pour parser les sous-commandes `add`/`remove`
- `TipsterService` sera étendu (pas remplacé) avec des méthodes d'écriture atomique en 8.2
- Garder le handler simple et extensible

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 8.1]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation Patterns]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Project Structure]
- [Source: .bmadOutput/implementation-artifacts/epic-7-retro-2026-02-25.md#Préparation Epic 8]
- [Source: src/Bet2InvestPoster/Telegram/Commands/HistoryCommandHandler.cs — pattern de référence]
- [Source: src/Bet2InvestPoster/Services/TipsterService.cs — service existant à réutiliser]
- [Source: src/Bet2InvestPoster/Telegram/TelegramBotService.cs:65-91 — dispatch commandes]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

Aucun — implémentation sans erreur du premier coup.

### Completion Notes List

- `TipstersCommandHandler` utilise `IServiceScopeFactory` (pas `ITipsterService` directement) car `ITipsterService` est Scoped et le handler est Singleton — même pattern que `RunCommandHandler`.
- `CanHandle` matche `/tipsters` uniquement. Si arguments détectés dans `HandleAsync`, répond avec message d'aide Usage (préparation story 8.2).
- `FormatTipsters` gère le singulier/pluriel pour "tipster" vs "tipsters".
- 213 tests passent (0 échec) — +11 nouveaux tests ajoutés.

### File List

- `src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs` (nouveau)
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs` (modifié — ajout FormatTipsters)
- `src/Bet2InvestPoster/Telegram/Formatters/MessageFormatter.cs` (modifié — implémentation FormatTipsters)
- `src/Bet2InvestPoster/Program.cs` (modifié — registration DI TipstersCommandHandler)
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs` (modifié — message aide inclut /tipsters)
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/TipstersCommandHandlerTests.cs` (nouveau)
- `tests/Bet2InvestPoster.Tests/Telegram/Formatters/MessageFormatterTests.cs` (modifié — ajout tests FormatTipsters)

