---
stepsCompleted:
  - step-01-init
  - step-02-context
  - step-03-starter
  - step-04-decisions
  - step-05-patterns
  - step-06-structure
  - step-07-validation
  - step-08-complete
lastStep: 8
status: 'complete'
completedAt: '2026-02-23'
inputDocuments:
  - .bmadOutput/planning-artifacts/prd.md
  - jtdev-bet2invest-scraper/README.md
workflowType: 'architecture'
project_name: 'bet2invest-poster'
user_name: 'taieb'
date: '2026-02-23'
---

# Architecture Decision Document

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Project Context Analysis

### Requirements Overview

**Functional Requirements:**
23 FRs en 7 groupes couvrant le cycle complet : authentification API → scraping paris à venir → sélection aléatoire → publication → notification. Le bot Telegram sert d'interface de contrôle (/run, /status) et de canal de notification (succès/échec). La sécurité se limite à la restriction par chat ID.

**Non-Functional Requirements:**
12 NFRs qui façonnent l'architecture :
- Fiabilité : redémarrage auto <30s, taux succès >95%, écriture atomique history.json
- Sécurité : credentials jamais dans les logs, env vars en production
- Intégration : 500ms entre requêtes API, détection proactive des changements d'API, retry Telegram avec backoff
- Maintenabilité : module API isolé, logs structurés (timestamp, étape, tipster, code erreur)

**Scale & Complexity:**

- Primary domain : Backend service (.NET 9)
- Complexity level : Low-medium
- Estimated architectural components : 6-8 (API client, scraper, selector, publisher, scheduler, Telegram bot, history manager, config)

### Technical Constraints & Dependencies

- **.NET 9 / C#** imposé par cohérence avec le scraper submodule existant
- **Submodule `jtdev-bet2invest-scraper`** — réutilisation du `Bet2InvestClient` (auth, appels API, modèles). Le scraper gère uniquement `SettledBets` (paris résolus) et `GetTipstersAsync()` (liste paginée)
- **Nouveau développement requis** — Deux endpoints API non couverts par le scraper : (1) récupération des paris à venir (non résolus) par tipster, (2) publication de pronostics sur le compte utilisateur
- **Rate limiting** — 500ms minimum entre chaque requête API bet2invest
- **Déploiement VPS** — Service systemd, pas de dépendance cloud
- **Utilisateur unique** — Pas de multi-tenancy, pas de scaling horizontal

### Cross-Cutting Concerns Identified

- **Gestion d'erreurs et retry** — Traverse tout le cycle (auth, scrape, publish, notify). 3 tentatives avec délai configurable (60s)
- **Logging structuré** — Chaque composant doit loguer avec timestamp, étape du cycle, et contexte. Les credentials ne doivent jamais apparaître
- **Notifications Telegram** — Canal de sortie partagé entre le scheduler (notifications auto) et le bot (réponses aux commandes)
- **Rate limiting API** — Toute interaction avec l'API bet2invest respecte le délai de 500ms
- **Configuration** — Hiérarchie env vars > appsettings.json partagée par tous les composants

## Starter Template Evaluation

### Primary Technology Domain

Worker Service .NET 9 (backend service long-running avec bot Telegram) — basé sur l'analyse du PRD et la cohérence avec le scraper submodule existant.

### Starter Options Considered

| Option | Évaluation |
|---|---|
| `dotnet new worker` | Template officiel Microsoft. Fournit BackgroundService, Generic Host, DI, config, logging. Match parfait. |
| `dotnet new console` | Trop basique. Nécessite setup manuel du host builder, DI, et configuration. |
| Clean Architecture Worker Service (ardalis) | Over-engineering. Pattern Clean Architecture ajoute de la complexité inutile pour un projet single-user de complexité low-medium. |

### Selected Starter: dotnet new worker

**Rationale for Selection:**
Le Worker Service template est le starter idéal car il fournit exactement l'infrastructure requise par le PRD : BackgroundService pour le scheduling quotidien, Generic Host pour la DI et la configuration (appsettings.json + env vars), et logging intégré. Aucune surcouche inutile.

**Initialization Command:**

```bash
dotnet new worker -n Bet2InvestPoster --framework net9.0
```

**Architectural Decisions Provided by Starter:**

**Language & Runtime:**
C# 13 / .NET 9 — top-level statements, implicit usings, nullable reference types activés par défaut

**Build Tooling:**
MSBuild / `dotnet build` — compilation standard .NET SDK

**Code Organization:**
- `Program.cs` — point d'entrée avec host builder
- `Worker.cs` — BackgroundService de base
- `appsettings.json` / `appsettings.Development.json` — configuration

**Development Experience:**
- `dotnet run` / `dotnet watch` pour le développement
- Logging intégré (console, debug)
- Configuration hot-reload

**NuGet Packages à Ajouter:**

| Package | Version | Usage |
|---|---|---|
| Telegram.Bot | 22.9.0 | Interface bot Telegram (Bot API 9.4) |
| Microsoft.Extensions.Hosting.Systemd | 9.0.x | Intégration systemd pour déploiement VPS |

**Référence Projet:**
- `jtdev-bet2invest-scraper` (submodule) — réutilisation de Bet2InvestClient, modèles, et infrastructure API

**Note:** L'initialisation du projet avec cette commande devrait être la première story d'implémentation.

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (Block Implementation):**
- Référence projet directe vers le submodule scraper
- Polly.Core 8.6.5 pour la résilience (retry cycle complet)
- Serilog 4.3.1 pour le logging structuré
- Écriture atomique history.json (write-to-temp + rename)

**Important Decisions (Shape Architecture):**
- Solution avec 2 projets (principal + tests xUnit)
- GitHub Actions pour CI (build + test)
- Purge history.json > 30 jours

**Deferred Decisions (Post-MVP):**
- Déploiement automatisé sur VPS (manuel pour le MVP)
- Rotation des logs Serilog (post-MVP)

### Data Architecture

**Stockage :** Fichiers JSON uniquement — pas de base de données
- `tipsters.json` — tableau de liens/IDs tipsters, lecture seule, relu à chaque exécution
- `history.json` — historique des pronostics publiés, lecture/écriture atomique (write-to-temp + rename)
- **Purge :** Entrées > 30 jours supprimées automatiquement à chaque exécution

### Authentication & Security

**API bet2invest :** Bearer token via `Bet2InvestClient.LoginAsync()` (réutilisation submodule). Renouvellement automatique si expiré (FR3).
**Bot Telegram :** Restriction par chat ID autorisé (FR19-FR20). Credentials exclusivement en env vars en production (NFR6). Jamais dans les logs (NFR5).

### API & Communication Patterns

**Résilience :** Polly.Core 8.6.5 — retry policy pour le cycle complet (3 tentatives, 60s délai configurable)
**Rate limiting :** 500ms entre chaque requête API bet2invest (conservé du scraper)
**Error handling :** Erreurs identifiables avec code, endpoint, HTTP status. Notification Telegram en cas d'échec.

### Infrastructure & Deployment

**Hosting :** VPS avec systemd (`Microsoft.Extensions.Hosting.Systemd`)
**Logging :** Serilog 4.3.1 — sinks console + fichier. Logs structurés : timestamp, étape du cycle (auth/scrape/select/publish), tipster, code erreur.
**CI/CD :** GitHub Actions (build + test automatisés). Déploiement manuel (`dotnet publish` + SCP/SSH) pour le MVP.
**Tests :** xUnit — projet séparé `Bet2InvestPoster.Tests`

### Project Structure

```
Bet2InvestPoster.sln
├── src/
│   └── Bet2InvestPoster/          # Projet principal (Worker Service)
│       ├── Program.cs
│       ├── appsettings.json
│       └── Bet2InvestPoster.csproj  # Ref vers submodule scraper
├── tests/
│   └── Bet2InvestPoster.Tests/     # Tests xUnit
│       └── Bet2InvestPoster.Tests.csproj
├── jtdev-bet2invest-scraper/       # Submodule Git
├── tipsters.json
└── history.json
```

### Decision Impact Analysis

**Implementation Sequence:**
1. Init projet (`dotnet new worker`) + référence submodule
2. Configuration (appsettings.json, DI, Serilog)
3. Extension Bet2InvestClient (upcoming bets + publish)
4. Logique de sélection + history manager
5. Scheduler (BackgroundService)
6. Bot Telegram (/run, /status, notifications)
7. Polly retry policies
8. Tests xUnit
9. GitHub Actions CI
10. Déploiement VPS + systemd

**Cross-Component Dependencies:**
- Serilog traverse tous les composants (DI via ILogger<T>)
- Polly wraps les appels API bet2invest et le cycle complet
- Configuration partagée par tous via IConfiguration/IOptions
- Notification Telegram utilisée par le scheduler et le error handler

### NuGet Packages Summary

| Package | Version | Usage |
|---|---|---|
| Telegram.Bot | 22.9.0 | Interface bot Telegram (Bot API 9.4) |
| Microsoft.Extensions.Hosting.Systemd | 9.0.x | Intégration systemd |
| Polly.Core | 8.6.5 | Résilience et retry |
| Serilog | 4.3.1 | Logging structuré |
| Serilog.Extensions.Hosting | latest | Intégration Generic Host |
| Serilog.Sinks.Console | latest | Logs console |
| Serilog.Sinks.File | latest | Logs fichier avec rotation |
| xunit | latest | Framework de tests |
| xunit.runner.visualstudio | latest | Test runner |
| Microsoft.NET.Test.Sdk | latest | SDK tests .NET |

## Implementation Patterns & Consistency Rules

### Pattern Categories Defined

**Critical Conflict Points Identified:** 6 catégories où les agents IA pourraient faire des choix différents

### Naming Patterns

**C# Naming Conventions (Microsoft Standard) :**
- Classes / Méthodes : `PascalCase` — `HistoryManager.PurgeOldEntries()`
- Variables locales / Paramètres : `camelCase` — `var selectedTips`
- Interfaces : Préfixe `I` — `IBet2InvestClient`, `IHistoryManager`
- Fichiers : Un fichier = une classe, nom du fichier = nom de la classe
- Dossiers : `PascalCase` — `Services/`, `Models/`, `Configuration/`
- Constantes : `PascalCase` — `MaxRetryCount`

**JSON Property Naming :**
- `camelCase` — cohérent avec `System.Text.Json` defaults
- Dates en ISO 8601 : `"2026-02-23T08:00:00Z"`

### Structure Patterns

**Project Organization (par type) :**

```
src/Bet2InvestPoster/
├── Configuration/       # Options classes (PosterOptions, TelegramOptions, etc.)
├── Models/              # DTOs et modèles de données (Tipster, Bet, HistoryEntry)
├── Services/            # Logique métier (HistoryManager, BetSelector, BetPublisher)
├── Telegram/            # Bot handler, commandes (/run, /status)
├── Workers/             # BackgroundServices (SchedulerWorker, TelegramBotWorker)
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

**Rules :**
- Chaque service a son interface (`IHistoryManager`, `IBetSelector`, etc.)
- Les Workers ne contiennent que l'orchestration — la logique métier est dans Services/
- Configuration/ contient les classes `IOptions<T>` mappées depuis appsettings.json
- Les modèles du scraper submodule sont réutilisés directement, pas dupliqués

### Format Patterns

**JSON Data Files :**

tipsters.json :
```json
[
  { "url": "https://bet2invest.com/tipster/123", "name": "TipsterName" }
]
```

history.json :
```json
[
  {
    "betId": "abc-123",
    "tipsterUrl": "https://bet2invest.com/tipster/123",
    "publishedAt": "2026-02-23T08:00:00Z",
    "matchDescription": "PSG vs OM - Over 2.5"
  }
]
```

**Telegram Message Formats :**
- Succès : `"✅ {count} pronostics publiés avec succès."`
- Échec : `"❌ Échec — {raison}. {détails retry}."`
- Status : Bloc formaté avec dernière exécution, prochain run, nombre publiés, état API

**Serilog Template :**
- Format : `"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] [{Step}] {Message} {Properties}"`
- Steps autorisés : `Auth`, `Scrape`, `Select`, `Publish`, `Notify`, `Purge`
- JAMAIS de credentials dans les properties de log

### Process Patterns

**Error Handling :**
- Exceptions custom : `Bet2InvestApiException` (erreurs API), `PublishException` (erreurs publication)
- Toutes les exceptions catchées au niveau du Worker, loguées via Serilog, notifiées via Telegram
- Jamais de catch silencieux — chaque erreur est loguée

**Retry (Polly) :**
- `ResiliencePipeline` configuré dans DI
- 3 tentatives, délai 60s configurable via `PosterOptions.RetryDelayMs`
- Le pipeline wrap le cycle complet (scrape → select → publish), pas chaque appel individuel

**Authentication Flow :**
- Login au début de chaque cycle d'exécution
- Si token expiré mid-cycle : re-login automatique + retry de l'appel
- Token en mémoire uniquement (pas persisté)

**DI Pattern :**
- Enregistrement dans Program.cs via IServiceCollection
- Services en Scoped (un scope par cycle d'exécution)
- Bet2InvestClient en Singleton (réutilisé entre cycles)

### Enforcement Guidelines

**All AI Agents MUST:**
- Suivre les conventions de nommage C# Microsoft standard (PascalCase classes, camelCase locals)
- Créer une interface pour chaque service
- Placer la logique métier dans Services/, jamais dans Workers/
- Loguer chaque opération avec le Step correspondant (Auth/Scrape/Select/Publish/Notify/Purge)
- Ne jamais loguer de credentials ou tokens
- Utiliser System.Text.Json (pas Newtonsoft) pour la sérialisation
- Utiliser IOptions<T> pour accéder à la configuration

## Project Structure & Boundaries

### Requirements to Structure Mapping

| FR Category | Composant | Emplacement |
|---|---|---|
| Auth (FR1-FR3) | `Bet2InvestClient` (submodule) | `jtdev-bet2invest-scraper/` |
| Scraping (FR4-FR6) | `TipsterService`, `UpcomingBetsFetcher` | `Services/` |
| Sélection (FR7-FR8) | `BetSelector`, `HistoryManager` | `Services/` |
| Publication (FR9-FR10) | `BetPublisher`, `HistoryManager` | `Services/` |
| Scheduling (FR11-FR13) | `SchedulerWorker` | `Workers/` |
| Telegram Commands (FR14-FR15) | `TelegramBotService`, `CommandHandler` | `Telegram/` |
| Telegram Notifications (FR16-FR18) | `NotificationService` | `Services/` |
| Sécurité (FR19-FR20) | `AuthorizationFilter` | `Telegram/` |
| Config (FR21-FR23) | `PosterOptions`, `TelegramOptions` | `Configuration/` |

### Complete Project Directory Structure

```
Bet2InvestPoster.sln
├── .github/
│   └── workflows/
│       └── ci.yml                          # GitHub Actions: build + test
├── src/
│   └── Bet2InvestPoster/
│       ├── Bet2InvestPoster.csproj          # Worker Service + refs
│       ├── Program.cs                       # Host builder, DI, Serilog setup
│       ├── appsettings.json                 # Config par défaut
│       ├── appsettings.Development.json     # Config dev
│       ├── Configuration/
│       │   ├── Bet2InvestOptions.cs          # IOptions<T> pour section Bet2Invest
│       │   ├── TelegramOptions.cs            # IOptions<T> pour section Telegram
│       │   └── PosterOptions.cs              # IOptions<T> pour section Poster
│       ├── Models/
│       │   ├── HistoryEntry.cs               # Entrée history.json
│       │   └── TipsterConfig.cs              # Entrée tipsters.json
│       ├── Services/
│       │   ├── ITipsterService.cs            # Lecture tipsters.json
│       │   ├── TipsterService.cs
│       │   ├── IUpcomingBetsFetcher.cs       # Récupération paris à venir
│       │   ├── UpcomingBetsFetcher.cs
│       │   ├── IBetSelector.cs               # Sélection aléatoire 5/10/15
│       │   ├── BetSelector.cs
│       │   ├── IBetPublisher.cs              # Publication via API
│       │   ├── BetPublisher.cs
│       │   ├── IHistoryManager.cs            # Lecture/écriture/purge history.json
│       │   ├── HistoryManager.cs
│       │   ├── INotificationService.cs       # Envoi notifications Telegram
│       │   ├── NotificationService.cs
│       │   ├── IPostingCycleService.cs       # Orchestration du cycle complet
│       │   └── PostingCycleService.cs
│       ├── Telegram/
│       │   ├── TelegramBotService.cs         # Polling + dispatch commandes
│       │   ├── AuthorizationFilter.cs        # Filtrage chat ID
│       │   ├── Commands/
│       │   │   ├── ICommandHandler.cs
│       │   │   ├── RunCommandHandler.cs      # /run
│       │   │   └── StatusCommandHandler.cs   # /status
│       │   └── Formatters/
│       │       └── MessageFormatter.cs       # Formatage messages Telegram
│       ├── Workers/
│       │   └── SchedulerWorker.cs            # BackgroundService scheduling quotidien
│       └── Exceptions/
│           ├── Bet2InvestApiException.cs
│           └── PublishException.cs
├── tests/
│   └── Bet2InvestPoster.Tests/
│       ├── Bet2InvestPoster.Tests.csproj
│       ├── Services/
│       │   ├── BetSelectorTests.cs
│       │   ├── HistoryManagerTests.cs
│       │   └── PostingCycleServiceTests.cs
│       └── Telegram/
│           └── AuthorizationFilterTests.cs
├── jtdev-bet2invest-scraper/                 # Submodule Git (existant)
├── .gitignore
├── .gitmodules
├── tipsters.json                             # Config tipsters (éditable)
└── deploy/
    └── bet2invest-poster.service             # Fichier systemd unit
```

### Architectural Boundaries

**API Boundary (externe) :**
- `Bet2InvestClient` (submodule) — seul point de contact avec l'API bet2invest
- `UpcomingBetsFetcher` et `BetPublisher` utilisent le client, jamais d'appel HTTP direct ailleurs
- Rate limiting (500ms) appliqué dans le client

**Telegram Boundary :**
- `TelegramBotService` — seul point de contact avec l'API Telegram
- `AuthorizationFilter` — gate d'entrée, filtre les chat IDs non autorisés
- `NotificationService` — seul service autorisé à envoyer des messages sortants

**Data Boundary :**
- `HistoryManager` — seul composant qui lit/écrit `history.json` (écriture atomique)
- `TipsterService` — seul composant qui lit `tipsters.json`
- Pas d'accès fichier direct depuis les Workers ou Telegram

**Orchestration Boundary :**
- `PostingCycleService` orchestre le cycle complet : fetch → select → publish → record → notify
- `SchedulerWorker` déclenche le cycle via `PostingCycleService`, ne contient pas de logique métier
- Commande `/run` appelle aussi `PostingCycleService`

### Data Flow

```
tipsters.json → TipsterService → UpcomingBetsFetcher → BetSelector → BetPublisher → HistoryManager → history.json
                                       ↓                                    ↓
                                  Bet2InvestClient                   NotificationService
                                  (API bet2invest)                   (API Telegram)
```

### External Integrations

| Service | Point d'intégration | Pattern |
|---|---|---|
| API bet2invest | `Bet2InvestClient` (submodule) | HTTP + Bearer token, 500ms rate limit |
| API Telegram | `Telegram.Bot` via `TelegramBotService` | Long polling, retry backoff |

### Deployment Structure

```bash
# Build
dotnet publish src/Bet2InvestPoster -c Release -o ./publish

# Fichiers déployés sur VPS
/opt/bet2invest-poster/
├── Bet2InvestPoster.dll          # Application
├── appsettings.json              # Config (credentials via env vars)
├── tipsters.json                 # Liste tipsters
├── history.json                  # Historique (auto-créé)
└── logs/                         # Logs Serilog (auto-créé)
```

### Development Workflow

**Développement local :**
```bash
dotnet run --project src/Bet2InvestPoster
```

**Tests :**
```bash
dotnet test tests/Bet2InvestPoster.Tests
```

**CI (GitHub Actions) :**
- Trigger : push/PR sur main
- Steps : restore → build → test
- Déploiement : manuel via `dotnet publish` + SCP/SSH

## Architecture Validation Results

### Coherence Validation ✅

**Decision Compatibility:**
Toutes les technologies sont compatibles .NET 9 : Polly.Core 8.6.5, Serilog 4.3.1, Telegram.Bot 22.9.0, Microsoft.Extensions.Hosting.Systemd, xUnit. Aucun conflit de version. Tous les packages sont disponibles sur NuGet pour net9.0.

**Pattern Consistency:**
Conventions de nommage C# standard appliquées uniformément. Pattern interface-per-service cohérent avec DI Generic Host. IOptions<T> aligné avec la hiérarchie de configuration. Serilog template structuré conforme au NFR12.

**Structure Alignment:**
La structure par type (Services/, Configuration/, Telegram/, Workers/) supporte tous les patterns définis. Les boundaries (API, Telegram, Data, Orchestration) sont respectés par la structure de fichiers. Le projet de tests miroir la structure source.

### Requirements Coverage Validation ✅

**Functional Requirements Coverage (23/23) :**

| FR Group | FRs | Composant Architectural | Status |
|---|---|---|---|
| Auth | FR1-FR3 | Bet2InvestClient (submodule) + ExtendedBet2InvestClient | ✅ |
| Scraping | FR4-FR6 | TipsterService + UpcomingBetsFetcher | ✅ |
| Sélection | FR7-FR8 | BetSelector + HistoryManager | ✅ |
| Publication | FR9-FR10 | BetPublisher + HistoryManager | ✅ |
| Scheduling | FR11-FR13 | SchedulerWorker + PosterOptions | ✅ |
| Commands | FR14-FR15 | RunCommandHandler + StatusCommandHandler | ✅ |
| Notifications | FR16-FR18 | NotificationService | ✅ |
| Sécurité | FR19-FR20 | AuthorizationFilter | ✅ |
| Config | FR21-FR23 | Configuration/ + Program.cs | ✅ |

**Non-Functional Requirements Coverage (12/12) :**

| NFR | Requirement | Support Architectural | Status |
|---|---|---|---|
| NFR1 | Restart <30s | systemd unit (deploy/) | ✅ |
| NFR2 | Succès >95% | Polly retry + error handling | ✅ |
| NFR3 | Notification <5min | NotificationService | ✅ |
| NFR4 | Atomic writes | HistoryManager (write-to-temp + rename) | ✅ |
| NFR5 | No credentials in logs | Enforcement guidelines | ✅ |
| NFR6 | Env vars production | Configuration hierarchy | ✅ |
| NFR7 | 100% chat ID rejection | AuthorizationFilter | ✅ |
| NFR8 | 500ms delay | Rate limiting Bet2InvestClient | ✅ |
| NFR9 | Error identification | Bet2InvestApiException + structured logs | ✅ |
| NFR10 | Telegram retry | TelegramBotService backoff | ✅ |
| NFR11 | Isolated API module | Services/ + submodule boundary | ✅ |
| NFR12 | Structured logs | Serilog template with steps | ✅ |

### Implementation Readiness Validation ✅

**Decision Completeness:** Toutes les décisions critiques documentées avec versions vérifiées.
**Structure Completeness:** Arborescence complète avec tous les fichiers, dossiers, et composants nommés.
**Pattern Completeness:** Naming, format, process, et enforcement guidelines couvrent tous les points de conflit identifiés.

### Gap Analysis Results

**Critical Gaps:** 0

**Important Gaps Addressed:**
- Intégration submodule → Résolu : `ExtendedBet2InvestClient` (wrapper) dans `Services/` du projet poster. Ne modifie pas le submodule. Hérite ou compose `Bet2InvestClient` pour ajouter `GetUpcomingBetsAsync()` et `PublishBetAsync()`.

**Nice-to-Have (Post-MVP):**
- Configuration Polly détaillée (backoff exponentiel vs fixe)
- Format détaillé du message /status
- Rotation des logs Serilog

### Architecture Completeness Checklist

**✅ Requirements Analysis**
- [x] Project context thoroughly analyzed
- [x] Scale and complexity assessed (low-medium)
- [x] Technical constraints identified (.NET 9, submodule, VPS)
- [x] Cross-cutting concerns mapped (retry, logging, notifications, rate limiting, config)

**✅ Architectural Decisions**
- [x] Critical decisions documented with versions
- [x] Technology stack fully specified (9 NuGet packages)
- [x] Integration patterns defined (wrapper submodule, Polly, Serilog)
- [x] Data architecture defined (JSON files, atomic writes, 30-day purge)

**✅ Implementation Patterns**
- [x] Naming conventions established (C# standard + JSON camelCase)
- [x] Structure patterns defined (par type, interface-per-service)
- [x] Format patterns specified (JSON, Telegram messages, Serilog)
- [x] Process patterns documented (error handling, retry, auth, DI)

**✅ Project Structure**
- [x] Complete directory structure defined (30+ fichiers)
- [x] Component boundaries established (4 boundaries)
- [x] Integration points mapped (2 external services)
- [x] Requirements to structure mapping complete (23 FRs → composants)

### Architecture Readiness Assessment

**Overall Status:** READY FOR IMPLEMENTATION

**Confidence Level:** High

**Key Strengths:**
- Stack cohérente et minimaliste — pas de surcouche inutile
- Boundaries clairs qui isolent les dépendances externes
- PostingCycleService comme orchestrateur central simplifie le flow
- Wrapper submodule protège contre les changements du scraper
- 100% des FRs et NFRs couverts architecturalement

**Areas for Future Enhancement:**
- Configuration Polly avancée (circuit breaker, backoff exponentiel)
- Monitoring plus poussé (health checks endpoint)
- Déploiement automatisé via CI/CD

### Implementation Handoff

**AI Agent Guidelines:**
- Suivre toutes les décisions architecturales exactement comme documenté
- Utiliser les patterns d'implémentation de manière cohérente
- Respecter les boundaries et la structure projet
- Référer à ce document pour toute question architecturale

**First Implementation Priority:**
1. `dotnet new worker -n Bet2InvestPoster --framework net9.0`
2. Ajouter les NuGet packages
3. Référencer le submodule scraper
4. Créer `ExtendedBet2InvestClient` (spike API pour valider les endpoints upcoming bets + publish)
