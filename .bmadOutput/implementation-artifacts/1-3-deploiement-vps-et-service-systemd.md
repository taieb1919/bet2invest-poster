# Story 1.3 : Déploiement VPS et Service systemd

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want déployer le service sur mon VPS comme un service systemd,
So that le service tourne en continu et redémarre automatiquement en cas de crash.

## Acceptance Criteria

1. **Given** le fichier `deploy/bet2invest-poster.service` configuré
   **When** le fichier est installé sur le VPS (`/etc/systemd/system/`)
   **Then** systemd démarre le service et le redémarre en cas de crash (délai < 30s, NFR1)
   **And** `RestartSec` est ≤ 5 secondes (bien inférieur à 30s)

2. **Given** le service déployé dans `/opt/bet2invest-poster/`
   **When** le service tourne
   **Then** il s'exécute en continu comme background service (FR23)
   **And** `AddSystemd()` (déjà configuré en story 1.2) permet l'intégration native avec systemd (notifications de démarrage/arrêt propres)

3. **Given** le fichier `deploy/bet2invest-poster.service` avec une directive `EnvironmentFile`
   **When** le service démarre
   **Then** les credentials (`Bet2Invest__Identifier`, `Bet2Invest__Password`, `Telegram__BotToken`, `Telegram__AuthorizedChatId`) sont chargées depuis le fichier d'env systemd (NFR6)
   **And** les credentials ne sont jamais dans `appsettings.json` en production (NFR5)

4. **Given** `PosterOptions.LogPath = "logs"` (valeur par défaut)
   **When** le service tourne depuis `/opt/bet2invest-poster/`
   **Then** les logs Serilog sont écrits dans `/opt/bet2invest-poster/logs/` (répertoire relatif au WorkingDirectory)

5. **Given** le repository GitHub avec `.github/workflows/ci.yml`
   **When** un push ou une PR est créée sur la branche `main`
   **Then** GitHub Actions exécute : checkout (avec submodules) → setup .NET 9 → restore → build → test
   **And** les étapes build et test réussissent sans erreur

6. **Given** le projet publié avec `dotnet publish`
   **When** le binaire est copié sur le VPS dans `/opt/bet2invest-poster/`
   **Then** le service démarre correctement avec `dotnet Bet2InvestPoster.dll` (ou binaire self-contained)

## Tasks / Subtasks

- [x] Task 1 : Créer le fichier systemd unit `deploy/bet2invest-poster.service` (AC: #1, #2, #3, #4)
  - [x] 1.1 Créer le répertoire `deploy/` à la racine du repository
  - [x] 1.2 Créer `deploy/bet2invest-poster.service` avec les sections `[Unit]`, `[Service]`, `[Install]`
  - [x] 1.3 Configurer `WorkingDirectory=/opt/bet2invest-poster`
  - [x] 1.4 Configurer `ExecStart=dotnet /opt/bet2invest-poster/Bet2InvestPoster.dll`
  - [x] 1.5 Configurer `Restart=on-failure` et `RestartSec=5` (NFR1 : redémarrage < 30s)
  - [x] 1.6 Configurer `EnvironmentFile=/etc/bet2invest-poster/env` pour les credentials (NFR6)
  - [x] 1.7 Ajouter `Environment=DOTNET_ENVIRONMENT=Production` et `Environment=ASPNETCORE_ENVIRONMENT=Production`
  - [x] 1.8 Configurer `After=network-online.target` et `Wants=network-online.target`
  - [x] 1.9 Configurer `WantedBy=multi-user.target` dans `[Install]`
  - [x] 1.10 Configurer `SyslogIdentifier=bet2invest-poster` pour les logs journald

- [x] Task 2 : Créer le workflow GitHub Actions CI `.github/workflows/ci.yml` (AC: #5)
  - [x] 2.1 Créer le répertoire `.github/workflows/`
  - [x] 2.2 Créer `.github/workflows/ci.yml` avec trigger sur `push` et `pull_request` vers `main`
  - [x] 2.3 Configurer le step `actions/checkout@v4` avec `submodules: recursive` (CRITIQUE — le submodule jtdev-bet2invest-scraper doit être récupéré)
  - [x] 2.4 Configurer `actions/setup-dotnet@v4` avec `dotnet-version: '9.0.x'`
  - [x] 2.5 Ajouter les steps : `dotnet restore Bet2InvestPoster.sln` → `dotnet build --no-restore -c Release` → `dotnet test --no-build -c Release`

- [x] Task 3 : Valider le build et les tests (AC: #5, #6)
  - [x] 3.1 `dotnet build Bet2InvestPoster.sln` réussit sans erreur ni avertissement
  - [x] 3.2 `dotnet test tests/Bet2InvestPoster.Tests` — tous les tests passent (11/11 minimum, 0 régression)
  - [x] 3.3 Vérifier la syntaxe du fichier systemd : `systemd-analyze verify deploy/bet2invest-poster.service` (optionnel si systemd disponible)

## Dev Notes

### Contexte de la Story

Cette story est la **dernière de l'Épique 1** (Fondation du Projet). Elle ne modifie aucun code C# existant — elle crée uniquement des fichiers d'infrastructure (systemd unit, CI GitHub Actions). Le code est déjà prêt pour le déploiement systemd depuis la story 1.2.

### État du Code Après Story 1.2 (IMPORTANT)

`Program.cs` a **déjà** `builder.Services.AddSystemd()` configuré (ligne 10). Ne pas toucher à `Program.cs` — tout est en place :
- Intégration systemd via `AddSystemd()` (notifie systemd du statut de démarrage/arrêt)
- Serilog avec console + file sinks, `LogPath` depuis `PosterOptions`
- Fast-fail validation des credentials manquantes au démarrage
- Hiérarchie configuration : env vars > appsettings.json (Generic Host standard)

### Fichier systemd Unit — Spécification Complète

Fichier à créer : `deploy/bet2invest-poster.service`

```ini
[Unit]
Description=Bet2Invest Poster - Service de publication automatique de pronostics
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
# AddSystemd() utilise Type=notify pour informer systemd du démarrage réussi
WorkingDirectory=/opt/bet2invest-poster
ExecStart=dotnet /opt/bet2invest-poster/Bet2InvestPoster.dll

# Redémarrage automatique (NFR1 : délai < 30s)
Restart=on-failure
RestartSec=5

# Credentials via EnvironmentFile (NFR6 — jamais dans appsettings.json)
EnvironmentFile=/etc/bet2invest-poster/env

# Variables d'environnement non-sensibles
Environment=DOTNET_ENVIRONMENT=Production
Environment=ASPNETCORE_ENVIRONMENT=Production

# Identification pour journald
SyslogIdentifier=bet2invest-poster

# Sécurité de base
PrivateTmp=yes
NoNewPrivileges=yes

[Install]
WantedBy=multi-user.target
```

**Pourquoi `Type=notify` :** `AddSystemd()` implémente le protocole sd_notify — il informe systemd quand le service est prêt. Si `Type=simple` (défaut), systemd peut considérer le service démarré avant qu'il soit réellement prêt.

### Format du Fichier EnvironmentFile (`/etc/bet2invest-poster/env`)

Ce fichier N'EST PAS créé par cette story (c'est une configuration VPS manuelle), mais il faut le documenter dans un commentaire dans le unit file ou une note. Format attendu :

```
Bet2Invest__Identifier=user@example.com
Bet2Invest__Password=your_password
Telegram__BotToken=123456:ABC-DEF...
Telegram__AuthorizedChatId=123456789
```

- Séparateur de section .NET : `__` (double underscore)
- Le fichier doit être protégé : `chmod 600 /etc/bet2invest-poster/env`

### GitHub Actions CI — Points Critiques

**CRITIQUE : `submodules: recursive`** est obligatoire car `jtdev-bet2invest-scraper` est un submodule git. Sans cette option, `dotnet build` échouerait avec des erreurs de fichiers manquants.

```yaml
- name: Checkout
  uses: actions/checkout@v4
  with:
    submodules: recursive  # OBLIGATOIRE pour jtdev-bet2invest-scraper
```

Commandes de build à utiliser (en ligne avec les standards projet) :
```bash
dotnet restore Bet2InvestPoster.sln
dotnet build Bet2InvestPoster.sln --no-restore -c Release
dotnet test tests/Bet2InvestPoster.Tests --no-build -c Release
```

### Procédure de Déploiement Manuel (Non Testé Automatiquement)

Pour référence (ne fait pas partie du scope de cette story côté implémentation) :

```bash
# Sur le poste dev — publier l'application
dotnet publish src/Bet2InvestPoster -c Release -o ./publish

# Sur le VPS — préparer le répertoire
sudo mkdir -p /opt/bet2invest-poster
sudo mkdir -p /etc/bet2invest-poster

# Copier les fichiers publiés (SCP depuis dev)
# scp -r ./publish/* user@vps:/opt/bet2invest-poster/

# Créer le fichier d'env (credentials)
# sudo nano /etc/bet2invest-poster/env
# sudo chmod 600 /etc/bet2invest-poster/env

# Installer le service systemd
sudo cp deploy/bet2invest-poster.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable bet2invest-poster
sudo systemctl start bet2invest-poster
sudo systemctl status bet2invest-poster
```

### Tests — Aucun Nouveau Test Unitaire Requis

Cette story crée uniquement des fichiers d'infrastructure (`.service`, `.yml`). Aucun code C# n'est modifié. Les 11 tests existants doivent continuer à passer sans modification.

**Validation de la story :**
- `dotnet build Bet2InvestPoster.sln` → Build succeeded
- `dotnet test tests/Bet2InvestPoster.Tests` → 11/11 tests passent

### Project Structure Notes

Fichiers à créer dans cette story :

```
Bet2InvestPoster.sln (root)
├── .github/
│   └── workflows/
│       └── ci.yml                    ← NOUVEAU
└── deploy/
    └── bet2invest-poster.service     ← NOUVEAU
```

Aucun fichier dans `src/` ou `tests/` ne doit être modifié.

### Conflits Potentiels / Points d'Attention

1. **`Type=notify` vs `Type=simple`** : Avec `AddSystemd()`, le type `notify` est correct. Si l'agent utilise `Type=simple`, le service fonctionnera mais systemd ne sera pas informé du statut de démarrage réel.

2. **`ExecStart` avec dotnet** : Si le runtime .NET 9 est installé sur le VPS via packages système, le chemin sera `/usr/bin/dotnet`. Alternativement, pour un déploiement self-contained, l'ExecStart serait directement `/opt/bet2invest-poster/Bet2InvestPoster` (sans `dotnet`).

3. **LogPath relatif** : `PosterOptions.LogPath = "logs"` (défaut) + `WorkingDirectory=/opt/bet2invest-poster` → logs dans `/opt/bet2invest-poster/logs/`. Correct selon AC #4. Ne pas modifier la valeur par défaut.

4. **GitHub Actions runner** : Utiliser `ubuntu-latest` (standard, gratuit sur GitHub). Le submodule `jtdev-bet2invest-scraper` n'a pas d'authentification requise (repository public).

### Références

- [Source: .bmadOutput/planning-artifacts/architecture.md#Deployment-Structure] — `/opt/bet2invest-poster/`, `dotnet publish`
- [Source: .bmadOutput/planning-artifacts/architecture.md#Infrastructure-Deployment] — systemd, GitHub Actions
- [Source: .bmadOutput/planning-artifacts/architecture.md#Project-Structure] — `deploy/bet2invest-poster.service`, `.github/workflows/ci.yml`
- [Source: .bmadOutput/planning-artifacts/epics.md#Story-1.3] — AC originaux
- [Source: .bmadOutput/implementation-artifacts/1-2-configuration-et-injection-de-dependances.md#Dev-Notes] — `AddSystemd()` via `builder.Services`, LogPath pattern
- [Source: NFR1] — Redémarrage < 30s → `RestartSec=5`
- [Source: NFR6] — Credentials exclusivement en env vars production → `EnvironmentFile`

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- dotnet build Bet2InvestPoster.sln : Build succeeded, 0 Warning, 0 Error (2026-02-23)
- dotnet test tests/Bet2InvestPoster.Tests : Failed=0, Passed=11, Total=11 (2026-02-23)
- [code-review] dotnet build après corrections : Build succeeded, 0 Warning, 0 Error (2026-02-23)
- [code-review] dotnet test après corrections : Failed=0, Passed=11, Total=11 (2026-02-23)

### Completion Notes List

- `deploy/bet2invest-poster.service` créé — Type=notify (compatible AddSystemd()), RestartSec=5 (NFR1), EnvironmentFile=/etc/bet2invest-poster/env (NFR6), PrivateTmp+NoNewPrivileges (sécurité de base), SyslogIdentifier pour journald (2026-02-23)
- `.github/workflows/ci.yml` créé — trigger push+PR sur main, submodules: recursive (CRITIQUE pour jtdev-bet2invest-scraper), setup .NET 9, restore → build → test (2026-02-23)
- Aucun fichier C# modifié — story 100% infrastructure (2026-02-23)
- 11/11 tests passent, 0 régression (2026-02-23)
- [code-review] 8 issues corrigés (2H+3M+3L) — User=bet2invest, ExecStart absolu, Restart=always, hardening systemd, cache NuGet CI, artefacts test CI, commentaire EnvironmentFile (2026-02-23)

### File List

- `deploy/bet2invest-poster.service` — NOUVEAU (unit systemd pour VPS, + corrections review)
- `.github/workflows/ci.yml` — NOUVEAU (GitHub Actions CI, + cache NuGet + artefacts test)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` — MODIFIÉ (sync statuts 1.2 → done, 1.3 → review)

### Senior Developer Review (AI)

**Reviewer :** claude-opus-4-6 (adversarial mode)
**Date :** 2026-02-23
**Outcome :** Changes Requested → Fixed

**Résumé :** 8 issues identifiés (2 High, 3 Medium, 3 Low) — tous corrigés automatiquement

**Corrections appliquées :**
1. **[HIGH] Aucun `User=` — service en root** — Ajout `User=bet2invest` + `Group=bet2invest`
2. **[HIGH] `ExecStart=dotnet` non-absolu** — Changé en `/usr/bin/dotnet` (spec systemd)
3. **[MEDIUM] Pas de cache NuGet CI** — Ajout `cache: true` dans setup-dotnet
4. **[MEDIUM] sprint-status.yaml absent du File List** — Ajouté à la File List
5. **[MEDIUM] Pas d'artefacts de test CI** — Ajout logger TRX + upload-artifact
6. **[LOW] EnvironmentFile sans `-` non documenté** — Commentaire intentionnel ajouté
7. **[LOW] `Restart=on-failure` → `Restart=always`** — Garantit le redémarrage même après exit propre
8. **[LOW] Hardening incomplet** — Ajout `ProtectSystem=strict`, `ProtectHome=yes`, `ReadWritePaths=`

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-23 | claude-sonnet-4-6 | Création deploy/bet2invest-poster.service et .github/workflows/ci.yml — story → review |
| 2026-02-23 | claude-opus-4-6 (code-review) | Review adversariale — 8 issues corrigés (2H+3M+3L), 11 tests total, 0 régression |
