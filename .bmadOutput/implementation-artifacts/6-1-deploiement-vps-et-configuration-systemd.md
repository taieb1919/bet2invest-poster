# Story 6.1 : Déploiement VPS et Configuration Systemd

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a l'utilisateur,
I want déployer le service bet2invest-poster sur mon VPS avec un service systemd opérationnel,
so that le service tourne en continu et redémarre automatiquement en cas de crash.

## Acceptance Criteria

1. **Given** le code compilé avec `dotnet publish -c Release`
   **When** les binaires sont copiés dans `/opt/bet2invest-poster/` sur le VPS
   **Then** le service démarre via `systemctl start bet2invest-poster` sans erreur

2. **Given** le fichier `bet2invest-poster.service` installé dans `/etc/systemd/system/`
   **When** `systemctl enable bet2invest-poster` est exécuté
   **Then** le service redémarre automatiquement au boot et après un crash (NFR1)

3. **Given** les variables d'environnement configurées dans le service systemd (`Bet2Invest__Identifier`, `Bet2Invest__Password`, `Telegram__BotToken`, `Telegram__AuthorizedChatId`, `Poster__BankrollId`)
   **When** le service démarre
   **Then** les credentials sont chargés depuis les env vars — jamais depuis `appsettings.json` en production (NFR5, NFR6)
   **And** `systemctl status bet2invest-poster` affiche `active (running)`

4. **Given** le service en cours d'exécution
   **When** on consulte les logs via `journalctl -u bet2invest-poster -f`
   **Then** les logs Serilog apparaissent avec timestamp, Step et contexte structuré (NFR12)
   **And** aucun credential n'apparaît dans les logs (NFR5)

5. **Given** le service actif
   **When** on tue le processus manuellement (`kill -9 <pid>`)
   **Then** systemd redémarre le service en moins de 30 secondes (NFR1)

## Tasks / Subtasks

- [x] Task 1 : Compiler et publier le projet (AC: #1)
  - [x] 1.1 Exécuter `dotnet publish src/Bet2InvestPoster -c Release -o ./publish` sur la machine de dev
  - [x] 1.2 Vérifier que `./publish/Bet2InvestPoster.dll` et toutes les dépendances sont présentes
  - [x] 1.3 Vérifier que `appsettings.json` est inclus dans `./publish/` (credentials vides — env vars en prod)

- [x] Task 2 : Préparer le VPS (AC: #1, #2, #3)
  - [x] 2.1 Créer l'utilisateur système : `sudo useradd -r -s /usr/sbin/nologin bet2invest`
  - [x] 2.2 Créer le répertoire d'installation : `sudo mkdir -p /opt/bet2invest-poster/logs`
  - [x] 2.3 Installer le runtime .NET 9 si absent : `dotnet --info` → .NET SDK 9.0.310 déjà installé
  - [x] 2.4 Copier les binaires publiés dans `/opt/bet2invest-poster/`
  - [x] 2.5 Copier `tipsters.json` dans `/opt/bet2invest-poster/` (inclus dans publish automatiquement)
  - [x] 2.6 Fixer les permissions : `sudo chown -R bet2invest:bet2invest /opt/bet2invest-poster`

- [x] Task 3 : Configurer les credentials (AC: #3)
  - [x] 3.1 Créer le répertoire : `sudo mkdir -p /etc/bet2invest-poster`
  - [x] 3.2 Copier `.env` vers `/etc/bet2invest-poster/env` (5 variables configurées)
  - [x] 3.3 Protéger le fichier : `chmod 600`, `chown root:bet2invest`

- [x] Task 4 : Installer et activer le service systemd (AC: #1, #2)
  - [x] 4.1 Copier le unit file dans `/etc/systemd/system/`
  - [x] 4.2 Recharger systemd : `sudo systemctl daemon-reload`
  - [x] 4.3 Activer au boot : `sudo systemctl enable bet2invest-poster`
  - [x] 4.4 Démarrer : `sudo systemctl start bet2invest-poster`
  - [x] 4.5 Vérifier : `systemctl status bet2invest-poster` → `active (running)` ✅

- [x] Task 5 : Valider les logs et la sécurité (AC: #4)
  - [x] 5.1 Consulter les logs : `journalctl -u bet2invest-poster`
  - [x] 5.2 Format structuré vérifié : timestamp, `[Step]`, message ✅
  - [x] 5.3 Aucun credential dans les logs (`NO_CREDENTIALS_FOUND`) ✅
  - [x] 5.4 Logs fichier dans `/opt/bet2invest-poster/logs/bet2invest-poster-20260224.log` ✅

- [x] Task 6 : Valider le redémarrage automatique (AC: #5)
  - [x] 6.1 PID identifié : 3538539
  - [x] 6.2 Processus tué avec `kill -9`
  - [x] 6.3 Service redémarré en ~5s (PID 3539546) — `active (running)` ✅ (NFR1 : < 30s)
  - [x] 6.4 Logs confirment le redémarrage

## Dev Notes

### Nature de cette story — Validation manuelle

**IMPORTANT** : Cette story est une procédure de **déploiement et validation manuelle**, pas une story d'implémentation de code. Tout le code est déjà implémenté (épiques 1-5 terminées). L'objectif est de vérifier le bon fonctionnement en conditions réelles sur le VPS.

### Fichiers existants — Tout est déjà prêt

| Fichier | Rôle | Status |
|---|---|---|
| `deploy/bet2invest-poster.service` | Unit file systemd complet | Existant |
| `src/Bet2InvestPoster/Program.cs:16` | `builder.Services.AddSystemd()` — intégration sd_notify | Existant |
| `src/Bet2InvestPoster/appsettings.json` | Config par défaut (credentials vides) | Existant |
| `src/Bet2InvestPoster/Bet2InvestPoster.csproj` | Ref `Microsoft.Extensions.Hosting.Systemd 9.0.8` | Existant |

### Configuration du service systemd

Le fichier `deploy/bet2invest-poster.service` est déjà configuré avec :
- `Type=notify` : intégration `AddSystemd()` (sd_notify protocol)
- `User=bet2invest` / `Group=bet2invest` : utilisateur système dédié
- `WorkingDirectory=/opt/bet2invest-poster`
- `Restart=always` / `RestartSec=5` : redémarrage auto < 30s (NFR1)
- `EnvironmentFile=/etc/bet2invest-poster/env` : credentials via env vars (NFR6)
- Hardening : `PrivateTmp=yes`, `NoNewPrivileges=yes`, `ProtectSystem=strict`, `ReadWritePaths=/opt/bet2invest-poster`

### Credentials — EnvironmentFile sans préfixe '-'

Le `EnvironmentFile=/etc/bet2invest-poster/env` est **intentionnellement sans préfixe `-`**. Si le fichier est absent, systemd refuse de démarrer le service. Ceci est voulu : les credentials sont obligatoires et `Program.cs` fait aussi une validation fast-fail (lignes 116-127).

### Variables d'environnement requises

| Variable | Description | Exemple |
|---|---|---|
| `Bet2Invest__Identifier` | Email du compte bet2invest | `user@example.com` |
| `Bet2Invest__Password` | Mot de passe | `secret` |
| `Telegram__BotToken` | Token du bot Telegram | `123456:ABC-DEF...` |
| `Telegram__AuthorizedChatId` | Chat ID Telegram autorisé | `123456789` |
| `Poster__BankrollId` | ID de la bankroll sur bet2invest | `abc-123` |

### Runtime .NET 9 sur le VPS

Le VPS doit avoir le runtime .NET 9 installé. Vérifier avec `dotnet --info`. Si absent :
```bash
# Ubuntu/Debian
sudo apt-get update && sudo apt-get install -y dotnet-runtime-9.0
```

Alternative : publier en self-contained (`dotnet publish -c Release --self-contained -r linux-x64`) pour ne pas dépendre du runtime installé. Cela augmente la taille du déploiement (~80 Mo) mais simplifie la gestion.

### Commande de publication

```bash
# Framework-dependent (nécessite runtime .NET 9 sur le VPS)
dotnet publish src/Bet2InvestPoster -c Release -o ./publish

# Self-contained (pas besoin de runtime sur le VPS)
dotnet publish src/Bet2InvestPoster -c Release --self-contained -r linux-x64 -o ./publish
```

### Transfert vers le VPS

```bash
# Copier les binaires
scp -r ./publish/* user@vps:/opt/bet2invest-poster/

# Copier tipsters.json (à la racine du projet)
scp src/Bet2InvestPoster/tipsters.json user@vps:/opt/bet2invest-poster/

# Copier le unit file
scp deploy/bet2invest-poster.service user@vps:/tmp/
ssh user@vps 'sudo cp /tmp/bet2invest-poster.service /etc/systemd/system/'
```

### Vérifications post-déploiement

```bash
# 1. Service actif
systemctl status bet2invest-poster

# 2. Logs structurés
journalctl -u bet2invest-poster -n 20

# 3. Logs fichier
ls -la /opt/bet2invest-poster/logs/

# 4. Pas de credentials dans les logs
journalctl -u bet2invest-poster | grep -i -E "(password|token|identifier)"
# → doit retourner 0 résultat

# 5. Redémarrage auto
PID=$(systemctl show bet2invest-poster -p MainPID --value)
sudo kill -9 $PID
sleep 10
systemctl status bet2invest-poster  # → active (running)
```

### Rollback en cas de problème

```bash
# Arrêter le service
sudo systemctl stop bet2invest-poster

# Consulter les logs d'erreur
journalctl -u bet2invest-poster -n 50 --no-pager

# Vérifier les permissions
ls -la /opt/bet2invest-poster/
ls -la /etc/bet2invest-poster/env

# Vérifier les env vars (sans afficher les valeurs)
sudo systemd-run --property=EnvironmentFile=/etc/bet2invest-poster/env env | grep -c "Bet2Invest\|Telegram\|Poster"
# → doit retourner 5
```

### Intelligence de la story précédente (Story 5.2)

- 132 tests verts (baseline post-code-review épique 5)
- `ResiliencePipelineService` en Singleton — `ResiliencePipeline` thread-safe
- Scope DI créé par tentative Polly dans `SchedulerWorker`
- Le commit le plus récent (`ac717ed`) adapte les IDs tipsters de int vers string (slug URL) — vérifier que `tipsters.json` sur le VPS utilise le format slug

### Attention — tipsters.json format

Le dernier commit (`ac717ed`) a changé le format des IDs tipsters de `int` vers `string` (slug URL). S'assurer que le fichier `tipsters.json` déployé utilise le bon format :
```json
[
  { "slug": "tipster-name", "name": "Tipster Name" }
]
```

### Project Structure Notes

- Aucune modification de code requise pour cette story
- Le déploiement est manuel (architecture: `dotnet publish` + SCP/SSH)
- Structure cible sur le VPS : `/opt/bet2invest-poster/` avec sous-dossier `logs/`

### References

- [Source: .bmadOutput/planning-artifacts/epics.md#Story-6.1] — AC originaux
- [Source: .bmadOutput/planning-artifacts/architecture.md#Infrastructure-Deployment] — systemd, /opt/bet2invest-poster/
- [Source: .bmadOutput/planning-artifacts/architecture.md#Deployment-Structure] — dotnet publish, fichiers déployés
- [Source: deploy/bet2invest-poster.service] — Unit file systemd complet
- [Source: src/Bet2InvestPoster/Program.cs:16] — AddSystemd() intégration
- [Source: src/Bet2InvestPoster/Program.cs:116-127] — Fast-fail validation credentials
- [Source: src/Bet2InvestPoster/appsettings.json] — Config par défaut (credentials vides)
- [Source: .bmadOutput/implementation-artifacts/5-2-resilience-polly-retry-du-cycle-complet.md] — 132 tests, patterns établis

## Dev Agent Record

### Agent Model Used

claude-opus-4-6

### Debug Log References

- `NETSDK1152` : Conflit `appsettings.json` entre le submodule scraper et le projet principal lors du `dotnet publish`. Le submodule a `CopyToOutputDirectory=PreserveNewest` sur son `appsettings.json`. Résolu avec `<ErrorOnDuplicatePublishOutputFiles>false</ErrorOnDuplicatePublishOutputFiles>` dans le csproj principal — notre `appsettings.json` prend la priorité.
- `203/EXEC` : Le service systemd échouait car `ExecStart=/usr/bin/dotnet` n'existait pas. Sur ce VPS, dotnet est à `/usr/local/bin/dotnet`. Corrigé dans le unit file.
- Warning Telegram "Conflict: terminated by other getUpdates request" — un autre bot avec le même token tourne (instance dev). Normal en phase de validation.
- 142/142 tests verts après modification du csproj — aucune régression.

### Completion Notes List

- AC#1 : `dotnet publish` réussi, binaires copiés dans `/opt/bet2invest-poster/`, service démarre via `systemctl start` sans erreur.
- AC#2 : Service `enabled` au boot, `Restart=always` + `RestartSec=5` configuré.
- AC#3 : Credentials chargés depuis `/etc/bet2invest-poster/env` (chmod 600, chown root:bet2invest). `systemctl status` → `active (running)`.
- AC#4 : Logs Serilog structurés avec timestamp, `[Step]`, message. Aucun credential dans les logs. Logs fichier dans `/opt/bet2invest-poster/logs/`.
- AC#5 : Processus tué avec `kill -9`, redémarré en ~5s (< 30s NFR1). Nouveau PID confirmé.
- Fix csproj : `ErrorOnDuplicatePublishOutputFiles=false` pour conflit appsettings.json submodule.
- Fix service : `ExecStart` corrigé vers `/usr/local/bin/dotnet`.

### File List

**Modifiés :**
- `src/Bet2InvestPoster/Bet2InvestPoster.csproj` (ajout `ErrorOnDuplicatePublishOutputFiles=false`)
- `deploy/bet2invest-poster.service` (fix ExecStart → `/usr/local/bin/dotnet`, ajout `Poster__BankrollId` dans commentaires, suppression `ASPNETCORE_ENVIRONMENT`)
- `src/Bet2InvestPoster/appsettings.json` (ajout placeholders `Password` et `BotToken` pour cohérence)
- `.gitignore` (ajout `publish/`)
- `.bmadOutput/implementation-artifacts/6-1-deploiement-vps-et-configuration-systemd.md` (ce fichier)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (statut → review)

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-24 | claude-opus-4-6 (dev-story) | Déploiement complet sur VPS — 6/6 tasks, 5/5 ACs validés. Fix NETSDK1152 + ExecStart path. Service active (running). |
| 2026-02-24 | claude-opus-4-6 (code-review) | Review adversariale : 2 HIGH, 3 MEDIUM, 2 LOW. Fixes appliqués : H2 (Poster__BankrollId dans commentaires service), M1/M2 (placeholders Password/BotToken dans appsettings.json), M3 (sprint-status à commiter), L3 (suppression ASPNETCORE_ENVIRONMENT). H1 (ExecStart non commité) résolu par inclusion dans prochain commit. |
