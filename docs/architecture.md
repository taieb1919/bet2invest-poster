# bet2invest-poster — Architecture

**Généré le :** 2026-02-25
**Type :** Backend Worker Service
**Pattern :** Service-oriented avec orchestrateur central + Command pattern Telegram

## Vue d'ensemble

Le système suit une architecture en couches avec un orchestrateur central (`PostingCycleService`) qui coordonne le cycle de publication. L'interface utilisateur est un bot Telegram avec un pattern Command Handler. La persistance est basée sur des fichiers JSON avec écriture atomique.

## Couches architecturales

```
┌─────────────────────────────────────────────────────────┐
│                    PRESENTATION                          │
│  TelegramBotService (polling) + AuthorizationFilter      │
│  CommandHandlers: run, status, start, stop, history,     │
│                   schedule, tipsters, report              │
│  MessageFormatter (formatage des réponses Telegram)      │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                    ORCHESTRATION                          │
│  SchedulerWorker (BackgroundService, planification)       │
│  PostingCycleService (cycle complet)                     │
│  ResiliencePipelineService (Polly retry + CB)            │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                    SERVICES MÉTIER                        │
│  TipsterService — CRUD tipsters.json                     │
│  UpcomingBetsFetcher — agrégation paris par tipster       │
│  BetSelector — filtrage + sélection (aléatoire/intel.)   │
│  BetPublisher — publication via API + enrichissement     │
│  ResultTracker — vérification résultats via settled bets │
│  NotificationService — notifications Telegram            │
│  OnboardingService — guide premier lancement             │
│  HistoryManager — persistance history.json               │
│  ExecutionStateService — état scheduling                 │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                    INFRASTRUCTURE                        │
│  ExtendedBet2InvestClient — wrapper API bet2invest       │
│  jtdev-bet2invest-scraper (submodule, lecture seule)     │
│  Serilog (logging structuré)                             │
│  Polly.Core (résilience)                                 │
│  Telegram.Bot (SDK bot)                                  │
└─────────────────────────────────────────────────────────┘
```

## Cycle de publication (PostingCycleService)

```
RunCycleAsync()
│
├── 1. Purge (HistoryManager) — suppression entrées > 30j
├── 2. Result Tracking (ResultTracker) — vérification résultats settled bets
├── 3. Load Tipsters (TipsterService) — lecture tipsters.json
├── 4. Resolve IDs (ExtendedBet2InvestClient) — résolution NumericId tipsters
├── 5. Fetch Upcoming (UpcomingBetsFetcher) — agrégation paris par tipster
├── 6. Select (BetSelector) — filtrage doublons + cotes + horaire + sélection
├── 7. Publish (BetPublisher) — POST API + enrichissement HistoryEntry
├── 8. Notify (NotificationService) — notification Telegram succès/échec
└── 9. Log résumé (Step: Publish)
```

## Pattern Command Handler (Telegram)

```csharp
interface ICommandHandler
{
    bool CanHandle(string command);
    Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct);
}
```

Toutes les commandes implémentent `ICommandHandler` et sont enregistrées en DI (Singleton). `TelegramBotService` dispatche via `CanHandle()`.

| Commande | Handler | Description |
|----------|---------|-------------|
| `/run` | RunCommandHandler | Exécution manuelle du cycle |
| `/status` | StatusCommandHandler | État du service |
| `/start` | StartCommandHandler | Activer le scheduling |
| `/stop` | StopCommandHandler | Suspendre le scheduling |
| `/history` | HistoryCommandHandler | 7 dernières publications |
| `/schedule` | ScheduleCommandHandler | Configurer l'heure |
| `/tipsters` | TipstersCommandHandler | CRUD tipsters |
| `/report` | ReportCommandHandler | Tableau de bord performances |

## Persistance (fichiers JSON)

Tous les fichiers de données utilisent l'écriture atomique (write-to-temp + rename) protégée par `SemaphoreSlim`.

| Fichier | Service responsable | Contenu |
|---------|--------------------|---------|
| `history.json` | HistoryManager | Liste HistoryEntry (betId, result, odds, sport, tipsterName, publishedAt) |
| `tipsters.json` | TipsterService | Liste TipsterConfig (url, name) |
| `scheduling-state.json` | ExecutionStateService | SchedulingEnabled, LastRunUtc, NextRunUtc, ScheduleTime |

## Résilience (Polly)

- **Retry** : `MaxRetryCount` tentatives avec `RetryDelayMs` entre chaque
- **Circuit Breaker** : seuil configurable (`CircuitBreakerFailureThreshold`), durée d'ouverture (`CircuitBreakerDurationSeconds`)
- **Health Check** : endpoint HTTP sur `HealthCheckPort` (défaut 8080)

## Injection de dépendances

| Lifetime | Services |
|----------|----------|
| **Singleton** | TimeProvider, Bet2InvestClient, HistoryManager, AuthorizationFilter, TelegramBotClient, NotificationService, ExecutionStateService, MessageFormatter, ConversationStateService, OnboardingService, CommandHandlers, ResiliencePipelineService |
| **Scoped** | ExtendedBet2InvestClient, TipsterService, UpcomingBetsFetcher, BetSelector, BetPublisher, ResultTracker, PostingCycleService |
| **HostedService** | SchedulerWorker, TelegramBotService |

Les services Scoped sont créés par cycle d'exécution (`IServiceScopeFactory.CreateScope()`), ce qui garantit un état frais à chaque run.

## Sécurité

- **Autorisation Telegram** : `AuthorizationFilter` vérifie le `ChatId` autorisé (unique utilisateur)
- **Credentials** : via `EnvironmentFile` systemd (jamais en clair dans le code)
- **systemd hardening** : PrivateTmp, NoNewPrivileges, ProtectSystem=strict, ProtectHome
- **Rate limiting API** : 500ms minimum entre requêtes (`RequestDelayMs`)

## Submodule

Le scraper `jtdev-bet2invest-scraper` est référencé comme `ProjectReference` et fournit :
- `Bet2InvestClient` : authentification + endpoints API
- `SettledBet`, `PendingBet` : modèles de données
- `GetSettledBetsAsync()` : récupération des paris résolus

**Règle absolue** : ne JAMAIS modifier les fichiers du submodule. Créer des wrappers dans `ExtendedBet2InvestClient` si nécessaire.
