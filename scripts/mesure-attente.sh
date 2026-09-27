#!/usr/bin/env bash
#
# CE QUE COUTE UNE HEURE D'ATTENTE A UN LIVREUR EN LIGNE.
#
# POURQUOI CE SCRIPT EXISTE. Le deuxieme audit de l'application livreur avance
# « environ 1,3 Mo par heure » d'attente. C'etait une ESTIMATION — un ordre de
# grandeur pose a partir du nombre de requetes et d'une taille supposee. Un
# chiffre suppose ne doit pas servir a decider d'allonger un intervalle : on
# mesure d'abord.
#
# CE QU'IL MESURE VRAIMENT : les octets de la requete HTTP et de la reponse,
# en-tetes compris, pour les deux appels que l'application repete quand le
# livreur attend — « GET /offers/current » sans offre, et « POST /position ».
# Le jeton est un vrai JWT de livreur : l'en-tete Authorization pese donc ce
# qu'il pese en production.
#
# CE QU'IL NE MESURE PAS, ET IL FAUT LE DIRE :
#
#   - TLS. La passerelle de developpement parle en clair. En production chaque
#     enregistrement TLS ajoute quelques dizaines d'octets, et l'ouverture
#     d'une connexion coute un aller-retour de poignee de main. Le chiffre
#     rendu ici est donc un MINORANT.
#   - Le GPS. Un point toutes les vingt secondes pendant dix heures coute de la
#     batterie, pas des donnees. Cela se mesure autrement.
#   - Les reprises. Un reseau qui hoquete fait repartir la requete entiere.
#
# UTILISATION :
#
#   ./scripts/mesure-attente.sh
#   TOURS=30 ./scripts/mesure-attente.sh
#   GATEWAY=https://driver.hba.bj ./scripts/mesure-attente.sh   # avec TLS
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
TOURS="${TOURS:-20}"
ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"
HORODATAGE="$(date +%s)"

LAT="6.3654"; LON="2.4183"

vert()  { printf '  \033[1;32m✓\033[0m %s\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }
echouer() { rouge "ECHEC : $*"; exit 1; }

command -v jq >/dev/null || echouer "jq est requis."

TEMPO="$(mktemp -d)"
trap 'rm -rf "$TEMPO"' EXIT

# ---------------------------------------------------------------------------
# Les intervalles sont LUS DANS LE CODE DE L'APPLICATION, pas recopies ici.
#
# UN CHIFFRE RECOPIE EST UN CHIFFRE QUI DIVERGE. Le jour ou le sondage passe a
# quinze secondes, cette mesure doit suivre toute seule — sinon elle continuera
# d'annoncer la consommation d'une version qui n'existe plus.
# ---------------------------------------------------------------------------
ECRAN="apps/driver_app/lib/features/home/home_screen.dart"
[ -f "$ECRAN" ] || echouer "$ECRAN introuvable."

lire_intervalle() {
  grep -E "static const $1 = Duration\(seconds: [0-9]+\)" "$ECRAN" \
    | grep -oE '[0-9]+' | head -1
}

SONDAGE="$(lire_intervalle _pollInterval)"
BATTEMENT="$(lire_intervalle _positionInterval)"
if [ -z "$SONDAGE" ] || [ -z "$BATTEMENT" ]; then
  echouer "intervalles illisibles dans $ECRAN."
fi

etape "1. Intervalles lus dans l'application"
vert "Sondage d'offre   : ${SONDAGE} s"
vert "Battement position : ${BATTEMENT} s"

# ---------------------------------------------------------------------------
etape "2. Un livreur en ligne"

post() {
  local route="$1" corps="$2" jeton="${3:-}"
  local args=(-sS -X POST "$GATEWAY$route" -H 'Content-Type: application/json' -d "$corps")
  [ -n "$jeton" ] && args+=(-H "Authorization: Bearer $jeton")
  curl "${args[@]}"
}

ADMIN="$(post /api/admin/v1/auth/login \
  "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$ADMIN" ] || echouer "connexion administrateur impossible — la passerelle repond-elle sur $GATEWAY ?"

TEL="+2290195${HORODATAGE: -6}"
DEFI="$(post /api/driver/v1/auth/otp/request \
  "{\"phone\":\"$TEL\",\"deviceId\":\"mesure\"}" | jq -r '.challengeId // empty')"
[ -n "$DEFI" ] || echouer "pas de defi OTP."

REPONSE="$(post /api/driver/v1/auth/otp/verify \
  "{\"challengeId\":\"$DEFI\",\"code\":\"000000\",\"deviceId\":\"mesure\",\"displayName\":\"Livreur Mesure\"}")"
LIVREUR="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
LIVREUR_ID="$(echo "$REPONSE" | jq -r '.principal.driverId // empty')"
[ -n "$LIVREUR" ] || echouer "pas de jeton livreur — $REPONSE"
vert "Livreur $LIVREUR_ID"

bleu "Attente de la creation du profil par le service Driver (Kafka)…"
sleep 8

post "/api/admin/v1/drivers/$LIVREUR_ID/kyc" '{"approved":true}' "$ADMIN" >/dev/null
post /api/driver/v1/presence/online "{\"latitude\":$LAT,\"longitude\":$LON}" "$LIVREUR" >/dev/null
vert "Dossier valide, livreur en ligne"

printf '  Jeton : %s octets\n' "${#LIVREUR}"

# ---------------------------------------------------------------------------
etape "3. Mesure ($TOURS tours par route)"

# Rend « octets montants octets descendants » pour un appel.
#
# « size_request » compte les octets envoyes, en-tetes compris ; la reponse est
# la somme de ses en-tetes et de son corps. Les trois sont donnes par curl
# lui-meme : on ne compte pas les caracteres d'une chaine, on demande a celui
# qui a parle sur la ligne.
mesurer() {
  local sortie
  sortie="$(curl -sS -o /dev/null "$@" \
    -w '%{size_request} %{size_header} %{size_download}')"
  awk '{print $1, $2 + $3}' <<< "$sortie"
}

moyenne() { awk '{s+=$1; n++} END {if (n) printf "%.0f", s/n}'; }

: > "$TEMPO/offre.txt"
: > "$TEMPO/position.txt"

for ((i = 0; i < TOURS; i++)); do
  mesurer "$GATEWAY/api/driver/v1/offers/current" \
    -H "Authorization: Bearer $LIVREUR" >> "$TEMPO/offre.txt"

  # LE MEME CORPS QUE L'APPLICATION, CLE D'IDEMPOTENCE COMPRISE. Elle pese
  # trente-six octets plus le nom de l'en-tete, a chaque battement : la
  # laisser de cote fausserait la mesure de plusieurs pour cent.
  mesurer -X POST "$GATEWAY/api/driver/v1/position" \
    -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $LIVREUR" \
    -H "Idempotency-Key: $(uuidgen 2>/dev/null || echo "00000000-0000-0000-0000-00000000000$i")" \
    -d "{\"latitude\":$LAT,\"longitude\":$LON,\"capturedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%S.000Z)\"}" \
    >> "$TEMPO/position.txt"
done

OFFRE_UP="$(awk '{print $1}' "$TEMPO/offre.txt" | moyenne)"
OFFRE_DOWN="$(awk '{print $2}' "$TEMPO/offre.txt" | moyenne)"
POS_UP="$(awk '{print $1}' "$TEMPO/position.txt" | moyenne)"
POS_DOWN="$(awk '{print $2}' "$TEMPO/position.txt" | moyenne)"

printf '  %-24s %6s montants %6s descendants\n' 'GET /offers/current' "$OFFRE_UP" "$OFFRE_DOWN"
printf '  %-24s %6s montants %6s descendants\n' 'POST /position'      "$POS_UP"   "$POS_DOWN"

# ---------------------------------------------------------------------------
etape "4. Projection"

awk -v so="$SONDAGE" -v ba="$BATTEMENT" \
    -v ou="$OFFRE_UP" -v od="$OFFRE_DOWN" -v pu="$POS_UP" -v pd="$POS_DOWN" '
BEGIN {
  n_offre = 3600 / so;
  n_pos   = 3600 / ba;
  o_heure = n_offre * (ou + od) + n_pos * (pu + pd);

  printf "  %-34s %8.0f\n", "Requetes par heure",      n_offre + n_pos;
  printf "  %-34s %8.0f  (%.0f + %.0f)\n", "  dont offres / positions", n_offre + n_pos, n_offre, n_pos;
  printf "  %-34s %8.0f octets\n", "Octets par heure",  o_heure;
  printf "  %-34s %8.2f Mo\n", "Par heure",             o_heure / 1048576;
  printf "  %-34s %8.2f Mo\n", "Pour dix heures en ligne", o_heure * 10 / 1048576;
  printf "  %-34s %8.2f Mo\n", "Pour vingt-cinq jours a 10 h", o_heure * 250 / 1048576;
}'

cat <<'FIN'

  CE CHIFFRE EST UN MINORANT. Il ne compte ni TLS, ni les poignees de main, ni
  les reprises sur reseau instable. En production, comptez davantage.

  Il ne compte pas non plus la batterie : un point GPS toutes les vingt
  secondes pendant dix heures est l'autre moitie de la facture, et elle se
  mesure sur un telephone, pas ici.
FIN
