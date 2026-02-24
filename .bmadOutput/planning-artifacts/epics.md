---
stepsCompleted:
  - step-01-prerequisites
  - step-02-design-epics
  - step-03-create-stories
  - step-04-final-validation
  - epic-6-step-01-prerequisites
  - epic-6-step-02-design-epics
  - epic-6-step-03-create-stories
  - epic-6-step-04-final-validation
inputDocuments:
  - .bmadOutput/planning-artifacts/prd.md
  - .bmadOutput/planning-artifacts/architecture.md
---

# bet2invest-poster - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for bet2invest-poster, decomposing the requirements from the PRD and Architecture into implementable stories.

## Requirements Inventory

### Functional Requirements

FR1: L'utilisateur peut configurer ses credentials bet2invest via appsettings.json ou variables d'environnement
FR2: Le système s'authentifie automatiquement sur l'API bet2invest
FR3: Le système renouvelle le token d'authentification si expiré avant une exécution
FR4: Le système lit la liste des tipsters depuis tipsters.json
FR5: Le système récupère les paris à venir (non résolus) de chaque tipster listé
FR6: Le système filtre uniquement les tipsters gratuits (free)
FR7: Le système sélectionne aléatoirement 5, 10 ou 15 pronostics
FR8: Le système vérifie qu'un pronostic n'a pas déjà été publié (doublons via history.json)
FR9: Le système publie les pronostics sélectionnés sur le compte utilisateur via l'API bet2invest
FR10: Le système enregistre les pronostics publiés dans history.json
FR11: Le système exécute le cycle complet automatiquement à l'heure configurée chaque jour
FR12: Le système retente l'exécution en cas d'échec (jusqu'à 3 tentatives)
FR13: L'utilisateur peut configurer l'heure d'exécution quotidienne
FR14: L'utilisateur peut déclencher une exécution manuelle via /run
FR15: L'utilisateur peut consulter l'état du système via /status : dernière exécution (date/heure + résultat), nombre de pronostics publiés, prochain run planifié, état de connexion API
FR16: Le système envoie une notification Telegram en cas de publication réussie
FR17: Le système envoie une notification Telegram en cas d'échec avec le détail de l'erreur
FR18: Le système notifie si toutes les tentatives de retry échouent
FR19: Le système restreint l'accès au bot Telegram au chat ID autorisé
FR20: Le système ignore silencieusement les commandes de chat IDs non autorisés
FR21: L'utilisateur peut configurer tous les paramètres via appsettings.json
FR22: L'utilisateur peut surcharger la configuration via variables d'environnement
FR23: Le système tourne en continu comme service background sur un VPS

### NonFunctional Requirements

NFR1: Le service redémarre automatiquement en cas de crash — délai de redémarrage < 30 secondes
NFR2: Taux de succès du cycle quotidien > 95% (hors indisponibilité API bet2invest)
NFR3: Notification Telegram envoyée dans les 5 minutes suivant un échec
NFR4: history.json ne doit jamais être corrompu suite à un crash en cours d'écriture — écriture atomique (write-to-temp + rename)
NFR5: Les credentials et tokens ne doivent jamais apparaître dans les logs ou messages d'erreur
NFR6: Les credentials sont stockés exclusivement dans des variables d'environnement en production
NFR7: Le bot rejette 100% des commandes de chat IDs non autorisés
NFR8: Délai minimum de 500ms entre chaque requête à l'API bet2invest
NFR9: En cas de changement d'API bet2invest, le système retourne un code d'erreur identifiable, logue le changement détecté (endpoint, code HTTP, payload), et envoie une notification Telegram
NFR10: Support des interruptions temporaires de l'API Telegram (retry avec backoff)
NFR11: Code API bet2invest isolé dans un module dédié pour faciliter l'adaptation
NFR12: Chaque entrée de log inclut au minimum : timestamp, étape du cycle (auth/scrape/select/publish), tipster concerné le cas échéant, et code d'erreur

### Additional Requirements

**Starter Template (Architecture) :**
- Initialisation via `dotnet new worker -n Bet2InvestPoster --framework net9.0`
- Solution avec 2 projets : `src/Bet2InvestPoster` (principal) + `tests/Bet2InvestPoster.Tests` (xUnit)

**Intégration Submodule (Architecture) :**
- ExtendedBet2InvestClient (wrapper) dans Services/ — ne modifie pas le submodule
- Hérite ou compose Bet2InvestClient pour ajouter GetUpcomingBetsAsync() et PublishBetAsync()
- Référence projet directe vers le submodule scraper

**NuGet Packages (Architecture) :**
- Telegram.Bot 22.9.0, Microsoft.Extensions.Hosting.Systemd 9.0.x, Polly.Core 8.6.5
- Serilog 4.3.1, Serilog.Extensions.Hosting, Serilog.Sinks.Console, Serilog.Sinks.File
- xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk

**Résilience (Architecture) :**
- Polly.Core 8.6.5 — ResiliencePipeline pour retry du cycle complet (3 tentatives, 60s)
- Pipeline wraps le cycle complet (scrape → select → publish), pas chaque appel individuel

**Logging (Architecture) :**
- Serilog 4.3.1 — sinks console + fichier
- Template structuré : "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] [{Step}] {Message} {Properties}"
- Steps autorisés : Auth, Scrape, Select, Publish, Notify, Purge

**Data (Architecture) :**
- JSON files uniquement (tipsters.json lecture seule, history.json read/write atomique)
- Purge automatique des entrées > 30 jours à chaque exécution
- System.Text.Json (pas Newtonsoft)

**DI Pattern (Architecture) :**
- Services en Scoped (un scope par cycle d'exécution)
- Bet2InvestClient en Singleton
- Interface pour chaque service

**CI/CD (Architecture) :**
- GitHub Actions : build + test sur push/PR main
- Déploiement manuel : dotnet publish + SCP/SSH
- systemd unit file pour VPS

**Déploiement (Architecture) :**
- VPS avec systemd (Microsoft.Extensions.Hosting.Systemd)
- Fichier deploy/bet2invest-poster.service
- /opt/bet2invest-poster/ comme répertoire d'installation

### FR Coverage Map

| FR | Epic | Description |
|---|---|---|
| FR1 | Epic 1 | Configuration credentials via appsettings.json / env vars |
| FR2 | Epic 2 | Authentification automatique API bet2invest |
| FR3 | Epic 2 | Renouvellement token si expiré |
| FR4 | Epic 2 | Lecture liste tipsters depuis tipsters.json |
| FR5 | Epic 2 | Récupération paris à venir (non résolus) |
| FR6 | Epic 2 | Filtrage tipsters gratuits (free) |
| FR7 | Epic 3 | Sélection aléatoire 5, 10 ou 15 pronostics |
| FR8 | Epic 3 | Détection doublons via history.json |
| FR9 | Epic 3 | Publication via API bet2invest |
| FR10 | Epic 3 | Enregistrement dans history.json |
| FR11 | Epic 5 | Exécution automatique quotidienne |
| FR12 | Epic 5 | Retry en cas d'échec (3 tentatives) |
| FR13 | Epic 5 | Configuration heure d'exécution |
| FR14 | Epic 4 | Commande /run (exécution manuelle) |
| FR15 | Epic 4 | Commande /status (état complet) |
| FR16 | Epic 4 | Notification succès |
| FR17 | Epic 4 | Notification échec avec détail |
| FR18 | Epic 4 | Notification si toutes tentatives échouent |
| FR19 | Epic 4 | Restriction accès par chat ID |
| FR20 | Epic 4 | Ignorer commandes non autorisées |
| FR21 | Epic 1 | Configuration via appsettings.json |
| FR22 | Epic 1 | Surcharge via env vars |
| FR23 | Epic 1 | Service background continu sur VPS |

## Epic List

### Epic 1 : Fondation du Projet et Configuration
L'utilisateur peut configurer et démarrer le service sur son VPS.
**FRs couvertes :** FR1, FR21, FR22, FR23
**NFRs adressées :** NFR1, NFR5, NFR6, NFR12

### Epic 2 : Connexion API et Extraction des Pronostics
Le système se connecte à bet2invest et récupère les paris à venir des meilleurs tipsters free.
**FRs couvertes :** FR2, FR3, FR4, FR5, FR6
**NFRs adressées :** NFR8, NFR9, NFR11

### Epic 3 : Sélection, Publication et Historique
Les pronostics sont sélectionnés aléatoirement, publiés sur le compte bet2invest, et enregistrés sans doublons.
**FRs couvertes :** FR7, FR8, FR9, FR10
**NFRs adressées :** NFR4

### Epic 4 : Interface Telegram — Commandes et Notifications
L'utilisateur contrôle et monitore le système via Telegram : /run, /status, notifications succès/échec.
**FRs couvertes :** FR14, FR15, FR16, FR17, FR18, FR19, FR20
**NFRs adressées :** NFR3, NFR7, NFR10

### Epic 5 : Automatisation Quotidienne et Résilience
Le système s'exécute automatiquement chaque jour avec retry — zéro intervention après configuration.
**FRs couvertes :** FR11, FR12, FR13
**NFRs adressées :** NFR2

### Epic 6 : Validation Manuelle en Production
L'utilisateur confirme que le service fonctionne correctement de bout en bout sur le VPS réel — de la publication d'un premier pronostic jusqu'au scheduling quotidien automatique.
**FRs validées :** FR1–FR23 (toutes — validation en conditions réelles)
**NFRs vérifiées :** NFR1, NFR2, NFR3, NFR4, NFR5, NFR6, NFR7, NFR8, NFR12

## Epic 1 : Fondation du Projet et Configuration

L'utilisateur peut configurer et démarrer le service sur son VPS.

### Story 1.1 : Initialisation du Projet Worker Service

As a développeur,
I want un projet .NET 9 Worker Service initialisé avec la structure de solution complète,
So that je dispose de la fondation technique pour développer le service.

**Acceptance Criteria:**

**Given** un repository git avec le submodule jtdev-bet2invest-scraper
**When** le projet est initialisé via `dotnet new worker`
**Then** la solution `Bet2InvestPoster.sln` est créée avec `src/Bet2InvestPoster/` et `tests/Bet2InvestPoster.Tests/`
**And** le `.csproj` principal référence le projet submodule scraper
**And** tous les NuGet packages sont installés (Telegram.Bot 22.9.0, Polly.Core 8.6.5, Serilog 4.3.1, Serilog.Extensions.Hosting, Serilog.Sinks.Console, Serilog.Sinks.File, Microsoft.Extensions.Hosting.Systemd)
**And** `dotnet build` réussit sans erreur
**And** le `.gitignore` exclut bin/, obj/, et les fichiers de config sensibles

### Story 1.2 : Configuration et Injection de Dépendances

As a l'utilisateur,
I want configurer mes credentials et paramètres via appsettings.json et variables d'environnement,
So that je puisse personnaliser le service sans modifier le code.

**Acceptance Criteria:**

**Given** un fichier `appsettings.json` avec les sections Bet2Invest, Telegram, et Poster
**When** le service démarre
**Then** les options sont chargées via `IOptions<Bet2InvestOptions>`, `IOptions<TelegramOptions>`, `IOptions<PosterOptions>`
**And** les variables d'environnement surchargent les valeurs de appsettings.json (FR22)
**And** Serilog est configuré avec sinks console + fichier
**And** chaque log inclut timestamp, étape du cycle (Step), et contexte (NFR12)
**And** les credentials ne sont jamais loguées dans aucun sink (NFR5)

### Story 1.3 : Déploiement VPS et Service systemd

As a l'utilisateur,
I want déployer le service sur mon VPS comme un service systemd,
So that le service tourne en continu et redémarre automatiquement en cas de crash.

**Acceptance Criteria:**

**Given** le fichier `deploy/bet2invest-poster.service` configuré
**When** le service est déployé sur le VPS dans `/opt/bet2invest-poster/`
**Then** systemd démarre le service et le redémarre en cas de crash (délai < 30s, NFR1)
**And** le service tourne en continu comme background service (FR23)
**And** les credentials sont chargées depuis les variables d'environnement systemd (NFR6)
**And** les logs Serilog sont écrits dans `/opt/bet2invest-poster/logs/`

## Epic 2 : Connexion API et Extraction des Pronostics

Le système se connecte à bet2invest et récupère les paris à venir des meilleurs tipsters free.

### Story 2.1 : ExtendedBet2InvestClient — Authentification et Wrapper

As a le système,
I want m'authentifier automatiquement sur l'API bet2invest et disposer d'un client étendu,
So that je puisse accéder aux endpoints de paris à venir et de publication non couverts par le scraper.

**Acceptance Criteria:**

**Given** le `Bet2InvestClient` du submodule scraper référencé dans le projet
**When** le service démarre un cycle d'exécution
**Then** `ExtendedBet2InvestClient` compose `Bet2InvestClient` et expose `LoginAsync()`, `GetUpcomingBetsAsync()`, `PublishBetAsync()`
**And** l'authentification est automatique via les credentials configurés (FR2)
**And** le token est renouvelé automatiquement si expiré avant ou pendant une exécution (FR3)
**And** le code API est isolé dans `Services/ExtendedBet2InvestClient.cs` avec interface `IExtendedBet2InvestClient` (NFR11)
**And** un délai de 500ms est respecté entre chaque requête API (NFR8)

### Story 2.2 : Lecture des Tipsters (TipsterService)

As a l'utilisateur,
I want que le système lise ma liste de tipsters depuis tipsters.json,
So that seuls les tipsters que j'ai choisis soient utilisés pour l'extraction.

**Acceptance Criteria:**

**Given** un fichier `tipsters.json` contenant un tableau de `{ "url": "...", "name": "..." }`
**When** le cycle d'exécution démarre
**Then** `TipsterService` relit le fichier à chaque exécution (éditable à chaud, pas de redémarrage)
**And** seuls les tipsters gratuits (free) sont retenus (FR6)
**And** si le fichier est absent ou invalide, une erreur explicite est loguée avec le Step `Scrape`

### Story 2.3 : Récupération des Paris à Venir (UpcomingBetsFetcher)

As a le système,
I want récupérer les paris à venir (non résolus) de chaque tipster listé,
So that je dispose d'un pool de pronostics candidats à la publication.

**Acceptance Criteria:**

**Given** une liste de tipsters validée par `TipsterService`
**When** `UpcomingBetsFetcher` interroge l'API pour chaque tipster
**Then** les paris à venir (non résolus) sont récupérés pour chaque tipster (FR5)
**And** un délai de 500ms est respecté entre chaque requête (NFR8)
**And** en cas de changement d'API (endpoint, code HTTP inattendu), le système logue le changement détecté (endpoint, code HTTP, payload) et retourne un `Bet2InvestApiException` identifiable (NFR9)
**And** les résultats sont agrégés en une liste unique de paris candidats

## Epic 3 : Sélection, Publication et Historique

Les pronostics sont sélectionnés aléatoirement, publiés sur le compte bet2invest, et enregistrés sans doublons.

### Story 3.1 : HistoryManager — Stockage, Doublons et Purge

As a l'utilisateur,
I want que le système ne publie jamais un pronostic déjà posté,
So that mon compte bet2invest ne contienne pas de doublons.

**Acceptance Criteria:**

**Given** un fichier `history.json` contenant les pronostics précédemment publiés
**When** le système vérifie un pronostic candidat
**Then** `HistoryManager` détecte si le `betId` existe déjà dans l'historique (FR8)
**And** l'écriture dans `history.json` est atomique (write-to-temp + rename) pour éviter toute corruption (NFR4)
**And** les entrées de plus de 30 jours sont purgées automatiquement à chaque exécution
**And** si `history.json` n'existe pas, il est créé automatiquement comme tableau vide
**And** chaque opération est loguée avec le Step `Purge` (purge) ou `Publish` (enregistrement)

### Story 3.2 : BetSelector — Sélection Aléatoire

As a l'utilisateur,
I want que le système sélectionne aléatoirement 5, 10 ou 15 pronostics parmi les candidats,
So that mes publications soient variées et en quantité adaptée.

**Acceptance Criteria:**

**Given** une liste de paris candidats (issus de `UpcomingBetsFetcher`) et un historique de doublons
**When** `BetSelector` effectue la sélection
**Then** les pronostics déjà dans `history.json` sont exclus
**And** le nombre sélectionné est aléatoirement 5, 10 ou 15 (FR7)
**And** si le nombre de candidats disponibles est inférieur au nombre cible, tous les candidats disponibles sont sélectionnés
**And** la sélection parmi les candidats restants est aléatoire
**And** l'opération est loguée avec le Step `Select` (nombre candidats, nombre sélectionnés)

### Story 3.3 : BetPublisher et PostingCycleService — Publication et Orchestration

As a l'utilisateur,
I want que les pronostics sélectionnés soient publiés sur mon compte bet2invest,
So que ma présence sur la plateforme soit maintenue automatiquement.

**Acceptance Criteria:**

**Given** une liste de pronostics sélectionnés par `BetSelector`
**When** `BetPublisher` publie chaque pronostic via `ExtendedBet2InvestClient.PublishBetAsync()`
**Then** chaque pronostic est publié sur le compte utilisateur via l'API bet2invest (FR9)
**And** un délai de 500ms est respecté entre chaque publication (NFR8)
**And** chaque pronostic publié est enregistré dans `history.json` via `HistoryManager` (FR10)
**And** `PostingCycleService` orchestre le cycle complet : fetch → select → publish → record
**And** chaque étape est loguée avec le Step correspondant (`Scrape`, `Select`, `Publish`)
**And** en cas d'erreur de publication, une `PublishException` est levée avec le détail (betId, code HTTP, message)

## Epic 4 : Interface Telegram — Commandes et Notifications

L'utilisateur contrôle et monitore le système via Telegram : /run, /status, notifications succès/échec.

### Story 4.1 : Bot Telegram — Setup, Polling et Sécurité

As a l'utilisateur,
I want que seul mon chat Telegram puisse interagir avec le bot,
So que personne d'autre ne puisse contrôler ou lire les informations du service.

**Acceptance Criteria:**

**Given** un bot Telegram configuré avec `BotToken` et `AuthorizedChatId`
**When** `TelegramBotService` démarre en long polling
**Then** le bot est connecté et écoute les messages entrants
**And** `AuthorizationFilter` vérifie le chat ID de chaque message reçu
**And** 100% des commandes provenant de chat IDs non autorisés sont rejetées silencieusement (FR19, FR20, NFR7)
**And** en cas d'interruption temporaire de l'API Telegram, le bot retry avec backoff exponentiel (NFR10)
**And** le démarrage et les erreurs de connexion sont loguées avec le Step `Notify`

### Story 4.2 : Commandes /run et /status

As a l'utilisateur,
I want envoyer /run pour déclencher une publication manuelle et /status pour voir l'état du système,
So that je puisse contrôler et surveiller le service à tout moment.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/run`
**Then** `RunCommandHandler` déclenche `PostingCycleService` immédiatement (FR14)
**And** le résultat (succès ou échec) est envoyé en réponse dans le chat

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/status`
**Then** `StatusCommandHandler` répond avec : dernière exécution (date/heure + résultat), nombre de pronostics publiés, prochain run planifié, état de connexion API (FR15)
**And** le message est formaté via `MessageFormatter` en bloc lisible

### Story 4.3 : NotificationService — Notifications Automatiques

As a l'utilisateur,
I want recevoir une notification Telegram après chaque exécution automatique,
So que je sois informé du résultat sans devoir vérifier manuellement.

**Acceptance Criteria:**

**Given** un cycle de publication terminé (succès ou échec)
**When** `NotificationService` envoie la notification
**Then** en cas de succès : message `"✅ {count} pronostics publiés avec succès."` (FR16)
**And** en cas d'échec : message `"❌ Échec — {raison}. {détails retry}."` avec le détail de l'erreur (FR17)
**And** si toutes les tentatives de retry échouent : notification explicite avec le nombre de tentatives et l'erreur finale (FR18)
**And** la notification est envoyée dans les 5 minutes suivant l'événement (NFR3)
**And** chaque envoi est logué avec le Step `Notify`

## Epic 5 : Automatisation Quotidienne et Résilience

Le système s'exécute automatiquement chaque jour avec retry — zéro intervention après configuration.

### Story 5.1 : SchedulerWorker — Exécution Quotidienne Planifiée

As a l'utilisateur,
I want que le système publie automatiquement des pronostics chaque jour à l'heure que j'ai configurée,
So que ma présence sur bet2invest soit maintenue sans aucune intervention de ma part.

**Acceptance Criteria:**

**Given** une heure d'exécution configurée dans `PosterOptions.ScheduleTime` (ex: `"08:00"`)
**When** l'heure configurée est atteinte
**Then** `SchedulerWorker` déclenche automatiquement `PostingCycleService` (FR11)
**And** l'heure d'exécution est configurable via `appsettings.json` ou variable d'environnement (FR13)
**And** après une exécution, le prochain run est calculé pour le lendemain à la même heure
**And** le scheduling est interne au service (pas de dépendance à cron externe)
**And** le démarrage et chaque déclenchement sont logués avec timestamp

### Story 5.2 : Résilience Polly — Retry du Cycle Complet

As a l'utilisateur,
I want que le système retente automatiquement en cas d'échec,
So que les erreurs temporaires (réseau, API) ne bloquent pas la publication quotidienne.

**Acceptance Criteria:**

**Given** un cycle de publication déclenché (automatique ou `/run`)
**When** le cycle échoue (erreur réseau, API down, timeout)
**Then** Polly.Core `ResiliencePipeline` retente le cycle complet jusqu'à 3 fois (FR12)
**And** le délai entre tentatives est de 60s (configurable via `PosterOptions.RetryDelayMs`)
**And** le pipeline wraps le cycle complet (scrape → select → publish), pas chaque appel individuel
**And** chaque tentative est loguée (numéro de tentative, erreur rencontrée)
**And** si les 3 tentatives échouent, `NotificationService` envoie l'alerte finale (FR18)
**And** le taux de succès quotidien cible est > 95% hors indisponibilité API bet2invest (NFR2)

## Epic 6 : Validation Manuelle en Production

L'utilisateur confirme que le service fonctionne correctement de bout en bout sur le VPS réel — de la publication d'un premier pronostic jusqu'au scheduling quotidien automatique.

### Story 6.1 : Déploiement VPS et Configuration Systemd

As a l'utilisateur,
I want déployer le service bet2invest-poster sur mon VPS avec un service systemd opérationnel,
So that le service tourne en continu et redémarre automatiquement en cas de crash.

**Acceptance Criteria :**

**Given** le code compilé avec `dotnet publish -c Release`
**When** les binaires sont copiés dans `/opt/bet2invest-poster/` sur le VPS
**Then** le service démarre via `systemctl start bet2invest-poster` sans erreur

**Given** le fichier `bet2invest-poster.service` installé dans `/etc/systemd/system/`
**When** `systemctl enable bet2invest-poster` est exécuté
**Then** le service redémarre automatiquement au boot et après un crash (NFR1)

**Given** les variables d'environnement configurées dans le service systemd (`Bet2Invest__Identifier`, `Bet2Invest__Password`, `Telegram__BotToken`, `Telegram__AuthorizedChatId`, `Poster__BankrollId`)
**When** le service démarre
**Then** les credentials sont chargés depuis les env vars — jamais depuis `appsettings.json` en production (NFR5, NFR6)
**And** `systemctl status bet2invest-poster` affiche `active (running)`

**Given** le service en cours d'exécution
**When** on consulte les logs via `journalctl -u bet2invest-poster -f`
**Then** les logs Serilog apparaissent avec timestamp, Step et contexte structuré (NFR12)
**And** aucun credential n'apparaît dans les logs (NFR5)

**Given** le service actif
**When** on tue le processus manuellement (`kill -9 <pid>`)
**Then** systemd redémarre le service en moins de 30 secondes (NFR1)

### Story 6.2 : Validation du Cycle de Publication Manuel

As a l'utilisateur,
I want exécuter `/run` depuis Telegram et vérifier qu'un pronostic est réellement publié sur bet2invest,
So that je confirme que le pipeline complet fonctionne en conditions réelles.

**Acceptance Criteria :**

**Given** le service déployé et actif sur le VPS (story 6.1 complétée)
**When** j'envoie `/run` depuis mon chat Telegram autorisé
**Then** le bot répond `✅ Cycle exécuté avec succès` (FR14)
**And** le nombre de pronostics publiés est mentionné dans la réponse

**Given** le cycle `/run` exécuté avec succès
**When** je consulte mon compte bet2invest
**Then** les pronostics sélectionnés sont visibles sur le compte (FR9)
**And** `history.json` sur le VPS contient les IDs des pronostics publiés (FR10)

**Given** le service actif
**When** j'envoie `/status` depuis Telegram (FR15)
**Then** la réponse affiche : dernière exécution (date/heure + résultat), nombre de pronostics publiés, prochain run planifié, état de connexion API

**Given** j'envoie `/run` une seconde fois le même jour
**When** le cycle s'exécute
**Then** aucun pronostic déjà publié n'est republié (FR8 — déduplication active via history.json)

**Given** un chat Telegram non autorisé envoie `/run`
**When** le bot reçoit la commande
**Then** la commande est ignorée silencieusement — aucune réponse, aucune exécution (FR19, FR20, NFR7)

**Given** le cycle en cours d'exécution
**When** on consulte les logs via `journalctl`
**Then** les logs structurés montrent les Steps Auth → Scrape → Select → Publish (NFR12)
**And** le délai d'au moins 500ms entre requêtes API est respecté (NFR8)

### Story 6.3 : Validation du Scheduling Quotidien et de la Résilience

As a l'utilisateur,
I want observer le déclenchement automatique du cycle à l'heure configurée et vérifier le comportement en cas d'erreur,
So that je confirme que le service fonctionne sans aucune intervention de ma part au quotidien.

**Acceptance Criteria :**

**Given** `Poster__ScheduleTime` configuré à une heure proche (ex : dans 5 minutes)
**When** l'heure configurée est atteinte
**Then** le cycle se déclenche automatiquement sans commande `/run` (FR11)
**And** une notification Telegram de succès est reçue dans les 5 minutes (FR16, NFR3)
**And** `/status` affiche le prochain run planifié pour le lendemain à la même heure (FR13, FR15)

**Given** le service redémarré après un arrêt volontaire (`systemctl restart`)
**When** il redémarre
**Then** le scheduling reprend correctement — le prochain run est recalculé depuis l'heure courante
**And** aucune exécution en double n'est déclenchée

**Given** le service actif pendant 24h+ après le déploiement
**When** le cycle quotidien automatique s'exécute
**Then** les pronostics sont publiés et `history.json` est mis à jour (NFR4 — pas de corruption)
**And** la notification de succès arrive sur Telegram (FR16)

**Given** une erreur simulée lors d'un cycle (ex : coupure réseau temporaire)
**When** le cycle échoue sur la 1ère tentative
**Then** Polly retente automatiquement jusqu'à 3 fois (FR12)
**And** chaque tentative est loguée avec le numéro de tentative
**And** si toutes les tentatives échouent, une notification d'échec définitif est reçue sur Telegram (FR18)
**And** le scheduling reprend normalement le lendemain

**Given** le service en production depuis plusieurs jours
**When** on consulte `/status`
**Then** l'historique des dernières exécutions reflète les cycles quotidiens réels
