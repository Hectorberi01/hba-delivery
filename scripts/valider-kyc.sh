#!/usr/bin/env bash
#
# VALIDE LE DOSSIER D'UN LIVREUR, comme le ferait ops depuis la console.
#
# Tant que le dossier n'est pas valide, le livreur ne peut pas passer en
# ligne : la regle est dans le referentiel, le service Driver la fait
# respecter, et l'application grise le bouton. Ce script evite d'ouvrir la
# console pour la seule chose qu'on refait vingt fois en developpement.
#
#   ./scripts/valider-kyc.sh +33618710738
#   ./scripts/valider-kyc.sh Hector
#   ./scripts/valider-kyc.sh            # liste les livreurs et s'arrete
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"
RECHERCHE="${1:-}"

bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
echouer() { rouge "ECHEC : $*"; exit 1; }

command -v jq >/dev/null || echouer "jq est requis (brew install jq)."

REPONSE="$(curl -sS -X POST "$GATEWAY/api/web/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"kyc\"}")"
JETON="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
[ -n "$JETON" ] || echouer "Identity refuse $ADMIN_EMAIL — $REPONSE"

LISTE="$(curl -sS "$GATEWAY/api/admin/v1/drivers?pageSize=200" -H "Authorization: Bearer $JETON")"
echo "$LISTE" | jq -e '.drivers' >/dev/null 2>&1 || echouer "liste inattendue — $LISTE"

# Les statuts arrivent en entiers : la passerelle serialise les enums
# protobuf en nombres. 1 = en attente, 2 = valide, 3 = rejete, 4 = suspendu.
afficher() {
  echo "$LISTE" | jq -r '.drivers[] |
    "  \(.id)  \(.phone // "?")  \(.displayName // "sans nom")  " +
    (if .verificationStatus == 2 then "VALIDE"
     elif .verificationStatus == 3 then "REJETE"
     elif .verificationStatus == 4 then "SUSPENDU"
     else "EN ATTENTE" end)'
}

if [ -z "$RECHERCHE" ]; then
  bleu "Livreurs connus :"
  afficher
  echo
  bleu "Relancez avec un telephone ou un nom : ./scripts/valider-kyc.sh +33618710738"
  exit 0
fi

CORRESPONDANCES="$(echo "$LISTE" | jq --arg q "$RECHERCHE" \
  '[.drivers[] | select(((.phone // "") | contains($q)) or ((.displayName // "") | ascii_downcase | contains($q | ascii_downcase)))]')"
NOMBRE="$(echo "$CORRESPONDANCES" | jq 'length')"

if [ "$NOMBRE" = "0" ]; then
  rouge "Aucun livreur ne correspond a « $RECHERCHE »."
  rouge ""
  rouge "Livreurs connus :"
  afficher >&2
  rouge ""
  rouge "UN COMPTE SANS PROFIL N'APPARAIT PAS ICI : le profil nait de"
  rouge "l'evenement AccountRegistered. Si l'inscription date de quelques"
  rouge "secondes, attendez ; si elle date de plus longtemps, regardez si"
  rouge "Driver consomme bien le topic identity."
  exit 1
fi

if [ "$NOMBRE" != "1" ]; then
  rouge "« $RECHERCHE » designe $NOMBRE livreurs. Precisez :"
  echo "$CORRESPONDANCES" | jq -r '.[] | "  \(.phone // "?")  \(.displayName // "sans nom")"' >&2
  exit 1
fi

ID="$(echo "$CORRESPONDANCES" | jq -r '.[0].id')"
NOM="$(echo "$CORRESPONDANCES" | jq -r '.[0].displayName // "sans nom"')"
TEL="$(echo "$CORRESPONDANCES" | jq -r '.[0].phone // "?"')"

bleu "Validation du dossier de $NOM ($TEL)…"

REPONSE="$(curl -sS -X POST "$GATEWAY/api/admin/v1/drivers/$ID/kyc" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $JETON" \
  -d '{"approved":true,"reason":"Validation de developpement"}')"

STATUT="$(echo "$REPONSE" | jq -r '.verificationStatus // empty')"
if [ "$STATUT" != "2" ]; then
  echouer "le service n'a pas valide le dossier — $REPONSE"
fi

vert "Dossier valide. Le livreur peut passer en ligne."
bleu "Dans l'application : tirez l'accueil vers le bas pour relire le statut."
