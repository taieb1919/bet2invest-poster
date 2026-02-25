---
stepsCompleted:
  - step-01-prerequisites
  - step-02-design-epics
  - step-03-create-stories
  - step-04-final-validation
inputDocuments:
  - .bmadOutput/planning-artifacts/prd.md
  - .bmadOutput/planning-artifacts/architecture.md
  - .bmadOutput/planning-artifacts/epics-phase2.md
---

# bet2invest-poster - Epic Breakdown (Phase 4)

## Overview

This document provides the epic and story breakdown for bet2invest-poster Phase 4, extending the project after completion of Phases 1-3 (Epics 1-12).

## Requirements Inventory

### Functional Requirements

FR37 : Le message Telegram de succès affiche le nombre de pronostics scrapés (total disponible) en plus du nombre publié
FR38 : Le message Telegram de succès inclut le détail des pronostics publiés (match, cote, tipster)
FR39 : Le système exécute le cycle de publication 3 fois par jour (configurable — nombre et horaires)
FR40 : L'utilisateur peut envoyer `/help` pour afficher la liste de toutes les commandes avec leur description
FR41 : Les commandes du bot Telegram sont enregistrées via `setMyCommands` de l'API Bot pour apparaître dans le menu Telegram

### NonFunctional Requirements

(Pas de nouveaux NFRs — les NFRs existants s'appliquent)

### Additional Requirements

- Les nouvelles commandes Telegram suivent le pattern `CommandHandler` existant dans `Telegram/Commands/`
- Configuration des horaires multiples via `PosterOptions` / `appsettings.json`
- `setMyCommands` appelé au démarrage du bot dans `TelegramBotService`

### FR Coverage Map

| FR | Epic | Description |
|---|---|---|
| FR37 | Epic 13 | Nombre de pronostics scrapés dans le message |
| FR38 | Epic 13 | Détail des pronostics publiés dans le message |
| FR39 | Epic 14 | 3 exécutions/jour configurables |
| FR40 | Epic 15 | Commande /help |
| FR41 | Epic 15 | setMyCommands pour le menu bot |

## Epic List

### Epic 13 : Messages de Publication Enrichis
L'utilisateur reçoit un message Telegram détaillé après chaque cycle : nombre total de pronostics scrapés, nombre publiés, et le détail de chaque pronostic.
**FRs couvertes :** FR37, FR38

### Epic 14 : Exécutions Multiples par Jour
Le système exécute le cycle de publication plusieurs fois par jour (3 par défaut, configurable en nombre et horaires).
**FRs couvertes :** FR39

### Epic 15 : Commande /help et Menu des Commandes
L'utilisateur peut envoyer `/help` pour voir toutes les commandes disponibles. Les commandes sont enregistrées dans le menu Telegram via `setMyCommands`.
**FRs couvertes :** FR40, FR41

## Epic 13 : Messages de Publication Enrichis

L'utilisateur reçoit un message Telegram détaillé après chaque cycle : nombre total de pronostics scrapés, nombre publiés, et le détail de chaque pronostic.

### Story 13.1 : Enrichir le message de succès avec les statistiques de scraping

As a l'utilisateur,
I want voir le nombre total de pronostics scrapés et le nombre publié dans le message de succès,
So that j'aie une vision claire de la couverture du scraping à chaque cycle.

**Acceptance Criteria:**

**Given** un cycle de publication terminé avec succès
**When** la notification Telegram est envoyée
**Then** le message inclut le nombre total de pronostics scrapés (candidats disponibles) et le nombre effectivement publié (FR37)
**And** le format est : `"✅ {published} pronostics publiés sur {scraped} scrapés."`

**Given** le cycle applique des filtres (cotes, plage horaire)
**When** la notification est envoyée
**Then** le message affiche aussi le nombre après filtrage : `"✅ {published}/{filtered} filtrés sur {scraped} scrapés."`

**Given** zéro pronostic scrapé
**When** la notification est envoyée
**Then** le message est : `"⚠️ Aucun pronostic disponible chez les tipsters configurés."`

### Story 13.2 : Détail des pronostics publiés dans le message

As a l'utilisateur,
I want voir le détail de chaque pronostic publié (match, cote, tipster) dans le message Telegram,
So that je sache exactement ce qui a été publié sans aller sur bet2invest.

**Acceptance Criteria:**

**Given** un cycle de publication terminé avec succès
**When** la notification Telegram est envoyée
**Then** le message inclut la liste des pronostics publiés, chacun avec : description du match, cote, nom du tipster (FR38)
**And** le format de chaque ligne est : `"• {matchDescription} — {odds} ({tipsterName})"`

**Given** plus de 15 pronostics publiés
**When** le message est formaté
**Then** seuls les 15 premiers sont affichés avec une note `"... et {n} autres"`

**Given** un pronostic sans description de match disponible
**When** le message est formaté
**Then** la ligne affiche `"• (sans description) — {odds} ({tipsterName})"`

## Epic 14 : Exécutions Multiples par Jour

Le système exécute le cycle de publication plusieurs fois par jour (3 par défaut, configurable en nombre et horaires).

### Story 14.1 : Scheduling multi-horaires configurable

As a l'utilisateur,
I want configurer plusieurs horaires d'exécution par jour au lieu d'un seul,
So that mes pronostics soient publiés à différents moments de la journée pour couvrir plus d'événements.

**Acceptance Criteria:**

**Given** `PosterOptions.ScheduleTimes` configuré avec `["08:00", "13:00", "19:00"]` dans `appsettings.json`
**When** le `SchedulerWorker` calcule les prochains runs
**Then** le cycle s'exécute à chacun des 3 horaires configurés chaque jour (FR39)
**And** chaque exécution est un cycle complet indépendant (scrape → select → publish → notify)

**Given** l'ancien paramètre `ScheduleTime` (string unique) est présent
**When** le service démarre
**Then** le système utilise `ScheduleTimes` (tableau) en priorité si défini, sinon fait un fallback sur `ScheduleTime` converti en tableau d'un élément (rétrocompatibilité)

**Given** `ScheduleTimes` non configuré et `ScheduleTime` non configuré
**When** le service démarre
**Then** la valeur par défaut est `["08:00", "13:00", "19:00"]` (3 exécutions/jour)

**Given** la commande `/status` envoyée
**When** le bot répond
**Then** tous les prochains horaires de la journée sont affichés (pas seulement le prochain)

**Given** la commande `/schedule` existante
**When** l'utilisateur envoie `/schedule 08:00,13:00,19:00`
**Then** les horaires sont mis à jour avec les nouvelles valeurs (séparées par virgule)
**And** le bot confirme `"⏰ Horaires mis à jour : 08:00, 13:00, 19:00. Prochain run : {date/heure}."`

**Given** un horaire invalide dans la liste (ex: `08:00,25:00,19:00`)
**When** le bot reçoit la commande `/schedule`
**Then** le bot rejette la commande entière : `"❌ Horaire invalide : 25:00. Usage : /schedule HH:mm[,HH:mm,...]"`

**Given** le cycle s'exécute à 13:00
**When** les doublons sont vérifiés
**Then** les pronostics publiés à 08:00 le même jour sont inclus dans la détection de doublons (pas de republication intra-jour)

## Epic 15 : Commande /help et Menu des Commandes

L'utilisateur peut envoyer `/help` pour voir toutes les commandes disponibles. Les commandes sont enregistrées dans le menu Telegram via `setMyCommands`.

### Story 15.1 : Commande /help — Liste des commandes disponibles

As a l'utilisateur,
I want envoyer `/help` pour afficher toutes les commandes du bot avec leur description,
So that je puisse découvrir et utiliser toutes les fonctionnalités sans documentation externe.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/help`
**Then** `HelpCommandHandler` répond avec la liste complète des commandes disponibles (FR40)
**And** chaque commande est affichée avec sa syntaxe et une description courte

**Given** une nouvelle commande ajoutée au bot dans le futur
**When** le développeur ajoute un `CommandHandler`
**Then** la liste dans `/help` doit être mise à jour manuellement (pas de découverte automatique)

**Given** un utilisateur non autorisé envoie `/help`
**When** `AuthorizationFilter` filtre la commande
**Then** la commande est ignorée silencieusement (comportement existant FR20)

### Story 15.2 : Enregistrement des commandes via setMyCommands

As a l'utilisateur,
I want que les commandes du bot apparaissent dans le menu natif de Telegram (bouton "/" ou autocomplétion),
So that je puisse découvrir et saisir les commandes facilement sans les mémoriser.

**Acceptance Criteria:**

**Given** le `TelegramBotService` démarre
**When** le bot se connecte à l'API Telegram
**Then** `SetMyCommandsAsync` est appelé avec la liste complète des commandes et leurs descriptions (FR41)
**And** les commandes sont enregistrées une seule fois au démarrage

**Given** la liste des commandes enregistrées
**When** l'utilisateur tape "/" dans le chat Telegram
**Then** le menu natif Telegram affiche toutes les commandes avec leur description

**Given** le `SetMyCommandsAsync` échoue (API Telegram indisponible)
**When** l'erreur est détectée
**Then** le bot logue l'erreur avec le Step `Notify` et continue son démarrage normalement (non bloquant)
**And** les commandes restent utilisables manuellement

