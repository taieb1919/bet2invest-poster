# Story 8.2: Commandes /tipsters add et /tipsters remove — CRUD Tipsters

Status: review

## Story

As a l'utilisateur,
I want ajouter ou retirer des tipsters via `/tipsters add <lien>` et `/tipsters remove <lien>` depuis Telegram,
so that je puisse mettre à jour ma liste de tipsters sans éditer de fichier sur le VPS.

## Acceptance Criteria

1. **Given** le bot Telegram actif et l'utilisateur autorisé **When** l'utilisateur envoie `/tipsters add https://bet2invest.com/tipster/johndoe` **Then** `TipstersCommandHandler` ajoute le tipster dans `tipsters.json` avec écriture atomique (write-to-temp + rename) (FR29) **And** le bot répond `"✅ Tipster ajouté : johndoe"`
2. **Given** le lien fourni est déjà dans la liste **When** l'utilisateur envoie `/tipsters add <lien_existant>` **Then** le bot répond `"ℹ️ Ce tipster est déjà dans la liste."`
3. **Given** le bot Telegram actif et l'utilisateur autorisé **When** l'utilisateur envoie `/tipsters remove https://bet2invest.com/tipster/johndoe` **Then** le tipster est retiré de `tipsters.json` avec écriture atomique (FR30) **And** le bot répond `"🗑️ Tipster retiré : johndoe"`
4. **Given** le lien fourni n'existe pas dans la liste **When** l'utilisateur envoie `/tipsters remove <lien_inconnu>` **Then** le bot répond `"❌ Tipster non trouvé dans la liste."`
5. **Given** l'utilisateur envoie `/tipsters add` sans argument **When** le bot reçoit la commande **Then** le bot répond `"Usage : /tipsters add <lien_tipster>"`

## Tasks / Subtasks

- [x] Task 1 : Étendre `ITipsterService` avec méthodes d'écriture (AC: #1, #3)
  - [x] 1.1 Ajouter `Task<TipsterConfig> AddTipsterAsync(string url, CancellationToken ct)` à `ITipsterService`
  - [x] 1.2 Ajouter `Task<bool> RemoveTipsterAsync(string url, CancellationToken ct)` à `ITipsterService`
  - [x] 1.3 Implémenter dans `TipsterService` avec écriture atomique (write-to-temp + rename)
  - [x] 1.4 Ajouter `SemaphoreSlim(1, 1)` pour protéger les écritures concurrentes
  - [x] 1.5 Valider l'URL fournie : format HTTP(S), extraction slug via `TryExtractSlug()`
- [x] Task 2 : Modifier `TipstersCommandHandler` pour parser add/remove (AC: #1–#5)
  - [x] 2.1 Remplacer le message "prochainement" par la logique CRUD réelle
  - [x] 2.2 Parser les sous-commandes : `parts[1]` split par espace → subcommand + argument
  - [x] 2.3 `/tipsters add <url>` → appeler `AddTipsterAsync`, répondre avec le nom extrait
  - [x] 2.4 `/tipsters remove <url>` → appeler `RemoveTipsterAsync`, répondre avec le nom
  - [x] 2.5 `/tipsters add` ou `/tipsters remove` sans argument → message Usage
  - [x] 2.6 Gérer le doublon (AC #2) et le tipster non trouvé (AC #4)
- [x] Task 3 : Tests unitaires (AC: #1–#5)
  - [x] 3.1 Tests `TipsterService.AddTipsterAsync` : ajout valide, doublon détecté, URL invalide
  - [x] 3.2 Tests `TipsterService.RemoveTipsterAsync` : suppression valide, tipster non trouvé
  - [x] 3.3 Tests `TipsterService` écriture atomique : fichier .tmp créé puis renommé
  - [x] 3.4 Tests `TipstersCommandHandler` : add valide, add doublon, add sans argument, remove valide, remove non trouvé, remove sans argument
  - [x] 3.5 Mettre à jour les fakes existants si `ITipsterService` change (FakeTipsterService dans TipstersCommandHandlerTests)

## Dev Notes

### Pattern d'écriture atomique — Copier de HistoryManager

`HistoryManager.cs` utilise exactement le pattern requis. Reproduire dans `TipsterService` :

```csharp
private readonly SemaphoreSlim _semaphore = new(1, 1);

private async Task SaveAtomicAsync(List<TipsterConfig> tipsters, CancellationToken ct)
{
    var json = JsonSerializer.Serialize(tipsters, _jsonOptions);
    var tempPath = _tipstersPath + ".tmp";
    await File.WriteAllTextAsync(tempPath, json, ct);
    File.Move(tempPath, _tipstersPath, overwrite: true);
}
```

Le `_jsonOptions` doit inclure `WriteIndented = true` et `PropertyNameCaseInsensitive = true` pour cohérence avec le format existant de `tipsters.json`.

### Changement de lifetime TipsterService — ATTENTION

`TipsterService` est enregistré **Scoped** dans `Program.cs` (ligne ~65). Pour supporter l'écriture atomique avec `SemaphoreSlim`, deux approches :

**Option A (Recommandée)** : Garder Scoped mais utiliser un `SemaphoreSlim` **statique** pour protéger le fichier :
```csharp
private static readonly SemaphoreSlim _fileLock = new(1, 1);
```
Cela protège le fichier même si plusieurs scopes sont créés simultanément.

**Option B** : Changer le lifetime en Singleton (comme `HistoryManager`). MAIS cela casse le pattern "relecture à chaque cycle" qui permet l'édition à chaud. Ne PAS changer.

→ **Utiliser Option A** : SemaphoreSlim statique + garder Scoped.

### Parsing des sous-commandes dans TipstersCommandHandler

Le `TipstersCommandHandler` actuel fait déjà le split et détecte les arguments :

```csharp
var parts = message.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
if (parts.Length > 1)
{
    // Actuellement : message "prochainement" → REMPLACER par logique CRUD
}
```

Logique de remplacement :
```csharp
if (parts.Length > 1)
{
    var subCommand = parts[1].ToLowerInvariant();
    switch (subCommand)
    {
        case "add":
            if (parts.Length < 3) { /* message usage */ break; }
            var url = parts[2];
            // appeler AddTipsterAsync...
            break;
        case "remove":
            if (parts.Length < 3) { /* message usage */ break; }
            var removeUrl = parts[2];
            // appeler RemoveTipsterAsync...
            break;
        default:
            // message usage général
            break;
    }
}
```

### Extraction du nom depuis l'URL

`TipsterConfig.TryExtractSlug()` extrait le slug (ex: `johndoe` depuis `https://bet2invest.com/tipsters/performance-stats/johndoe`). Utiliser ce slug comme nom d'affichage dans les réponses.

Pour `AddTipsterAsync` :
1. Créer un `TipsterConfig` avec l'URL fournie
2. Appeler `TryExtractSlug()` pour valider et extraire le slug
3. Si l'extraction échoue → URL invalide, rejeter
4. Utiliser le slug comme `Name` par défaut (l'utilisateur ne fournit que l'URL)
5. Vérifier doublon par URL ou slug (comparaison case-insensitive)

### Format tipsters.json existant

```json
[
  { "url": "https://bet2invest.com/tipsters/performance-stats/NG1", "name": "NG1" },
  { "url": "https://bet2invest.com/tipsters/performance-stats/Edge_Analytics", "name": "Edge Analytics" }
]
```

Propriétés sérialisées : `url` et `name` uniquement (`Id` et `NumericId` sont `[JsonIgnore]`).

### Messages de réponse — Respecter les AC exactement

| Cas | Message |
|---|---|
| Add réussi | `"✅ Tipster ajouté : {slug}"` |
| Add doublon | `"ℹ️ Ce tipster est déjà dans la liste."` |
| Add sans argument | `"Usage : /tipsters add <lien_tipster>"` |
| Add URL invalide | `"❌ URL invalide. Format attendu : https://bet2invest.com/tipsters/performance-stats/<nom>"` |
| Remove réussi | `"🗑️ Tipster retiré : {slug}"` |
| Remove non trouvé | `"❌ Tipster non trouvé dans la liste."` |
| Remove sans argument | `"Usage : /tipsters remove <lien_tipster>"` |
| Sous-commande inconnue | `"Usage : /tipsters | /tipsters add <lien> | /tipsters remove <lien>"` |

### Accès scoped depuis handler Singleton

Même pattern que story 8.1 — utiliser `IServiceScopeFactory` :

```csharp
await using var scope = _scopeFactory.CreateAsyncScope();
var tipsterService = scope.ServiceProvider.GetRequiredService<ITipsterService>();
```

### Fichiers à modifier / créer

| Fichier | Action |
|---|---|
| `src/Bet2InvestPoster/Services/ITipsterService.cs` | Ajouter `AddTipsterAsync` et `RemoveTipsterAsync` |
| `src/Bet2InvestPoster/Services/TipsterService.cs` | Implémenter add/remove avec écriture atomique + SemaphoreSlim statique |
| `src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs` | Remplacer placeholder par logique CRUD |
| `tests/Bet2InvestPoster.Tests/Services/TipsterServiceTests.cs` | Ajouter tests add/remove/doublon/atomique |
| `tests/Bet2InvestPoster.Tests/Telegram/Commands/TipstersCommandHandlerTests.cs` | Ajouter tests sous-commandes + mettre à jour FakeTipsterService |

**Aucun nouveau fichier à créer.** Aucune modification DI dans `Program.cs` nécessaire.

### Project Structure Notes

- Pas de nouveau fichier à créer — on étend les fichiers existants de la story 8.1
- Pas de changement d'enregistrement DI — les handlers et services existants suffisent
- L'écriture atomique de `tipsters.json` suit le même pattern que `history.json` (HistoryManager)
- Le SemaphoreSlim statique est une exception locale justifiée par le lifetime Scoped du service

### Testing Standards

- Tests xUnit avec NSubstitute (`Substitute.For<T>()`) pour les mocks
- Utiliser les fakes existants : `FakeTipsterService`, `FakeTelegramBotClient`
- Ajouter `AddTipsterAsync` et `RemoveTipsterAsync` à `FakeTipsterService`
- Pour les tests atomiques dans `TipsterServiceTests`, utiliser un répertoire temp (`Path.GetTempPath()`)
- Vérifier le contenu du fichier après add/remove pour confirmer la persistance
- Pattern de test : Arrange → Act → Assert, un assert logique par test

### Learnings Story 8.1

1. `TipstersCommandHandler` utilise `IServiceScopeFactory` (pas `ITipsterService` directement) — Singleton vs Scoped
2. `CanHandle` matche `/tipsters` uniquement — le dispatch envoie TOUTES les variantes `/tipsters*` à ce handler
3. `FormatTipsters` existe déjà dans `IMessageFormatter` — ne pas modifier
4. Les fakes doivent être mis à jour quand l'interface change
5. 213 tests passent actuellement — ne pas en casser

### Learnings Epic 7 (Rétrospective)

1. Le pattern CommandHandler scale bien — 6 commandes sans modifier le dispatch
2. Tests async : signaling déterministe (`TaskCompletionSource`), JAMAIS `Task.Delay`
3. Mettre à jour story file et sprint-status en fin d'implémentation

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 8.2]
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation Patterns]
- [Source: .bmadOutput/implementation-artifacts/8-1-commande-tipsters-consultation-de-la-liste.md]
- [Source: src/Bet2InvestPoster/Services/HistoryManager.cs — pattern écriture atomique]
- [Source: src/Bet2InvestPoster/Services/TipsterService.cs — service à étendre]
- [Source: src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs — handler à modifier]
- [Source: src/Bet2InvestPoster/Models/TipsterConfig.cs — TryExtractSlug()]

## Dev Agent Record

### Agent Model Used

claude-opus-4-6

### Debug Log References

### Completion Notes List

- Implémenté le 2026-02-25
- `ITipsterService` étendu avec `AddTipsterAsync` et `RemoveTipsterAsync`
- `TipsterService` : écriture atomique (write-to-temp + rename) + `SemaphoreSlim` statique (Option A)
- `TipstersCommandHandler` : placeholder "prochainement" remplacé par logique CRUD complète
- Fakes mis à jour dans `PostingCycleServiceTests`, `PostingCycleServiceNotificationTests`, `TipstersCommandHandlerTests`
- 226 tests passent (213 existants + 13 nouveaux)

### File List

- `src/Bet2InvestPoster/Services/ITipsterService.cs`
- `src/Bet2InvestPoster/Services/TipsterService.cs`
- `src/Bet2InvestPoster/Telegram/Commands/TipstersCommandHandler.cs`
- `src/Bet2InvestPoster/Program.cs`
- `src/Bet2InvestPoster/Telegram/Formatters/IMessageFormatter.cs`
- `src/Bet2InvestPoster/Telegram/TelegramBotService.cs`
- `tests/Bet2InvestPoster.Tests/Services/TipsterServiceTests.cs`
- `tests/Bet2InvestPoster.Tests/Telegram/Commands/TipstersCommandHandlerTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceTests.cs`
- `tests/Bet2InvestPoster.Tests/Services/PostingCycleServiceNotificationTests.cs`

