#!/usr/bin/env bash
# Demande a FedaPay ce qu'il pense de NOTRE corps de requete, et de la
# variante documentee.
#
# POURQUOI CE SCRIPT EXISTE. FedaPayClient envoie un objet « customer » qui ne
# porte QUE le numero de telephone. Tous les exemples de la documentation du
# fournisseur portent en plus firstname, lastname et email — et la version
# anglaise precise que l'email doit etre unique, ce qui laisse entendre que
# c'est LUI qui identifie le client chez eux. Savoir si notre corps est refuse
# pour cette raison ne se devine pas : on le demande.
#
# LA CLE NE PASSE JAMAIS EN ARGUMENT. « ps » montre la ligne de commande de
# tous les processus, y compris a un autre utilisateur de la machine. Elle est
# ecrite dans un fichier de configuration curl, en 600, efface a la sortie.
#
# CE SCRIPT CREE DE VRAIES TRANSACTIONS DANS LE BAC A SABLE. Aucune n'est
# payee, aucune n'est reclamee : elles expirent seules. Il refuse de tourner
# sur un compte « live ».
set -euo pipefail

cd "$(dirname "$0")/.."

ENV_FILE="src/Services/Payment/.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "Pas de $ENV_FILE : rien a essayer." >&2
  exit 1
fi

lire() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -1; }

CLE=$(lire FEDAPAY_SECRET_KEY)
MILIEU=$(lire FEDAPAY_ENVIRONMENT)
PAYS=$(lire FEDAPAY_CUSTOMER_COUNTRY)
: "${PAYS:=bj}"
: "${MILIEU:=sandbox}"

if [ -z "$CLE" ]; then
  echo "FEDAPAY_SECRET_KEY absent de $ENV_FILE." >&2
  exit 1
fi

if [ "$MILIEU" = "live" ]; then
  echo "FEDAPAY_ENVIRONMENT=live : ce script ne touche pas a un compte reel." >&2
  exit 1
fi

BASE="https://sandbox-api.fedapay.com/v1"

# Le prefixe de la cle dit a quel compte elle appartient : « sk_live_ » avec
# Environment=sandbox est une panne courante, et elle se voit la. On coupe aux
# DEUX PREMIERS CHAMPS — « sk_sandbox » — et la partie aleatoire ne sort pas.
echo "Cle         : $(printf '%s' "$CLE" | cut -d_ -f1-2)_… (${#CLE} caracteres)"
echo "API         : $BASE"
echo

TRAVAIL=$(mktemp -d)
trap 'rm -rf "$TRAVAIL"' EXIT
CONF="$TRAVAIL/curl.conf"
umask 077
printf 'header = "Authorization: Bearer %s"\nheader = "Content-Type: application/json"\n' "$CLE" > "$CONF"

essai() {
  local titre="$1" corps="$2"

  echo "── $titre"
  echo "$corps" | python3 -m json.tool

  local sortie code
  sortie=$(curl -sS -K "$CONF" -X POST "$BASE/transactions" \
    --data "$corps" -w '\n%{http_code}' --max-time 20) || {
      echo "  curl a echoue (reseau ?)." >&2
      echo
      return 0
    }

  code=$(printf '%s' "$sortie" | tail -1)
  echo "  HTTP $code"
  printf '%s' "$sortie" | sed '$d' | python3 -m json.tool 2>/dev/null \
    || printf '%s' "$sortie" | sed '$d'
  echo
}

# 1. Ce que le code envoie AUJOURD'HUI.
essai "Notre corps actuel : customer avec le seul telephone" "$(cat <<JSON
{
  "description": "Essai HBA",
  "amount": 1000,
  "currency": { "iso": "XOF" },
  "custom_metadata": { "essai": "1" },
  "customer": { "phone_number": { "number": "+22997000000", "country": "$PAYS" } }
}
JSON
)"

# 2. Le corps de la documentation.
essai "Corps documente : firstname, lastname, email, telephone" "$(cat <<JSON
{
  "description": "Essai HBA",
  "amount": 1000,
  "currency": { "iso": "XOF" },
  "custom_metadata": { "essai": "2" },
  "customer": {
    "firstname": "Essai",
    "lastname": "HBA",
    "email": "essai-$(date +%s)@hba.test",
    "phone_number": { "number": "+22997000000", "country": "$PAYS" }
  }
}
JSON
)"

# 3. Sans customer du tout : la documentation le donne pour facultatif.
essai "Sans customer" "$(cat <<JSON
{
  "description": "Essai HBA",
  "amount": 1000,
  "currency": { "iso": "XOF" },
  "custom_metadata": { "essai": "3" }
}
JSON
)"

echo "─────────────────────────────────────────────────────────────────"
echo "Ce qu'il faut regarder :"
echo "  - un 401 partout       : la cle n'est pas celle du bac a sable"
echo "  - 1 refuse, 2 accepte  : c'est l'email qui manque a notre corps"
echo "  - tout accepte         : la creation n'est pas en cause, le probleme"
echo "                           est apres — jeton, retour, ou webhook"
echo "  - « payment_url » dans la reponse de creation : le repli sur /token"
echo "    ne sert pas. Verifie le 29/09/2026 : il y est."
