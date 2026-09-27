#!/usr/bin/env bash
#
# LA CARTE DES LIVREURS, DE BOUT EN BOUT.
#
# Deux livreurs envoient une position -> la route d'exploitation les rend ->
# une position vieillie disparait -> un livreur ne peut pas lire la carte.
#
# CE QUE CE SCRIPT VERIFIE VRAIMENT, et qui ne se voit nulle part ailleurs :
# la lecture globale part de l'ensemble des horodatages, pas de l'index GEO.
# Si l'ordre des deux etait inverse, tout passerait ici SAUF l'etape 5 — la
# position perimee resterait sur la carte. C'est pour elle que ce script
# existe.
#
#   ./scripts/carte-positions.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
IDENTITY_DIR="src/Services/Identity"
COMPOSE_INFRA="deploy/docker-compose.yml"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"
REDIS_PASSWORD="${REDIS_PASSWORD:-hba-redis-dev}"

HORODATAGE="$(date +%s)"

vert()  { printf '  \033[1;32m✓\033[0m %s\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }
echouer() { rouge "ECHEC : $*"; exit 1; }

attendu() {
  if [ "$2" = "$3" ]; then
    printf '  \033[1;32m✓\033[0m %-46s %s\n' "$1" "$3"
  else
    printf '  \033[1;31m✗\033[0m %-46s attendu «%s», obtenu «%s»\n' "$1" "$2" "$3" >&2
    exit 1
  fi
}

TEMPO="$(mktemp -d)"
command -v jq >/dev/null || echouer "jq est requis."
command -v docker >/dev/null || echouer "docker est requis."

redis() { docker compose -f "$COMPOSE_INFRA" exec -T redis redis-cli -a "$REDIS_PASSWORD" --no-auth-warning "$@"; }

# --------------------------------------------------------- Images a jour --

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  etape "0. Construction (SKIP_BUILD=1 pour sauter)"
  for service in Identity Driver; do
    printf '  %s…\n' "$service"
    docker compose -f "src/Services/$service/docker-compose.yml" build >/dev/null \
      || echouer "la construction de $service a echoue."
  done
  printf '  Gateway…\n'
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build >/dev/null \
    || echouer "la construction de la gateway a echoue."

  docker compose -f src/Services/Driver/docker-compose.yml up -d --force-recreate >/dev/null
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml up -d --force-recreate >/dev/null
  bleu "Attente du redemarrage…"
  sleep 10
fi

restaurer() {
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" \
    up -d --force-recreate identity >/dev/null 2>&1 || true
  rm -rf "$TEMPO"
}
trap restaurer EXIT

etape "0 bis. Identity en code OTP fixe"
docker compose -f "$IDENTITY_DIR/docker-compose.yml" \
  -f "$IDENTITY_DIR/compose.dev-otp.yml" up -d --force-recreate identity >/dev/null
sleep 10

# ------------------------------------------------------------- 1. Routes --

etape "1. La route existe-t-elle ?"
CODE="$(curl -sS -o /dev/null -w '%{http_code}' "$GATEWAY/api/admin/v1/drivers/positions")"
# Sans jeton, une route qui existe rend 401 ; une route absente rend 404.
[ "$CODE" != "404" ] || echouer "GET /api/admin/v1/drivers/positions est absente : image de passerelle perimee."
attendu "Route presente (401 sans jeton)" "401" "$CODE"

# ------------------------------------------------------------ 2. Comptes --

etape "2. Administrateur"
ADMIN="$(curl -sS -X POST "$GATEWAY/api/web/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"carte\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$ADMIN" ] || echouer "Identity refuse $ADMIN_EMAIL."
vert "Connecte."

creer_livreur() {
  local tel="$1" nom="$2"
  local defi reponse
  defi="$(curl -sS -X POST "$GATEWAY/api/driver/v1/auth/otp/request" -H 'Content-Type: application/json' \
    -d "{\"phone\":\"$tel\",\"deviceId\":\"carte\"}" | jq -r '.challengeId // empty')"
  [ -n "$defi" ] || echouer "pas de defi OTP pour $tel."

  reponse="$(curl -sS -X POST "$GATEWAY/api/driver/v1/auth/otp/verify" -H 'Content-Type: application/json' \
    -d "{\"challengeId\":\"$defi\",\"code\":\"000000\",\"deviceId\":\"carte\",\"displayName\":\"$nom\"}")"
  echo "$reponse" | jq -r '.accessToken // empty' > "$TEMPO/jeton"
  echo "$reponse" | jq -r '.principal.driverId // empty' > "$TEMPO/id"
  [ -s "$TEMPO/jeton" ] || echouer "pas de jeton livreur pour $tel — $reponse"
  [ -s "$TEMPO/id" ] || echouer "le jeton ne porte pas driver_id — $reponse"
}

etape "3. Deux livreurs"
creer_livreur "+2290193${HORODATAGE: -6}" "Carte Nord"
JETON_A="$(cat "$TEMPO/jeton")"; ID_A="$(cat "$TEMPO/id")"
creer_livreur "+2290194${HORODATAGE: -6}" "Carte Sud"
JETON_B="$(cat "$TEMPO/jeton")"; ID_B="$(cat "$TEMPO/id")"
vert "Nord $ID_A"
vert "Sud  $ID_B"

bleu "Attente de la creation des profils par le service Driver (Kafka)…"
sleep 8

# ---------------------------------------------------------- 4. Positions --

pousser() {
  local jeton="$1" lat="$2" lon="$3" code
  code="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$GATEWAY/api/driver/v1/position" \
    -H "Authorization: Bearer $jeton" -H 'Content-Type: application/json' \
    -d "{\"latitude\":$lat,\"longitude\":$lon}")"
  [ "$code" = "204" ] || echouer "envoi de position refuse ($code)."
}

etape "4. Les deux positions remontent"
pousser "$JETON_A" 6.3850 2.3900
pousser "$JETON_B" 6.3550 2.4200

carte() { curl -sS "$GATEWAY/api/admin/v1/drivers/positions" -H "Authorization: Bearer $ADMIN"; }

REPONSE="$(carte)"
echo "$REPONSE" > "$TEMPO/carte.json"

attendu "Livreurs sur la carte" "2" "$(echo "$REPONSE" | jq '[.positions[] | select(.driverId=="'"$ID_A"'" or .driverId=="'"$ID_B"'")] | length')"
attendu "Fenetre de fraicheur annoncee" "true" "$(echo "$REPONSE" | jq '(.freshnessSeconds // 0) > 0')"

# LES ENUMERATIONS SORTENT EN NOMS, PAS EN ENTIERS. C'est ce qui distingue
# cette route des voisines, et la console s'y fie.
attendu "Statut rendu par son nom" "true" \
  "$(echo "$REPONSE" | jq '[.positions[].operationalStatus] | all(type == "string")')"
attendu "Vehicule rendu par son nom" "true" \
  "$(echo "$REPONSE" | jq '[.positions[].vehicleType] | all(startswith("VEHICLE_TYPE_"))')"
attendu "Date en ISO, pas en { seconds, nanos }" "true" \
  "$(echo "$REPONSE" | jq '[.positions[].seenAt] | all(type == "string")')"

# LA LATITUDE REVIENT-ELLE INTACTE ? Redis GEO stocke en geohash 52 bits :
# l'erreur est de l'ordre du demi-metre, donc invisible a quatre decimales.
attendu "Latitude du Nord conservee" "6.385" \
  "$(echo "$REPONSE" | jq -r '[.positions[] | select(.driverId=="'"$ID_A"'")][0].latitude | .*1000 | round / 1000')"
attendu "Nom du livreur joint depuis la base" "Carte Sud" \
  "$(echo "$REPONSE" | jq -r '[.positions[] | select(.driverId=="'"$ID_B"'")][0].displayName')"

# ------------------------------------------------- 5. La fraicheur filtre --

# LE SEUL MOYEN DE FABRIQUER UNE POSITION PERIMEE. Le service horodate a la
# RECEPTION, pas d'apres le telephone : impossible de vieillir un point par
# l'API. On vieillit donc son score directement dans Redis — ce qui teste
# exactement ce que le service lira.
etape "5. Une position perimee sort de la carte"
VIEUX="$(( $(date +%s) - 86400 ))"
redis ZADD driver:positions:seen "$VIEUX" "$ID_A" >/dev/null \
  || echouer "impossible d'ecrire dans Redis (mot de passe ? conteneur ?)."

REPONSE="$(carte)"
attendu "Le Nord a disparu" "0" "$(echo "$REPONSE" | jq '[.positions[] | select(.driverId=="'"$ID_A"'")] | length')"
attendu "Le Sud est toujours la" "1" "$(echo "$REPONSE" | jq '[.positions[] | select(.driverId=="'"$ID_B"'")] | length')"

# ------------------------------------------------ 6. Un livreur ne lit pas --

etape "6. La carte est refusee a un livreur"
CODE="$(curl -sS -o "$TEMPO/refus.json" -w '%{http_code}' \
  "$GATEWAY/api/admin/v1/drivers/positions" -H "Authorization: Bearer $JETON_A")"
case "$CODE" in
  401|403) vert "Refus ($CODE)." ;;
  *) rouge "Un livreur a obtenu la carte de ses collegues ($CODE) :"; cat "$TEMPO/refus.json" >&2; exit 1 ;;
esac

# ------------------------------------------------------- 7. Le plafond --

# PAS DE TEST DU PASSAGE HORS LIGNE ICI, ET C'EST DELIBERE : y passer exige
# un dossier valide, que ce script ne monte pas — « make dossier » couvre ce
# chemin. RemoveAsync est d'ailleurs anterieur a cette carte. Ce qui est neuf,
# et donc ce qu'on mesure ici, c'est le plafond de la lecture globale.
etape "7. Le plafond limite la liste"
attendu "limit=1 rend un seul point" "1" \
  "$(curl -sS "$GATEWAY/api/admin/v1/drivers/positions?limit=1" -H "Authorization: Bearer $ADMIN" | jq '.positions | length')"

printf '\n\033[1;32mLa carte des positions tient de bout en bout.\033[0m\n'
