# bet2invest-poster — Arbre source annoté

**Généré le :** 2026-02-25

```
bet2invest-poster/
├── .github/
│   └── workflows/
│       └── ci.yml                          # CI: checkout → .NET 9 → restore → build → test → artifacts
├── deploy/
│   └── bet2invest-poster.service           # Fichier systemd unit (sd_notify, hardened)
├── docs/                                   # Documentation projet (ce dossier)
├── jtdev-bet2invest-scraper/               # ⚠️ SUBMODULE (LECTURE SEULE)
│   ├── Api/                                # Client API bet2invest (auth, endpoints)
│   ├── Models/                             # SettledBet, Tipster, etc.
│   └── ...
├── logs/                                   # Logs Serilog (rotation quotidienne)
├── publish/                                # Artefacts de publication
│   ├── appsettings.json                    # Configuration de base
│   └── appsettings.Development.json        # Overrides développement
├── src/
│   └── Bet2InvestPoster/                   # 📦 PROJET PRINCIPAL
│       ├── Configuration/                  # Options pattern (Bet2Invest, Telegram, Poster)
│       │   ├── Bet2InvestOptions.cs         # ApiBase, Identifier, Password, RequestDelayMs
│       │   ├── TelegramOptions.cs           # BotToken, AuthorizedChatId
│       │   └── PosterOptions.cs             # ScheduleTime, Retry, Filters, SelectionMode
│       ├── Exceptions/                     # Exceptions métier
│       │   ├── Bet2InvestApiException.cs    # Erreur API (endpoint, status, payload)
│       │   └── PublishException.cs          # Erreur publication (betId, status)
│       ├── Models/                         # Modèles de données
│       │   ├── BetOrderRequest.cs           # Corps POST /v1/bankrolls/{id}/bets
│       │   ├── CircuitBreakerState.cs       # Enum: Closed, Open, HalfOpen
│       │   ├── HistoryEntry.cs              # Publication + résultat (won/lost/pending)
│       │   ├── PendingBet.cs                # Paris à venir enrichis (ROI, sport, tipster)
│       │   ├── ScrapedTipster.cs            # Tipster scraped (DTO → TipsterConfig)
│       │   └── TipsterConfig.cs             # Configuration tipster persistée (url, name)
│       ├── Properties/
│       │   └── launchSettings.json
│       ├── Services/                       # 🔧 CŒUR MÉTIER (28 fichiers)
│       │   ├── IBetPublisher.cs             # Publication paris via API
│       │   ├── BetPublisher.cs
│       │   ├── IBetSelector.cs              # Sélection (aléatoire ou intelligente)
│       │   ├── BetSelector.cs
│       │   ├── IConversationStateService.cs # État conversations Telegram multi-tour
│       │   ├── ConversationStateService.cs
│       │   ├── IExecutionStateService.cs    # État scheduling (JSON persisté)
│       │   ├── ExecutionStateService.cs
│       │   ├── IExtendedBet2InvestClient.cs # Wrapper API étendu
│       │   ├── ExtendedBet2InvestClient.cs
│       │   ├── IHistoryManager.cs           # Gestion history.json (CRUD atomique)
│       │   ├── HistoryManager.cs
│       │   ├── INotificationService.cs      # Notifications Telegram
│       │   ├── NotificationService.cs
│       │   ├── IOnboardingService.cs        # Onboarding premier lancement
│       │   ├── OnboardingService.cs
│       │   ├── IPostingCycleService.cs      # Orchestrateur cycle complet
│       │   ├── PostingCycleService.cs
│       │   ├── IResiliencePipelineService.cs # Polly retry + circuit breaker
│       │   ├── ResiliencePipelineService.cs
│       │   ├── IResultTracker.cs            # Vérification résultats settled bets
│       │   ├── ResultTracker.cs
│       │   ├── ITipsterService.cs           # CRUD tipsters.json
│       │   ├── TipsterService.cs
│       │   ├── IUpcomingBetsFetcher.cs      # Agrégation paris par tipster
│       │   ├── UpcomingBetsFetcher.cs
│       │   ├── Bet2InvestHealthCheck.cs     # Health check endpoint
│       │   └── SerilogConsoleLoggerAdapter.cs
│       ├── Telegram/                       # 📱 INTERFACE TELEGRAM
│       │   ├── AuthorizationFilter.cs       # Filtre ChatId autorisé
│       │   ├── TelegramBotService.cs        # Long polling + dispatch commandes
│       │   ├── Commands/                   # 8 command handlers
│       │   │   ├── ICommandHandler.cs       # Interface commune
│       │   │   ├── RunCommandHandler.cs     # /run — exécution manuelle
│       │   │   ├── StatusCommandHandler.cs  # /status — état du service
│       │   │   ├── StartCommandHandler.cs   # /start — activer scheduling
│       │   │   ├── StopCommandHandler.cs    # /stop — suspendre scheduling
│       │   │   ├── HistoryCommandHandler.cs # /history — dernières publications
│       │   │   ├── ScheduleCommandHandler.cs # /schedule — configurer heure
│       │   │   ├── TipstersCommandHandler.cs # /tipsters — CRUD tipsters
│       │   │   └── ReportCommandHandler.cs  # /report — tableau de bord
│       │   └── Formatters/
│       │       ├── IMessageFormatter.cs     # Interface formatage messages
│       │       └── MessageFormatter.cs      # Formatage status, history, report, etc.
│       ├── Workers/
│       │   └── SchedulerWorker.cs           # BackgroundService (cron quotidien)
│       ├── Program.cs                      # 🚀 ENTRY POINT (DI, Serilog, validation)
│       └── Bet2InvestPoster.csproj         # Projet (.NET 9, packages, submodule ref)
├── tests/
│   └── Bet2InvestPoster.Tests/             # 🧪 TESTS XUNIT (31 fichiers)
│       ├── Configuration/
│       │   └── OptionsTests.cs
│       ├── Helpers/
│       │   └── FakeNotificationService.cs
│       ├── Models/
│       │   └── TipsterConfigTests.cs
│       ├── Services/                       # Tests services (15 fichiers)
│       │   ├── BetPublisherTests.cs
│       │   ├── BetSelectorTests.cs
│       │   ├── HistoryManagerTests.cs
│       │   ├── PostingCycleServiceTests.cs
│       │   ├── ResultTrackerTests.cs
│       │   └── ...
│       ├── Telegram/                       # Tests Telegram (11 fichiers)
│       │   ├── AuthorizationFilterTests.cs
│       │   ├── Commands/
│       │   │   ├── FakeTelegramBotClient.cs
│       │   │   ├── ReportCommandHandlerTests.cs
│       │   │   └── ...
│       │   └── Formatters/
│       │       └── MessageFormatterTests.cs
│       ├── Workers/
│       │   ├── SchedulerWorkerTests.cs
│       │   └── SchedulerWorkerPollyTests.cs
│       └── Bet2InvestPoster.Tests.csproj
├── Bet2InvestPoster.sln                    # Solution .NET
├── .env                                    # Variables d'environnement locales
├── .gitmodules                             # Référence submodule scraper
├── app.run.sh                              # Script lancement local
└── tipsters.json                           # Configuration tipsters (données)
```
