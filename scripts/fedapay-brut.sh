#!/usr/bin/env bash
# Rend l'objet transaction ENTIER, tel que FedaPay le donne.
#
# POURQUOI IL EXISTE. Le lecteur de fedapay-transaction.sh choisit les champs
# qu'il montre — et un champ qu'il ne connait pas est un champ qu'on ne verra
# jamais. Quand « status » vaut « declined » et que « last_error_code » est
# vide, le motif est ailleurs dans l'objet, dans un champ que personne n'a
# encore nomme. Celui-ci ne choisit rien.
#
# LA CLE NE PASSE JAMAIS EN ARGUMENT.
set -euo pipefail

cd "$(dirname "$0")/.."

ENV_FILE="src/Services/Payment/.env"
ID="${1:-}"

[ -n "$ID" ] || { echo "Usage : fedapay-brut.sh <identifiant NUMERIQUE de transaction>" >&2; exit 1; }
[ -f "$ENV_FILE" ] || { echo "Pas de $ENV_FILE." >&2; exit 1; }

lire() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -1; }

CLE=$(lire FEDAPAY_SECRET_KEY)
MILIEU=$(lire FEDAPAY_ENVIRONMENT)
: "${MILIEU:=sandbox}"

[ -n "$CLE" ] || { echo "FEDAPAY_SECRET_KEY absent." >&2; exit 1; }

if [ "$MILIEU" = "live" ]; then BASE="https://api.fedapay.com/v1"
else BASE="https://sandbox-api.fedapay.com/v1"; fi

TRAVAIL=$(mktemp -d)
trap 'rm -rf "$TRAVAIL"' EXIT
CONF="$TRAVAIL/curl.conf"
umask 077
printf 'header = "Authorization: Bearer %s"\n' "$CLE" > "$CONF"

sortie=$(curl -sS -K "$CONF" --max-time 20 -w '\n%{http_code}' "$BASE/transactions/$ID")
code=$(printf '%s' "$sortie" | tail -1)
corps=$(printf '%s' "$sortie" | sed '$d')

echo "HTTP $code"
printf '%s' "$corps" | python3 -m json.tool 2>/dev/null || printf '%s\n' "$corps"
