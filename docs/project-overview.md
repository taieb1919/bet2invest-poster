# bet2invest-poster — Vue d'ensemble du projet

**Généré le :** 2026-02-25
**Type :** Backend Worker Service (.NET 9 / C# 13)
**Repository :** Monolith
**Architecture :** Service-oriented avec orchestrateur central

## Résumé

bet2invest-poster est un service Worker .NET 9 qui automatise la republication de pronostics sportifs depuis la plateforme bet2invest. Il s'exécute en tant que service systemd sur un VPS Linux et offre une interface de contrôle complète via un bot Telegram.

## Fonctionnalités principales

| Domaine | Fonctionnalité |
|---------|---------------|
| **Cycle de publication** | Scraping tipsters → fetch paris → sélection (aléatoire ou intelligente) → publication via API → notification Telegram |
| **Scheduling** | Exécution quotidienne planifiée, contrôlable via `/start`, `/stop`, `/schedule` |
| **Gestion tipsters** | CRUD via `/tipsters`, `/tipsters add`, `/tipsters remove`, `/tipsters update` |
| **Filtrage** | Fourchette de cotes (min/max), plage horaire des événements |
| **Reporting** | Suivi automatique des résultats (won/lost/pending), commande `/report` |
| **Résilience** | Polly retry + circuit breaker, health checks HTTP |
| **Logging** | Serilog dual (console + fichier JSON), rotation quotidienne, rétention configurable |
| **Déploiement** | systemd avec sd_notify, CI GitHub Actions |

## Stack technologique

| Catégorie | Technologie | Version |
|-----------|------------|---------|
| Framework | .NET SDK | 9.0 |
| Langage | C# | 13 |
| Type de projet | Microsoft.NET.Sdk.Web (Worker) | — |
| Bot | Telegram.Bot | 22.9.0 |
| Résilience | Polly.Core | 8.6.5 |
| Logging | Serilog + Console + File | 4.3.1 |
| Hosting | Microsoft.Extensions.Hosting.Systemd | 9.0.8 |
| Tests | xunit | — |
| CI | GitHub Actions | — |
| Submodule | jtdev-bet2invest-scraper (lecture seule) | — |

## Métriques du code

| Métrique | Valeur |
|----------|--------|
| Fichiers source (.cs) | 54 |
| Fichiers tests (.cs) | 31 |
| Interfaces | 15 |
| Services | ~25 classes |
| Commandes Telegram | 8 handlers |
| Tests totaux | 318+ |

## Architecture simplifiée

```
┌─────────────────────────────────────────────┐
│              SchedulerWorker                 │
│         (BackgroundService, cron)            │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│          PostingCycleService                  │
│      (Orchestrateur du cycle complet)        │
│                                              │
│  Purge → Tipsters → Fetch → Select →        │
│  Publish → Track Results → Notify            │
└──────────────────────────────────────────────┘
         │              │              │
    ┌────▼────┐   ┌────▼────┐   ┌────▼────┐
    │ History │   │Extended │   │Telegram │
    │ Manager │   │ B2I     │   │Bot Svc  │
    │(JSON)   │   │ Client  │   │(polling)│
    └─────────┘   └─────────┘   └─────────┘
```

## Fichiers de données persistants

| Fichier | Responsable | Description |
|---------|-------------|-------------|
| `history.json` | HistoryManager | Historique des paris publiés + résultats |
| `tipsters.json` | TipsterService | Liste des tipsters configurés |
| `scheduling-state.json` | ExecutionStateService | État du scheduling (enabled, dernière/prochaine exécution) |

## Documentation associée

- [Architecture détaillée](./architecture.md)
- [Arbre source annoté](./source-tree-analysis.md)
- [Guide de développement](./development-guide.md)
- [Guide de déploiement](./deployment-guide.md)
- [Inventaire des composants](./component-inventory.md)
