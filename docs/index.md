# bet2invest-poster — Documentation Index

**Généré le :** 2026-02-25
**Scan level :** Quick (pattern-based)

## Project Overview

- **Type :** Monolith — Backend Worker Service
- **Langage :** C# 13 / .NET 9
- **Architecture :** Service-oriented avec orchestrateur central + Command pattern Telegram
- **Déploiement :** systemd sur VPS Linux

## Quick Reference

- **Tech Stack :** .NET 9, Telegram.Bot 22.9.0, Polly.Core 8.6.5, Serilog 4.3.1
- **Entry Point :** `src/Bet2InvestPoster/Program.cs`
- **Build :** `dotnet build Bet2InvestPoster.sln`
- **Tests :** `dotnet test tests/Bet2InvestPoster.Tests` (318+ tests)
- **Run :** `./app.run.sh` ou `dotnet run --project src/Bet2InvestPoster`

## Generated Documentation

- [Project Overview](./project-overview.md) — Vue d'ensemble, stack, métriques
- [Architecture](./architecture.md) — Couches, cycle de publication, patterns, DI, sécurité
- [Source Tree Analysis](./source-tree-analysis.md) — Arbre source annoté complet
- [Component Inventory](./component-inventory.md) — Services, commandes, modèles, configuration
- [Development Guide](./development-guide.md) — Prérequis, build, tests, patterns
- [Deployment Guide](./deployment-guide.md) — systemd, VPS, health checks, logs

## Existing Documentation

- [README](../README.md) — Minimal (titre seulement)

## Getting Started

1. Cloner avec submodules : `git clone --recursive <repo-url>`
2. Configurer `.env` avec les credentials (Bet2Invest, Telegram, BankrollId)
3. Build : `dotnet build Bet2InvestPoster.sln`
4. Tests : `dotnet test tests/Bet2InvestPoster.Tests`
5. Run : `./app.run.sh`
6. Envoyer `/status` au bot Telegram pour vérifier
