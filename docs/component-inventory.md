# bet2invest-poster — Inventaire des composants

**Généré le :** 2026-02-25

## Services métier

| Service | Interface | Lifetime | Responsabilité |
|---------|-----------|----------|---------------|
| PostingCycleService | IPostingCycleService | Scoped | Orchestrateur du cycle complet (purge → fetch → select → publish → track → notify) |
| ExtendedBet2InvestClient | IExtendedBet2InvestClient | Scoped | Wrapper API bet2invest (upcoming bets, publish, resolve tipster IDs, settled bets) |
| TipsterService | ITipsterService | Scoped | CRUD tipsters.json (load, add, remove, replace) |
| UpcomingBetsFetcher | IUpcomingBetsFetcher | Scoped | Agrégation des paris à venir par tipster |
| BetSelector | IBetSelector | Scoped | Filtrage doublons + cotes + horaire, sélection aléatoire ou intelligente |
| BetPublisher | IBetPublisher | Scoped | Publication paris via POST API + enrichissement HistoryEntry |
| ResultTracker | IResultTracker | Scoped | Vérification résultats via settled bets, mise à jour history |
| HistoryManager | IHistoryManager | Singleton | Persistance history.json (écriture atomique, déduplication, purge) |
| ExecutionStateService | IExecutionStateService | Singleton | État scheduling (scheduling-state.json) |
| NotificationService | INotificationService | Singleton | Envoi notifications Telegram (succès, échec, onboarding) |
| OnboardingService | IOnboardingService | Singleton | Guide premier lancement via Telegram |
| ConversationStateService | IConversationStateService | Singleton | État conversations multi-tour (/tipsters add, remove) |
| ResiliencePipelineService | IResiliencePipelineService | Singleton | Pipeline Polly retry + circuit breaker |
| Bet2InvestHealthCheck | IHealthCheck | Singleton | Health check HTTP endpoint |

## Commandes Telegram

| Commande | Handler | Description |
|----------|---------|-------------|
| `/run` | RunCommandHandler | Déclenche un cycle de publication manuellement |
| `/status` | StatusCommandHandler | Affiche l'état du service (scheduling, dernière exécution, prochaine) |
| `/start` | StartCommandHandler | Active le scheduling automatique |
| `/stop` | StopCommandHandler | Suspend le scheduling automatique |
| `/history` | HistoryCommandHandler | Affiche les 7 dernières publications |
| `/schedule HH:mm` | ScheduleCommandHandler | Configure l'heure d'exécution quotidienne |
| `/tipsters` | TipstersCommandHandler | Affiche, ajoute ou retire des tipsters |
| `/report [jours]` | ReportCommandHandler | Tableau de bord performances (taux réussite, ROI, sport, top tipsters) |

## Workers (BackgroundServices)

| Worker | Description |
|--------|-------------|
| SchedulerWorker | Planification quotidienne, calcul prochain run, exécution du cycle via PostingCycleService |
| TelegramBotService | Long polling Telegram, dispatch commandes via ICommandHandler, AuthorizationFilter |

## Modèles de données

| Modèle | Description | Persisté |
|--------|-------------|----------|
| HistoryEntry | Publication enrichie (betId, result, odds, sport, tipsterName, publishedAt) | Oui (history.json) |
| TipsterConfig | Configuration tipster (url, name, Id extrait) | Oui (tipsters.json) |
| PendingBet | Paris à venir enrichis (ROI, winRate, sport, tipsterUsername) | Non |
| ScrapedTipster | DTO tipster scraped (username, roi, betsNumber, mostBetSport) | Non |
| BetOrderRequest | Corps POST API publication (sportId, matchupId, marketKey, designation) | Non |
| CircuitBreakerState | Enum état circuit breaker (Closed, Open, HalfOpen) | Non |

## Configuration (Options pattern)

| Classe | Section | Champs clés |
|--------|---------|-------------|
| Bet2InvestOptions | Bet2Invest | ApiBase, Identifier, Password, RequestDelayMs |
| TelegramOptions | Telegram | BotToken, AuthorizedChatId |
| PosterOptions | Poster | ScheduleTime, RetryDelayMs, MaxRetryCount, DataPath, LogPath, MinOdds, MaxOdds, EventHorizonHours, SelectionMode, HealthCheckPort, LogRetentionDays |

## Formatage (MessageFormatter)

| Méthode | Usage |
|---------|-------|
| FormatStatus | `/status` — état scheduling, dernière/prochaine exécution |
| FormatHistory | `/history` — liste publications récentes |
| FormatTipsters | `/tipsters` — liste tipsters configurés |
| FormatOnboarding | Premier lancement — guide de démarrage |
| FormatReport | `/report` — taux réussite, ROI, répartition sport, top tipsters |

## Exceptions

| Exception | Usage |
|-----------|-------|
| Bet2InvestApiException | Erreur API (endpoint, status HTTP, payload, détection changement contrat) |
| PublishException | Erreur publication d'un pari (betId, status HTTP) |
