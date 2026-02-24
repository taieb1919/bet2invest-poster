---
stepsCompleted:
  - step-01-prerequisites
  - step-02-design-epics
  - step-03-create-stories
  - step-04-final-validation
inputDocuments:
  - .bmadOutput/planning-artifacts/prd.md
  - .bmadOutput/planning-artifacts/architecture.md
  - .bmadOutput/planning-artifacts/epics.md
---

# bet2invest-poster - Epic Breakdown (Phase 2 & 3)

## Overview

This document provides the epic and story breakdown for bet2invest-poster Phase 2 (Post-MVP) and Phase 3 (Expansion), extending the MVP completed in Phase 1 (Epics 1-6).

## Requirements Inventory

### Functional Requirements

FR24 : L'utilisateur peut activer le scheduling automatique via `/start`
FR25 : L'utilisateur peut suspendre le scheduling via `/stop`
FR26 : L'utilisateur peut consulter l'historique des 7 dernières publications via `/history`
FR27 : L'utilisateur peut configurer l'heure d'exécution via `/schedule <HH:mm>` en Telegram
FR28 : L'utilisateur peut afficher la liste des tipsters actuels via `/tipsters`
FR29 : L'utilisateur peut ajouter un tipster via `/tipsters add <lien>`
FR30 : L'utilisateur peut retirer un tipster via `/tipsters remove <lien>`
FR31 : Le système propose un onboarding guidé au premier lancement via Telegram
FR32 : Le système scrape automatiquement les tipsters free et propose une mise à jour via `/tipsters update`
FR33 : Le système sélectionne les pronostics en multi-critères (ROI + taux de réussite + sport) au lieu d'aléatoire
FR34 : Le système génère un reporting sur les performances des pronostics republiés
FR35 : L'utilisateur peut configurer une fourchette de cotes acceptées (min/max) — les pronostics hors fourchette sont exclus de la sélection
FR36 : L'utilisateur peut configurer une plage horaire maximale (ex: 24h ou 48h) — seuls les événements démarrant dans les X prochaines heures sont retenus

### NonFunctional Requirements

NFR13 : Rotation quotidienne des logs Serilog avec rétention configurable
NFR14 : Configuration Polly avancée (circuit breaker, backoff exponentiel)
NFR15 : Health checks endpoint pour monitoring externe

### Additional Requirements

**Architecture (Post-MVP) :**
- Déploiement automatisé via CI/CD (CI existant, CD à ajouter)
- Les nouvelles commandes Telegram doivent suivre le même pattern `CommandHandler` existant
- `TipsterService` doit être étendu (pas remplacé) pour supporter CRUD
- Écriture atomique de `tipsters.json` (write-to-temp + rename) comme pour `history.json`
- La sélection multi-critères Phase 3 remplace `BetSelector` actuel (mode aléatoire → mode intelligent)
- Nouveaux paramètres `PosterOptions` : `MinOdds`, `MaxOdds`, `EventHorizonHours`

### FR Coverage Map

| FR | Epic | Description |
|---|---|---|
| FR24 | Epic 7 | `/start` — activer scheduling |
| FR25 | Epic 7 | `/stop` — suspendre scheduling |
| FR26 | Epic 7 | `/history` — historique 7 dernières publications |
| FR27 | Epic 7 | `/schedule <HH:mm>` — configurer heure |
| FR28 | Epic 8 | `/tipsters` — afficher liste |
| FR29 | Epic 8 | `/tipsters add` — ajouter tipster |
| FR30 | Epic 8 | `/tipsters remove` — retirer tipster |
| FR31 | Epic 10 | Onboarding guidé Telegram |
| FR32 | Epic 11 | `/tipsters update` — scraping auto |
| FR33 | Epic 11 | Sélection multi-critères |
| FR34 | Epic 12 | Reporting performances |
| FR35 | Epic 9 | Fourchette de cotes acceptées |
| FR36 | Epic 9 | Plage horaire événements |

## Epic List

### Epic 7 : Commandes Telegram Étendues — Contrôle Complet
L'utilisateur contrôle totalement le scheduling du service depuis Telegram, sans accéder au VPS.
**FRs couvertes :** FR24, FR25, FR26, FR27
**NFRs adressées :** —

### Epic 8 : Gestion des Tipsters via Telegram
L'utilisateur gère sa liste de tipsters (consulter, ajouter, retirer) directement depuis Telegram sans éditer de fichier sur le VPS.
**FRs couvertes :** FR28, FR29, FR30
**NFRs adressées :** —

### Epic 9 : Filtrage Avancé des Pronostics
L'utilisateur affine la qualité de ses publications avec des critères de cotes acceptées et de plage horaire des événements.
**FRs couvertes :** FR35, FR36
**NFRs adressées :** —

### Epic 10 : Onboarding et Qualité Opérationnelle
Le système guide l'utilisateur au premier lancement et améliore sa fiabilité opérationnelle (logs, monitoring, résilience avancée).
**FRs couvertes :** FR31
**NFRs adressées :** NFR13, NFR14, NFR15

### Epic 11 : Sélection Intelligente et Tipsters Automatisés (Phase 3)
Le système propose automatiquement les meilleurs tipsters free et remplace la sélection aléatoire par une sélection multi-critères intelligente.
**FRs couvertes :** FR32, FR33
**NFRs adressées :** —

### Epic 12 : Reporting des Performances (Phase 3)
L'utilisateur suit les performances de ses pronostics republiés pour optimiser sa stratégie de sélection.
**FRs couvertes :** FR34
**NFRs adressées :** —

## Epic 7 : Commandes Telegram Étendues — Contrôle Complet

L'utilisateur contrôle totalement le scheduling du service depuis Telegram, sans accéder au VPS.

### Story 7.1 : Commandes /start et /stop — Contrôle du Scheduling

As a l'utilisateur,
I want activer ou suspendre le scheduling automatique via `/start` et `/stop` depuis Telegram,
So that je puisse contrôler quand le service publie sans accéder au VPS.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/stop`
**Then** le `SchedulerWorker` suspend le prochain déclenchement automatique (FR25)
**And** le bot répond `"⏸️ Scheduling suspendu. Utilisez /start pour reprendre."`
**And** `/run` reste fonctionnel (exécution manuelle non affectée)

**Given** le scheduling suspendu
**When** l'utilisateur envoie `/start`
**Then** le `SchedulerWorker` reprend le scheduling automatique à l'heure configurée (FR24)
**And** le bot répond `"▶️ Scheduling activé. Prochain run : {heure}."`

**Given** le scheduling déjà actif
**When** l'utilisateur envoie `/start`
**Then** le bot répond `"ℹ️ Scheduling déjà actif. Prochain run : {heure}."`

**Given** le service redémarré après un `/stop`
**When** le service démarre
**Then** l'état du scheduling (actif/suspendu) est persisté et restauré

### Story 7.2 : Commande /history — Historique des Publications

As a l'utilisateur,
I want consulter l'historique des dernières publications via `/history`,
So that je puisse vérifier ce qui a été publié récemment sans accéder au VPS.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/history`
**Then** `HistoryCommandHandler` lit `history.json` et affiche les 7 dernières publications (FR26)
**And** chaque entrée affiche : date, nombre de pronostics publiés, statut (succès/échec)
**And** le message est formaté via `MessageFormatter` en bloc lisible

**Given** aucune publication dans l'historique
**When** l'utilisateur envoie `/history`
**Then** le bot répond `"📭 Aucune publication dans l'historique."`

### Story 7.3 : Commande /schedule — Configuration Horaire via Telegram

As a l'utilisateur,
I want configurer l'heure d'exécution quotidienne via `/schedule <HH:mm>` depuis Telegram,
So that je puisse ajuster l'horaire de publication sans modifier de fichier de configuration.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/schedule 10:30`
**Then** `ScheduleCommandHandler` met à jour `PosterOptions.ScheduleTime` en mémoire et persiste le changement (FR27)
**And** le `SchedulerWorker` recalcule le prochain run avec la nouvelle heure
**And** le bot répond `"⏰ Heure de publication mise à jour : 10:30. Prochain run : {date/heure}."`

**Given** l'utilisateur envoie `/schedule` sans argument
**When** le bot reçoit la commande
**Then** le bot répond avec l'heure actuelle : `"⏰ Heure actuelle : {HH:mm}. Usage : /schedule HH:mm"`

**Given** l'utilisateur envoie `/schedule 25:99` (format invalide)
**When** le bot reçoit la commande
**Then** le bot répond `"❌ Format invalide. Usage : /schedule HH:mm (ex: /schedule 08:00)"`

## Epic 8 : Gestion des Tipsters via Telegram

L'utilisateur gère sa liste de tipsters (consulter, ajouter, retirer) directement depuis Telegram sans éditer de fichier sur le VPS.

### Story 8.1 : Commande /tipsters — Consultation de la Liste

As a l'utilisateur,
I want afficher la liste de mes tipsters actuels via `/tipsters`,
So that je puisse vérifier quels tipsters sont configurés sans accéder au VPS.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/tipsters`
**Then** `TipstersCommandHandler` lit `tipsters.json` et affiche la liste complète (FR28)
**And** chaque tipster affiche : nom, URL, statut (free/premium)
**And** le nombre total de tipsters est affiché en fin de message

**Given** `tipsters.json` vide ou inexistant
**When** l'utilisateur envoie `/tipsters`
**Then** le bot répond `"📭 Aucun tipster configuré. Utilisez /tipsters add <lien> pour en ajouter."`

### Story 8.2 : Commandes /tipsters add et /tipsters remove — CRUD Tipsters

As a l'utilisateur,
I want ajouter ou retirer des tipsters via `/tipsters add <lien>` et `/tipsters remove <lien>` depuis Telegram,
So that je puisse mettre à jour ma liste de tipsters sans éditer de fichier sur le VPS.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/tipsters add https://bet2invest.com/tipster/johndoe`
**Then** `TipstersCommandHandler` ajoute le tipster dans `tipsters.json` avec écriture atomique (write-to-temp + rename) (FR29)
**And** le bot répond `"✅ Tipster ajouté : johndoe"`
**And** le tipster est disponible dès le prochain cycle d'exécution

**Given** le lien fourni est déjà dans la liste
**When** l'utilisateur envoie `/tipsters add <lien_existant>`
**Then** le bot répond `"ℹ️ Ce tipster est déjà dans la liste."`

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/tipsters remove https://bet2invest.com/tipster/johndoe`
**Then** le tipster est retiré de `tipsters.json` avec écriture atomique (FR30)
**And** le bot répond `"🗑️ Tipster retiré : johndoe"`

**Given** le lien fourni n'existe pas dans la liste
**When** l'utilisateur envoie `/tipsters remove <lien_inconnu>`
**Then** le bot répond `"❌ Tipster non trouvé dans la liste."`

**Given** l'utilisateur envoie `/tipsters add` sans argument
**When** le bot reçoit la commande
**Then** le bot répond `"Usage : /tipsters add <lien_tipster>"`

## Epic 9 : Filtrage Avancé des Pronostics

L'utilisateur affine la qualité de ses publications avec des critères de cotes acceptées et de plage horaire des événements.

### Story 9.1 : Filtrage par Cotes et Plage Horaire

As a l'utilisateur,
I want configurer une fourchette de cotes acceptées et une plage horaire maximale pour les événements,
So that seuls les pronostics pertinents (cotes raisonnables, événements proches) soient publiés.

**Acceptance Criteria:**

**Given** `PosterOptions` configuré avec `MinOdds: 1.20`, `MaxOdds: 3.50`, `EventHorizonHours: 24`
**When** `BetSelector` filtre les paris candidats
**Then** les paris avec une cote < `MinOdds` ou > `MaxOdds` sont exclus de la sélection (FR35)
**And** les paris dont l'événement démarre au-delà de `EventHorizonHours` heures sont exclus (FR36)
**And** le filtrage est appliqué AVANT la sélection aléatoire

**Given** `MinOdds` et `MaxOdds` non configurés (valeurs par défaut)
**When** le cycle s'exécute
**Then** aucun filtrage par cotes n'est appliqué (comportement rétrocompatible)

**Given** `EventHorizonHours` non configuré (valeur par défaut)
**When** le cycle s'exécute
**Then** aucun filtrage par plage horaire n'est appliqué (comportement rétrocompatible)

**Given** les filtres configurés réduisent les candidats à zéro
**When** `BetSelector` effectue la sélection
**Then** le cycle se termine avec un message `"⚠️ Aucun pronostic ne correspond aux critères de filtrage."`
**And** une notification Telegram est envoyée avec le détail des filtres actifs

**Given** l'utilisateur configure les filtres via `appsettings.json` ou variables d'environnement
**When** le service démarre
**Then** les paramètres `MinOdds`, `MaxOdds`, `EventHorizonHours` sont chargés dans `PosterOptions`
**And** les variables d'environnement surchargent `appsettings.json` (ex: `Poster__MinOdds=1.50`)

**Given** le cycle s'exécute avec filtrage actif
**When** les logs sont écrits
**Then** le nombre de candidats avant et après filtrage est logué avec le Step `Select`

## Epic 10 : Onboarding et Qualité Opérationnelle

Le système guide l'utilisateur au premier lancement et améliore sa fiabilité opérationnelle (logs, monitoring, résilience avancée).

### Story 10.1 : Onboarding Guidé via Telegram

As a l'utilisateur,
I want être guidé au premier lancement du bot pour vérifier que tout est correctement configuré,
So that je puisse confirmer que le service est opérationnel sans connaissances techniques approfondies.

**Acceptance Criteria:**

**Given** le service démarre pour la première fois (aucun `history.json` existant)
**When** le bot se connecte à Telegram
**Then** le bot envoie un message d'onboarding à l'utilisateur autorisé (FR31)
**And** le message inclut : confirmation de connexion API bet2invest, nombre de tipsters chargés, heure de scheduling configurée, liste des commandes disponibles
**And** le bot propose `"Envoyez /run pour tester une première publication, ou /status pour vérifier l'état."`

**Given** le service a déjà fonctionné (`history.json` existe)
**When** le service redémarre
**Then** aucun message d'onboarding n'est envoyé

**Given** la connexion API bet2invest échoue au premier lancement
**When** le bot envoie le message d'onboarding
**Then** le message indique clairement l'erreur : `"⚠️ Connexion API bet2invest échouée — vérifiez vos credentials."`

### Story 10.2 : Rotation des Logs et Rétention Configurable

As a l'utilisateur,
I want que les logs soient automatiquement rotés et purgés selon une durée configurable,
So that l'espace disque du VPS ne soit pas saturé par les fichiers de logs.

**Acceptance Criteria:**

**Given** Serilog configuré avec le sink File
**When** le service écrit des logs
**Then** les fichiers de logs sont rotés quotidiennement (NFR13)
**And** le nom du fichier inclut la date (ex: `bet2invest-poster-20260224.log`)

**Given** `PosterOptions.LogRetentionDays` configuré (ex: 30)
**When** un nouveau fichier de log est créé
**Then** les fichiers de log plus anciens que `LogRetentionDays` jours sont supprimés automatiquement

**Given** `LogRetentionDays` non configuré
**When** le service démarre
**Then** la rétention par défaut est de 30 jours

### Story 10.3 : Résilience Polly Avancée et Health Checks

As a l'utilisateur,
I want que le système gère les pannes de manière plus intelligente et expose un endpoint de santé,
So that le service soit plus résilient et monitorable en production.

**Acceptance Criteria:**

**Given** le `ResiliencePipeline` Polly existant
**When** le pipeline est configuré
**Then** un circuit breaker est ajouté : après 3 échecs consécutifs, le circuit s'ouvre pendant 5 minutes (NFR14)
**And** le retry utilise un backoff exponentiel au lieu d'un délai fixe (60s → 60s, 120s, 240s)
**And** les paramètres du circuit breaker sont configurables via `PosterOptions`

**Given** le circuit breaker ouvert
**When** un cycle est déclenché (automatique ou `/run`)
**Then** le cycle échoue immédiatement avec `"🔴 Circuit breaker actif — service API indisponible. Réessai automatique dans {minutes} min."`
**And** une notification Telegram est envoyée

**Given** le service en cours d'exécution
**When** une requête HTTP GET arrive sur `/health`
**Then** le endpoint retourne `200 OK` avec : statut du service, dernière exécution, état du circuit breaker, connexion API (NFR15)

**Given** le service en cours d'exécution
**When** une requête HTTP GET arrive sur `/health` et le circuit breaker est ouvert
**Then** le endpoint retourne `503 Service Unavailable` avec le détail

## Epic 11 : Sélection Intelligente et Tipsters Automatisés (Phase 3)

Le système propose automatiquement les meilleurs tipsters free et remplace la sélection aléatoire par une sélection multi-critères intelligente.

### Story 11.1 : Commande /tipsters update — Scraping et Suggestion Automatique

As a l'utilisateur,
I want que le système scrape automatiquement les tipsters free de bet2invest et me propose une liste mise à jour,
So that ma liste de tipsters reste optimale sans recherche manuelle sur le site.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/tipsters update`
**Then** le système utilise `ExtendedBet2InvestClient` pour scraper la liste des tipsters free triés par ROI descendant (FR32)
**And** le bot affiche la liste proposée avec : nom, ROI, nombre de pronostics, sport principal
**And** le bot demande confirmation : `"Voulez-vous remplacer votre liste actuelle ? [Oui / Non / Fusionner]"`

**Given** l'utilisateur répond "Oui"
**When** la confirmation est reçue
**Then** `tipsters.json` est remplacé par la nouvelle liste avec écriture atomique
**And** le bot confirme `"✅ Liste mise à jour : {count} tipsters."`

**Given** l'utilisateur répond "Fusionner"
**When** la confirmation est reçue
**Then** les nouveaux tipsters sont ajoutés aux existants (sans doublons)
**And** le bot confirme `"✅ {added} tipsters ajoutés. Total : {count}."`

**Given** l'utilisateur répond "Non"
**When** la confirmation est reçue
**Then** aucune modification n'est effectuée
**And** le bot confirme `"❌ Mise à jour annulée."`

**Given** le scraping échoue (API indisponible)
**When** l'erreur est détectée
**Then** le bot répond `"❌ Impossible de récupérer la liste des tipsters — {raison}."`

### Story 11.2 : Sélection Multi-Critères Intelligente

As a l'utilisateur,
I want que le système sélectionne les pronostics selon des critères intelligents (ROI, taux de réussite, sport) au lieu d'aléatoire,
So that la qualité de mes publications soit optimisée.

**Acceptance Criteria:**

**Given** `PosterOptions.SelectionMode` configuré à `"intelligent"` (défaut : `"random"` pour rétrocompatibilité)
**When** `BetSelector` effectue la sélection
**Then** les pronostics sont scorés selon : ROI du tipster (40%), taux de réussite du tipster (30%), diversité de sport (20%), fraîcheur de l'événement (10%) (FR33)
**And** les pronostics avec le score le plus élevé sont sélectionnés en priorité
**And** le nombre sélectionné reste 5, 10 ou 15 (aléatoire comme avant)

**Given** `SelectionMode` configuré à `"random"`
**When** le cycle s'exécute
**Then** le comportement est identique au MVP (sélection aléatoire pure)

**Given** le mode intelligent actif
**When** les logs sont écrits
**Then** chaque pronostic sélectionné est logué avec son score et les critères détaillés (Step `Select`)

**Given** les données de ROI ou taux de réussite ne sont pas disponibles pour un tipster
**When** `BetSelector` calcule le score
**Then** les critères manquants sont ignorés et le poids est redistribué sur les critères disponibles

**Given** l'utilisateur configure `SelectionMode` via variable d'environnement
**When** le service démarre
**Then** `Poster__SelectionMode=intelligent` active le mode intelligent

## Epic 12 : Reporting des Performances (Phase 3)

L'utilisateur suit les performances de ses pronostics republiés pour optimiser sa stratégie de sélection.

### Story 12.1 : Suivi des Résultats des Pronostics Publiés

As a l'utilisateur,
I want que le système vérifie automatiquement les résultats (gagné/perdu) de mes pronostics publiés,
So that je dispose de données fiables pour évaluer la qualité de mes publications.

**Acceptance Criteria:**

**Given** des pronostics publiés enregistrés dans `history.json`
**When** le cycle quotidien s'exécute
**Then** `ResultTracker` vérifie les résultats des pronostics publiés dans les derniers 7 jours via l'API bet2invest
**And** chaque entrée dans `history.json` est enrichie avec : `result` (won/lost/pending), `odds`, `sport`, `tipsterName`
**And** l'écriture dans `history.json` reste atomique (write-to-temp + rename)

**Given** l'API bet2invest ne retourne pas encore le résultat d'un pronostic
**When** `ResultTracker` interroge l'API
**Then** le pronostic reste en statut `pending` et sera revérifié au prochain cycle

**Given** le résultat d'un pronostic est résolu (won/lost)
**When** `ResultTracker` met à jour `history.json`
**Then** le résultat est définitif et ne sera plus revérifié

**Given** le cycle de vérification des résultats
**When** les logs sont écrits
**Then** chaque vérification est loguée avec le Step `Report` (nombre vérifié, nombre résolu, nombre pending)

### Story 12.2 : Commande /report — Tableau de Bord des Performances

As a l'utilisateur,
I want consulter un rapport de performances de mes pronostics publiés via `/report`,
So that je puisse évaluer l'efficacité de ma stratégie de sélection et l'ajuster.

**Acceptance Criteria:**

**Given** le bot Telegram actif et l'utilisateur autorisé
**When** l'utilisateur envoie `/report`
**Then** `ReportCommandHandler` génère un rapport basé sur `history.json` (FR34)
**And** le rapport inclut :
- Période couverte (ex: "7 derniers jours" / "30 derniers jours")
- Nombre total de pronostics publiés
- Taux de réussite (won / total résolu)
- ROI moyen des pronostics gagnants
- Répartition par sport
- Top 3 tipsters les plus performants
**And** le message est formaté via `MessageFormatter` en bloc lisible

**Given** l'utilisateur envoie `/report 30` (avec argument jours)
**When** le bot reçoit la commande
**Then** le rapport couvre les 30 derniers jours au lieu de la période par défaut (7 jours)

**Given** aucun pronostic résolu dans la période demandée
**When** l'utilisateur envoie `/report`
**Then** le bot répond `"📊 Aucun pronostic résolu sur cette période. Les résultats sont vérifiés quotidiennement."`
