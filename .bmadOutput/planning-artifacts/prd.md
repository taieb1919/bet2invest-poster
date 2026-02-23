---
stepsCompleted:
  - step-01-init
  - step-02-discovery
  - step-02b-vision
  - step-02c-executive-summary
  - step-03-success
  - step-04-journeys
  - step-05-domain
  - step-06-innovation
  - step-07-project-type
  - step-08-scoping
  - step-09-functional
  - step-10-nonfunctional
  - step-11-polish
  - step-12-complete
inputDocuments:
  - jtdev-bet2invest-scraper/README.md
documentCounts:
  briefs: 0
  research: 0
  brainstorming: 0
  projectDocs: 1
workflowType: 'prd'
classification:
  projectType: cli_tool
  domain: fintech
  complexity: medium
  projectContext: brownfield
---

# Product Requirements Document - bet2invest-poster

**Author:** taieb
**Date:** 2026-02-23

## Executive Summary

bet2invest-poster est un service d'automatisation .NET 9 avec interface Telegram bot qui élimine le travail manuel quotidien de curation et publication de pronostics sportifs sur bet2invest.com. L'outil se connecte à l'API bet2invest, récupère les paris à venir des meilleurs tipsters gratuits (free) triés par ROI descendant, en sélectionne aléatoirement 5, 10 ou 15, et les publie sur le compte de l'utilisateur. Il s'exécute une fois par jour sans intervention humaine, déployé sur un VPS.

L'utilisateur cible est un parieur actif sur bet2invest qui souhaite maintenir une présence régulière sur la plateforme en publiant des pronostics de qualité sans effort quotidien.

### Ce qui rend ce produit unique

La valeur réside dans la fiabilité d'exécution, pas dans l'intelligence de sélection. Le critère est simple — tipsters free avec ROI élevé — mais l'automatisation complète du cycle scrape → filtre → sélection aléatoire → publication transforme une corvée quotidienne en un processus "set and forget". L'outil étend le scraper existant (submodule jtdev-bet2invest-scraper) avec un flux inversé : au lieu de lire et exporter, il lit et publie.

### Classification

- **Type :** Service d'automatisation avec bot Telegram
- **Domaine :** Paris sportifs / pronostics
- **Complexité :** Moyenne — API tierce connue, logique de sélection simple, scheduling quotidien
- **Contexte :** Brownfield — extension du scraper .NET 9 existant

## Success Criteria

### User Success

- Exécution quotidienne sans intervention — zéro action requise après configuration initiale
- Pronostics publiés correctement sur le compte bet2invest chaque jour
- Notification Telegram en cas d'échec (API down, erreur réseau, authentification expirée)
- Zéro doublon — jamais de pronostic déjà posté republié

### Business Success

- Usage personnel — un seul utilisateur
- Élimination complète de la tâche manuelle de curation et publication
- Présence régulière maintenue sur bet2invest sans effort

### Technical Success

- Connexion fiable à l'API bet2invest avec gestion d'erreurs et retry
- Récupération des paris à venir (non résolus) des tipsters free — fonctionnalité non couverte par le scraper actuel
- Sélection aléatoire de 5, 10 ou 15 pronostics parmi les tipsters à fort ROI
- Publication via l'API bet2invest sans erreur
- Détection de doublons par rapport aux publications précédentes

### Measurable Outcomes

- Taux de succès de publication quotidienne > 95% (hors maintenance API bet2invest)
- Zéro doublon publié
- Notification envoyée dans les 5 minutes suivant un échec
- Liste des tipsters mise à jour une fois par trimestre

## User Journeys

### Parcours 1 : Configuration Initiale

Taieb installe bet2invest-poster sur son VPS. Il configure ses credentials bet2invest et le token Telegram dans les variables d'environnement, prépare manuellement le fichier `tipsters.json` avec les liens des meilleurs tipsters free, et configure l'heure d'exécution dans `appsettings.json`. Il lance le service, envoie `/status` au bot Telegram pour vérifier que tout est connecté. Le bot confirme : "Connecté. Prochain run : 08h00." L'outil est prêt.

**Capabilities :** configuration via env vars + appsettings.json, commande `/status`, validation connexion API.

### Parcours 2 : Journée Normale (Happy Path)

8h00. Le bot s'exécute automatiquement : connexion API bet2invest, récupération des paris à venir des tipsters dans `tipsters.json`, vérification des doublons, sélection aléatoire de 8 pronostics (entre 5 et 15), publication sur le compte de Taieb. Message Telegram : "8 pronostics publiés avec succès."

**Capabilities :** exécution planifiée, scraping paris à venir, détection doublons, sélection aléatoire, publication API, notification succès.

### Parcours 3 : Échec et Dépannage

8h05. L'API bet2invest est down. Après 3 tentatives (retry), message Telegram : "Échec — API bet2invest indisponible. Timeout après 3 tentatives." Taieb envoie `/run` pour relancer manuellement. L'API répond, 12 pronostics publiés. Confirmation envoyée.

**Capabilities :** retry automatique, notification d'échec détaillée, relance manuelle `/run`.

### Parcours 4 : Maintenance Trimestrielle des Tipsters

Taieb édite manuellement `tipsters.json` sur le VPS pour mettre à jour les liens des tipsters avec les meilleurs ROI actuels. Aucun redémarrage nécessaire — le fichier est relu à chaque exécution.

**Capabilities :** fichier tipsters éditable à chaud, pas de redémarrage requis.

### Parcours 5 : Monitoring

Taieb envoie `/status` pour vérifier l'état : dernière exécution, nombre de pronostics publiés, prochain run, connexion API.

**Capabilities :** commande `/status` avec état complet.

### Journey Requirements Summary

| Capability | Parcours |
|---|---|
| Bot Telegram (commandes + notifications) | Tous |
| Scheduling quotidien configurable | P1, P2 |
| Scraping des paris à venir (non résolus) | P2 |
| Détection de doublons | P2 |
| Sélection aléatoire 5/10/15 | P2 |
| Publication via API bet2invest | P2, P3 |
| Notification succès/échec | P2, P3 |
| Retry automatique + relance manuelle `/run` | P3 |
| Fichier `tipsters.json` éditable | P4 |
| Commande `/status` | P5 |

## Domain-Specific Requirements

### Dépendance API Tierce

- L'API bet2invest peut changer sans préavis — le code isole les appels API dans un module dédié
- Délai de 500ms entre chaque requête API (conservé du scraper)
- Une exécution par jour = risque de ban minimal

### Déploiement VPS

- Service systemd en continu sur VPS
- Scheduling interne (pas de dépendance à cron externe)
- Redémarrage automatique en cas de crash (systemd restart policy)
- Logs persistants pour diagnostic

### Risk Mitigations

| Risque | Impact | Mitigation |
|---|---|---|
| API bet2invest change | Publication échoue | Notification + logs détaillés + code API isolé |
| Token Telegram compromis | Accès non autorisé | Restriction par chat ID + régénération token |
| VPS down | Pas de publication | Systemd auto-restart |
| Credentials expirés | Connexion échoue | Notification explicite + relance manuelle |

## Technical Architecture

### Stack Technique

- .NET 9 / C# — cohérent avec le scraper existant
- Telegram.Bot (library .NET) pour l'interface Telegram
- `BackgroundService` / `IHostedService` pour le scheduling interne
- Réutilisation du `Bet2InvestClient` et des modèles du scraper (submodule)

### Commandes Telegram (MVP)

| Commande | Description |
|---|---|
| `/run` | Exécution manuelle immédiate |
| `/status` | État actuel (dernière exécution, prochain run, connexion API) |

### Commandes Telegram (Post-MVP)

| Commande | Description |
|---|---|
| `/start` | Activer le scheduling automatique |
| `/stop` | Suspendre le scheduling |
| `/history` | Historique des 7 dernières publications |
| `/tipsters` | Afficher la liste des tipsters actuels |
| `/tipsters update` | Scraper et proposer une nouvelle liste |
| `/tipsters add <lien>` | Ajouter un tipster |
| `/tipsters remove <lien>` | Retirer un tipster |
| `/schedule <HH:mm>` | Configurer l'heure d'exécution |

### Configuration

Hiérarchie : Variables d'environnement > appsettings.json

```json
{
  "Bet2Invest": {
    "ApiBase": "https://api.bet2invest.com",
    "Email": "",
    "Password": "",
    "RequestDelayMs": 500
  },
  "Telegram": {
    "BotToken": "",
    "AuthorizedChatId": ""
  },
  "Poster": {
    "ScheduleTime": "08:00",
    "TipstersFile": "./tipsters.json",
    "HistoryFile": "./history.json",
    "MinPosts": 5,
    "MaxPosts": 15,
    "RetryCount": 3,
    "RetryDelayMs": 60000
  }
}
```

### Fichiers de Données

- `tipsters.json` — Liste des liens/IDs de tipsters, éditable manuellement, relu à chaque exécution
- `history.json` — Historique des pronostics publiés pour détection de doublons

### Implementation Considerations

- Réutilisation du `Bet2InvestClient` du scraper pour l'authentification et les appels API
- **Nouveau développement requis :** récupération des paris à venir (non résolus) — le scraper actuel ne gère que les `SettledBets`
- **Nouveau développement requis :** endpoint de publication de pronostics via l'API bet2invest

## Project Scoping & Phased Development

### MVP Strategy

**Approche :** Problem-solving MVP — livrer la publication automatique quotidienne avec le minimum de fonctionnalités. Le fichier `tipsters.json` manuel suffit car la mise à jour est trimestrielle.

**Ressources :** Un développeur .NET, un VPS, un bot Telegram (BotFather).

### Phase 1 — MVP

**Parcours supportés :** P2 (Happy Path), P3 (Échec), P5 (Status)

- Authentification API bet2invest (réutilisation `Bet2InvestClient`)
- Récupération des paris à venir des tipsters dans `tipsters.json`
- Sélection aléatoire de 5, 10 ou 15 pronostics
- Publication via API bet2invest
- Détection de doublons via `history.json`
- Bot Telegram : `/run`, `/status`, notifications succès/échec
- Scheduling quotidien interne (`BackgroundService`)
- Retry automatique (3 tentatives)
- Configuration via `appsettings.json` + variables d'environnement
- Restriction d'accès bot par chat ID

### Phase 2 — Post-MVP

- Commandes Telegram étendues : `/start`, `/stop`, `/history`, `/schedule`
- Gestion tipsters via Telegram : `/tipsters`, CRUD
- Onboarding guidé via Telegram
- Logs structurés avec rotation quotidienne

### Phase 3 — Expansion

- Mise à jour automatisée de la liste tipsters
- Sélection intelligente multi-critères (ROI + taux de réussite + sport)
- Reporting sur les performances des pronostics republiés

### Risk Mitigation Strategy

**Risque technique principal :** La récupération des paris à venir n'existe pas dans le scraper actuel. Mitigation : explorer l'API bet2invest en priorité pour confirmer la faisabilité.

**Contingence :** Si le temps manque, le MVP peut fonctionner en CLI pur sans bot Telegram, avec ajout du bot en phase 2.

## Functional Requirements

### Authentification & Connexion API

- FR1 : L'utilisateur peut configurer ses credentials bet2invest via `appsettings.json` ou variables d'environnement
- FR2 : Le système s'authentifie automatiquement sur l'API bet2invest
- FR3 : Le système renouvelle le token d'authentification si expiré avant une exécution

### Scraping des Pronostics

- FR4 : Le système lit la liste des tipsters depuis `tipsters.json`
- FR5 : Le système récupère les paris à venir (non résolus) de chaque tipster listé
- FR6 : Le système filtre uniquement les tipsters gratuits (free)

### Sélection & Publication

- FR7 : Le système sélectionne aléatoirement 5, 10 ou 15 pronostics
- FR8 : Le système vérifie qu'un pronostic n'a pas déjà été publié (doublons via `history.json`)
- FR9 : Le système publie les pronostics sélectionnés sur le compte utilisateur via l'API bet2invest
- FR10 : Le système enregistre les pronostics publiés dans `history.json`

### Scheduling & Exécution Automatique

- FR11 : Le système exécute le cycle complet (scrape → sélection → publication) automatiquement à l'heure configurée chaque jour
- FR12 : Le système retente l'exécution en cas d'échec (jusqu'à 3 tentatives)
- FR13 : L'utilisateur peut configurer l'heure d'exécution quotidienne

### Bot Telegram — Commandes

- FR14 : L'utilisateur peut déclencher une exécution manuelle via `/run`
- FR15 : L'utilisateur peut consulter l'état du système via `/status` : dernière exécution (date/heure + résultat), nombre de pronostics publiés, prochain run planifié, état de connexion API

### Bot Telegram — Notifications

- FR16 : Le système envoie une notification Telegram en cas de publication réussie
- FR17 : Le système envoie une notification Telegram en cas d'échec avec le détail de l'erreur
- FR18 : Le système notifie si toutes les tentatives de retry échouent

### Sécurité & Contrôle d'Accès

- FR19 : Le système restreint l'accès au bot Telegram au chat ID autorisé
- FR20 : Le système ignore silencieusement les commandes de chat IDs non autorisés

### Configuration & Déploiement

- FR21 : L'utilisateur peut configurer tous les paramètres via `appsettings.json`
- FR22 : L'utilisateur peut surcharger la configuration via variables d'environnement
- FR23 : Le système tourne en continu comme service background sur un VPS

## Non-Functional Requirements

### Fiabilité

- NFR1 : Le service redémarre automatiquement en cas de crash — délai de redémarrage < 30 secondes
- NFR2 : Taux de succès du cycle quotidien > 95% (hors indisponibilité API bet2invest)
- NFR3 : Notification Telegram envoyée dans les 5 minutes suivant un échec
- NFR4 : `history.json` ne doit jamais être corrompu suite à un crash en cours d'écriture — écriture atomique (write-to-temp + rename) vérifiable par test d'interruption

### Sécurité

- NFR5 : Les credentials et tokens ne doivent jamais apparaître dans les logs ou messages d'erreur
- NFR6 : Les credentials sont stockés exclusivement dans des variables d'environnement en production
- NFR7 : Le bot rejette 100% des commandes de chat IDs non autorisés

### Intégration

- NFR8 : Délai minimum de 500ms entre chaque requête à l'API bet2invest
- NFR9 : En cas de changement d'API bet2invest, le système retourne un code d'erreur identifiable, logue le changement détecté (endpoint, code HTTP, payload), et envoie une notification Telegram — jamais de crash silencieux
- NFR10 : Support des interruptions temporaires de l'API Telegram (retry avec backoff)

### Maintenabilité

- NFR11 : Code API bet2invest isolé dans un module dédié pour faciliter l'adaptation
- NFR12 : Chaque entrée de log inclut au minimum : timestamp, étape du cycle (auth/scrape/select/publish), tipster concerné le cas échéant, et code d'erreur — permettant de diagnostiquer un échec sans débogueur
