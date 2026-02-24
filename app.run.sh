#!/usr/bin/env bash
# Lance l'application bet2invest-poster avec les variables d'environnement du .env
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Charger le .env en gérant les caractères spéciaux (#, $, etc.)
while IFS='=' read -r key value; do
  [[ -z "$key" || "$key" =~ ^# ]] && continue
  export "$key=$value"
done < "$SCRIPT_DIR/.env"

exec dotnet run --project "$SCRIPT_DIR/src/Bet2InvestPoster"
