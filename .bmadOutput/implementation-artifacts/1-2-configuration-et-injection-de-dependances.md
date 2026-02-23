# Story 1.2 : Configuration et Injection de Dépendances

Status: done

## Story

As a l'utilisateur,
I want configurer mes credentials et paramètres via appsettings.json et variables d'environnement,
So that je puisse personnaliser le service sans modifier le code.

## Acceptance Criteria

1. **Given** un fichier `appsettings.json` avec les sections Bet2Invest, Telegram, et Poster
   **When** le service démarre
   **Then** les sections sont présentes avec les clés et valeurs par défaut appropriées

2. **Given** le projet configuré avec les classes Options
   **When** le service démarre
   **Then** les options sont chargées via `IOptions<Bet2InvestOptions>`, `IOptions<TelegramOptions>`, `IOptions<PosterOptions>`
   **And** chaque section est enregistrée dans le DI container

3. **Given** une variable d'environnement définie (ex: `Bet2Invest__Identifier=user@example.com`)
   **When** le service démarre
   **Then** la variable d'environnement surcharge la valeur de appsettings.json (FR22)
   **And** la hiérarchie de config est : env vars > appsettings.{Environment}.json > appsettings.json

4. **Given** Serilog configuré dans Program.cs
   **When** le service démarre
   **Then** Serilog est le logger actif avec sinks console + fichier
   **And** le fichier log est créé dans le dossier configuré (PosterOptions.LogPath)
   **And** la rotation des logs est journalière

5. **Given** un message de log écrit avec `LogContext.PushProperty("Step", "Auth")`
   **When** le message apparaît dans les sinks
   **Then** le format inclut timestamp, level, Step, message, et propriétés structurées (NFR12)
   **And** le template est : `{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{Step}] {Message:lj} {Properties:j}`

6. **Given** les classes options Bet2InvestOptions et TelegramOptions
   **When** elles sont utilisées dans le code
   **Then** les propriétés `Password` et `BotToken` ne sont jamais passées à un logger (NFR5)
   **And** les options classes sont conçues pour ne pas exposer les credentials dans les logs

## Tasks / Subtasks

- [x] Task 1 : Créer les classes Options de configuration (AC: #1, #2)
  - [x] 1.1 Créer `Configuration/Bet2InvestOptions.cs` avec ApiBase, Identifier, Password, RequestDelayMs
  - [x] 1.2 Créer `Configuration/TelegramOptions.cs` avec BotToken, AuthorizedChatId
  - [x] 1.3 Créer `Configuration/PosterOptions.cs` avec ScheduleTime, RetryDelayMs, MaxRetryCount, DataPath, LogPath

- [x] Task 2 : Mettre à jour appsettings.json (AC: #1)
  - [x] 2.1 Ajouter la section "Bet2Invest" avec ApiBase, Identifier (vide), Password (vide), RequestDelayMs=500
  - [x] 2.2 Ajouter la section "Telegram" avec BotToken (vide), AuthorizedChatId=0
  - [x] 2.3 Ajouter la section "Poster" avec ScheduleTime="08:00", RetryDelayMs=60000, MaxRetryCount=3, DataPath=".", LogPath="logs"

- [x] Task 3 : Configurer Program.cs — Serilog + DI + UseSystemd (AC: #2, #3, #4, #5, #6)
  - [x] 3.1 Ajouter `builder.Services.AddSystemd()` pour intégration VPS
  - [x] 3.2 Configurer Serilog via `builder.Services.AddSerilog(lc => ...)` avec template structuré
  - [x] 3.3 Ajouter sink console + sink fichier avec RollingInterval.Day et chemin depuis PosterOptions.LogPath
  - [x] 3.4 Enregistrer IOptions<Bet2InvestOptions>, IOptions<TelegramOptions>, IOptions<PosterOptions>
  - [x] 3.5 S'assurer que la hiérarchie env vars > appsettings est preservée (built-in au Generic Host)

- [x] Task 4 : Écrire les tests de configuration (AC: #1, #2, #3)
  - [x] 4.1 Créer `tests/Bet2InvestPoster.Tests/Configuration/OptionsTests.cs`
  - [x] 4.2 Test : Bet2InvestOptions se bind correctement depuis IConfiguration avec toutes ses propriétés
  - [x] 4.3 Test : TelegramOptions se bind correctement depuis IConfiguration
  - [x] 4.4 Test : PosterOptions a les bonnes valeurs par défaut
  - [x] 4.5 Test : Configuration en couches — une source plus prioritaire surcharge la précédente (simule env vars > appsettings)

- [x] Task 5 : Valider le build et les tests (AC: tous)
  - [x] 5.1 `dotnet build Bet2InvestPoster.sln` réussit sans erreur — Build succeeded, 0 Warning, 0 Error
  - [x] 5.2 `dotnet test tests/Bet2InvestPoster.Tests` — 8/8 tests passent (7 nouveaux + 1 WorkerTests)

## Dev Notes

### Exigences Techniques Critiques pour cette Story

- **Packages requis** : déjà installés en story 1.1 (Serilog 4.3.1, Serilog.Extensions.Hosting 10.0.0, Serilog.Sinks.Console 6.1.1, Serilog.Sinks.File 7.0.0, Microsoft.Extensions.Hosting.Systemd 9.0.8)
- **Aucun nouveau package** n'est requis — tous sont déjà dans le .csproj
- **NFR5 (No credentials in logs)** : Les classes `Bet2InvestOptions` et `TelegramOptions` ne doivent JAMAIS avoir leurs propriétés `Password`/`BotToken` passées à un `ILogger`. Approche : ne jamais logguer l'objet options complet, logguer uniquement `Identifier` (non-sensible) et les valeurs de config non-sensibles.

### API Serilog 4.3.1 — Points Clés

- `AddSerilog(lc => ...)` via le package Serilog.Extensions.Hosting 10.0.0
- `Enrich.FromLogContext()` nécessaire pour que `{Step}` soit visible dans les templates
- Usage dans le code applicatif : `using (LogContext.PushProperty("Step", "Auth")) { ... }`
- Pour UseSystemd avec HostApplicationBuilder : `builder.Services.AddSystemd()` (PAS `builder.Host.UseSystemd()` — HostApplicationBuilder n'expose pas .Host)

### Template de Log Obligatoire (NFR12)

```
"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{Step}] {Message:lj} {Properties:j}{NewLine}{Exception}"
```

Steps autorisés : `Auth`, `Scrape`, `Select`, `Publish`, `Notify`, `Purge`

### Hiérarchie de Configuration (FR22)

Le Generic Host (.NET 9) applique automatiquement : env vars > appsettings.{Environment}.json > appsettings.json.
- Séparateur de section dans les env vars : `__` (double underscore)
- Exemple : `Bet2Invest__Identifier=user@example.com` surcharge `Bet2Invest.Identifier` dans appsettings.json

### Valeurs par Défaut des Options

**Bet2InvestOptions** :
- `ApiBase` = "https://api.bet2invest.com" (URL de l'API production)
- `Identifier` = "" (à surcharger par env var)
- `Password` = "" (à surcharger par env var — JAMAIS dans appsettings.json en production)
- `RequestDelayMs` = 500 (NFR8 — délai minimum entre requêtes)

**TelegramOptions** :
- `BotToken` = "" (à surcharger par env var — JAMAIS dans appsettings.json en production)
- `AuthorizedChatId` = 0 (à surcharger par env var)

**PosterOptions** :
- `ScheduleTime` = "08:00" (heure d'exécution quotidienne, format HH:mm)
- `RetryDelayMs` = 60000 (60 secondes entre retry, NFR2)
- `MaxRetryCount` = 3 (3 tentatives, FR12)
- `DataPath` = "." (répertoire des fichiers tipsters.json et history.json)
- `LogPath` = "logs" (répertoire des logs Serilog)

### Structure des Fichiers Attendue Après Cette Story

```
src/Bet2InvestPoster/
├── Configuration/
│   ├── Bet2InvestOptions.cs   ← NOUVEAU
│   ├── TelegramOptions.cs     ← NOUVEAU
│   └── PosterOptions.cs       ← NOUVEAU
├── Program.cs                 ← MODIFIÉ (Serilog + DI + UseSystemd)
└── appsettings.json           ← MODIFIÉ (sections Bet2Invest, Telegram, Poster)
tests/Bet2InvestPoster.Tests/
└── Configuration/
    └── OptionsTests.cs        ← NOUVEAU
```

### Apprentissages de Story 1.1

- La référence submodule (ProjectReference vers Exe) fonctionne sans conflit
- `dotnet build` valide que le projet compile proprement
- UnitTest1.cs a été remplacé par WorkerTests.cs (classe renommée mais fichier gardé)

### Références

- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation-Patterns] — Template Serilog, pattern DI
- [Source: .bmadOutput/planning-artifacts/architecture.md#Configuration] — Sections et valeurs
- [Source: .bmadOutput/planning-artifacts/epics.md#Story-1.2] — AC originaux

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- dotnet build Bet2InvestPoster.sln : Build succeeded, 0 Warning, 0 Error (2026-02-23)
- dotnet test tests/Bet2InvestPoster.Tests : Failed=0, Passed=8, Total=8 (2026-02-23)
- AddSystemd() via builder.Services (HostApplicationBuilder n'expose pas .Host — service collection extension utilisée)
- AddSerilog(lc => ...) via Serilog.Extensions.Hosting 10.0.0 — log path lu depuis configuration avant setup Serilog
- [code-review] dotnet build après corrections : Build succeeded, 0 Warning, 0 Error (2026-02-23)
- [code-review] dotnet test après corrections : Failed=0, Passed=11, Total=11 (2026-02-23)

### Completion Notes List

- Story créée à partir de l'epic (statut backlog → in-progress via fichier story) (2026-02-23)
- 3 classes Options créées dans Configuration/ : Bet2InvestOptions, TelegramOptions, PosterOptions (2026-02-23)
- appsettings.json mis à jour avec les 3 sections (Bet2Invest, Telegram, Poster) et valeurs par défaut (2026-02-23)
- Program.cs reconfiguré : Serilog (console + file sinks, template structuré NFR12), AddSystemd(), IOptions<T> pour 3 classes (2026-02-23)
- 7 tests de configuration écrits dans OptionsTests.cs (binding, valeurs par défaut, surcharge source prioritaire) (2026-02-23)
- Tous les ACs satisfaits : options bindées, env vars override (AC#3 vérifié par test de layering), Serilog configuré avec {Step}, credentials non exposées dans les classes (2026-02-23)
- [code-review] 7 issues corrigés (1 High + 3 Medium + 3 Low) — 11 tests au total, 0 régression (2026-02-23)

### File List

- `src/Bet2InvestPoster/Configuration/Bet2InvestOptions.cs` — NOUVEAU (+ ToString redact review)
- `src/Bet2InvestPoster/Configuration/TelegramOptions.cs` — NOUVEAU (+ ToString redact review)
- `src/Bet2InvestPoster/Configuration/PosterOptions.cs` — NOUVEAU
- `src/Bet2InvestPoster/Program.cs` — MODIFIÉ (Serilog + AddSystemd + IOptions + fast-fail validation)
- `src/Bet2InvestPoster/appsettings.json` — MODIFIÉ (sections sans Password/BotToken)
- `tests/Bet2InvestPoster.Tests/Configuration/OptionsTests.cs` — NOUVEAU (+ 3 tests review)

### Senior Developer Review (AI)

**Reviewer :** claude-sonnet-4-6 (adversarial mode)
**Date :** 2026-02-23
**Outcome :** Changes Requested → Fixed

**Résumé :** 7 issues identifiés (1 High, 3 Medium, 3 Low) — tous corrigés automatiquement

**Corrections appliquées :**
1. **[HIGH] Password/BotToken dans appsettings.json** — Supprimés de appsettings.json (NFR6 spirit)
2. **[MEDIUM] MinimumLevel.Debug() hardcodé** — Changé en `MinimumLevel.Information()`
3. **[MEDIUM] Aucune validation au démarrage** — Fast-fail dans Program.cs avant `host.Run()`
4. **[MEDIUM] `{Properties:j}` dans template console** — Templates console/fichier séparés
5. **[LOW] AC#6 mécanisme protection incomplet** — `ToString()` override avec [REDACTED] sur les 2 classes sensibles
6. **[LOW] Test AC#3 nom trompeur** — Renommé + commentaire explicatif
7. **[LOW] AuthorizedChatId non testé avec valeur négative** — Test `TelegramOptions_SupportsNegativeChatId_ForGroupAndChannelChats` ajouté

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-23 | claude-sonnet-4-6 | Création du fichier story à partir des epics (statut backlog → in-progress) |
| 2026-02-23 | claude-sonnet-4-6 | Implémentation complète — 3 Options classes, appsettings.json, Program.cs (Serilog), 7 tests — story → review |
| 2026-02-23 | claude-sonnet-4-6 (code-review) | Review adversariale — 7 issues corrigés (1H+3M+3L), 3 tests ajoutés, 11 tests total |
