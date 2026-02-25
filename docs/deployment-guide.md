# bet2invest-poster — Guide de déploiement

**Généré le :** 2026-02-25

## Infrastructure cible

- **OS** : Linux (VPS)
- **Runtime** : .NET 9 (`dotnet`)
- **Supervision** : systemd (sd_notify)
- **Utilisateur** : `bet2invest:bet2invest`
- **Répertoire** : `/opt/bet2invest-poster`
- **Credentials** : `/etc/bet2invest-poster/env`

## Publication

```bash
# Build en Release
dotnet publish src/Bet2InvestPoster -c Release -o publish/

# Copier sur le VPS
scp -r publish/* user@vps:/opt/bet2invest-poster/
```

## Configuration systemd

Le fichier `deploy/bet2invest-poster.service` est pré-configuré :

```ini
[Unit]
Description=bet2invest-poster
After=network-online.target

[Service]
Type=notify
User=bet2invest
Group=bet2invest
WorkingDirectory=/opt/bet2invest-poster
ExecStart=/usr/local/bin/dotnet /opt/bet2invest-poster/Bet2InvestPoster.dll
Restart=always
RestartSec=5

# Credentials
EnvironmentFile=/etc/bet2invest-poster/env

# Security hardening
PrivateTmp=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/opt/bet2invest-poster

[Install]
WantedBy=multi-user.target
```

## Installation sur le VPS

```bash
# 1. Créer l'utilisateur
sudo useradd -r -s /usr/sbin/nologin bet2invest

# 2. Créer les répertoires
sudo mkdir -p /opt/bet2invest-poster /etc/bet2invest-poster

# 3. Configurer les credentials
sudo tee /etc/bet2invest-poster/env <<EOF
Bet2Invest__Identifier=xxx
Bet2Invest__Password=xxx
Telegram__BotToken=xxx
Telegram__AuthorizedChatId=xxx
Poster__BankrollId=xxx
EOF
sudo chmod 600 /etc/bet2invest-poster/env

# 4. Copier le service systemd
sudo cp deploy/bet2invest-poster.service /etc/systemd/system/
sudo systemctl daemon-reload

# 5. Activer et démarrer
sudo systemctl enable bet2invest-poster
sudo systemctl start bet2invest-poster

# 6. Vérifier
sudo systemctl status bet2invest-poster
journalctl -u bet2invest-poster -f
```

## Health Check

Le service expose un endpoint HTTP sur le port configurable (`HealthCheckPort`, défaut 8080) :

```bash
curl http://localhost:8080/healthz
```

## Logs

- **Console** : format human-readable (stdout → journalctl)
- **Fichier** : JSON structuré dans `/opt/bet2invest-poster/logs/`
- **Rotation** : quotidienne
- **Rétention** : configurable (`LogRetentionDays`, défaut 30)

## Mise à jour

```bash
# 1. Build et publier
dotnet publish src/Bet2InvestPoster -c Release -o publish/

# 2. Copier sur VPS
scp -r publish/* user@vps:/opt/bet2invest-poster/

# 3. Redémarrer
sudo systemctl restart bet2invest-poster
```
