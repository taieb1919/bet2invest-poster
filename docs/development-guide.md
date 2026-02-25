# bet2invest-poster — Guide de développement

**Généré le :** 2026-02-25

## Prérequis

- .NET SDK 9.0+
- Git (avec support submodules)
- Compte bet2invest (Identifier + Password)
- Bot Telegram (BotToken + ChatId autorisé)

## Installation

```bash
# Cloner avec submodule
git clone --recursive <repo-url>
cd bet2invest-poster

# Ou si déjà cloné sans submodule
git submodule update --init --recursive
```

## Configuration

### Variables d'environnement (fichier `.env`)

```bash
Bet2Invest__Identifier=<votre-identifiant>
Bet2Invest__Password=<votre-mot-de-passe>
Telegram__BotToken=<token-bot-telegram>
Telegram__AuthorizedChatId=<votre-chat-id>
Poster__BankrollId=<id-bankroll>
```

### Configuration optionnelle (`appsettings.json`)

```json
{
  "Poster": {
    "ScheduleTime": "08:00",
    "RetryDelayMs": 60000,
    "MaxRetryCount": 3,
    "DataPath": ".",
    "LogPath": "logs",
    "LogRetentionDays": 30,
    "MinOdds": null,
    "MaxOdds": null,
    "EventHorizonHours": null,
    "SelectionMode": "random",
    "HealthCheckPort": 8080
  }
}
```

## Build

```bash
# Build la solution complète
dotnet build Bet2InvestPoster.sln

# Ou seulement le projet principal
dotnet build src/Bet2InvestPoster
```

## Tests

```bash
# Exécuter tous les tests
dotnet test tests/Bet2InvestPoster.Tests

# Avec détail
dotnet test tests/Bet2InvestPoster.Tests --verbosity normal
```

**318+ tests** couvrant services, commandes Telegram, modèles, configuration et workers.

## Exécution locale

```bash
# Via script
./app.run.sh

# Ou directement
cd src/Bet2InvestPoster
dotnet run
```

Le service démarre le bot Telegram (long polling) et le scheduler. Les commandes Telegram sont immédiatement disponibles.

## Patterns de développement

### Ajouter un nouveau service

1. Créer `Services/IMonService.cs` (interface)
2. Créer `Services/MonService.cs` (implémentation)
3. Enregistrer dans `Program.cs` (Singleton ou Scoped selon le besoin)
4. Créer `tests/.../MonServiceTests.cs` avec Fakes

### Ajouter une commande Telegram

1. Créer `Telegram/Commands/MonCommandHandler.cs` implémentant `ICommandHandler`
2. `CanHandle("/macommande")` → true
3. `HandleAsync` : logique + envoi message
4. Enregistrer `builder.Services.AddSingleton<ICommandHandler, MonCommandHandler>()` dans `Program.cs`
5. Créer tests avec `FakeTelegramBotClient`

### Pattern de test (Fakes)

Le projet utilise des **Fakes** (sealed classes implémentant les interfaces) plutôt que des mocks. Pattern standard :

```csharp
private sealed class FakeHistoryManager : IHistoryManager
{
    public List<HistoryEntry> Entries { get; set; } = new();
    // Implémenter chaque méthode avec comportement minimal
}
```

### Écriture atomique (fichiers JSON)

Tout fichier persisté utilise le pattern write-to-temp + rename :

```csharp
var tempPath = path + ".tmp";
await File.WriteAllTextAsync(tempPath, json, ct);
File.Move(tempPath, path, overwrite: true);
```

Protégé par `SemaphoreSlim(1, 1)` pour la concurrence.

## Logging

Steps de log autorisés : `Auth`, `Scrape`, `Select`, `Publish`, `Notify`, `Purge`, `Report`

```csharp
using (LogContext.PushProperty("Step", "Publish"))
{
    _logger.LogInformation("Message...");
}
```

## CI/CD

- **CI** : `.github/workflows/ci.yml` — checkout (recursive) → .NET 9 → restore → build Release → test → upload artifacts
- **CD** : Déploiement manuel via `publish/` sur VPS
