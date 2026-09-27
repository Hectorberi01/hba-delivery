#!/usr/bin/env bash
#
# PROPOSER UNE COURSE A UN LIVREUR CHOISI, DE BOUT EN BOUT.
#
# CE QUE CE SCRIPT PROUVE, ET QUI NE SE VOIT NULLE PART AILLEURS : le livreur
# est place A DIX KILOMETRES du retrait, donc HORS des trois rayons du moteur
# (2, 4 et 6 km). Le moteur ne lui propose rien — on le verifie — et
# l'exploitation lui propose la course quand meme. Si l'offre manuelle passait
# par le meme chemin que les vagues, l'etape 9 echouerait.
#
# Il verifie aussi les cinq refus, un par un : livreur inconnu, livreur hors
# ligne, deuxieme offre au meme livreur, course deja prise, et un livreur qui
# essaie d'appeler la route d'exploitation.
#
#   ./scripts/offre-manuelle.sh
#   SKIP_BUILD=1 ./scripts/offre-manuelle.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
IDENTITY_DIR="src/Services/Identity"
PAYMENT_DIR="src/Services/Payment"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"

HORODATAGE="$(date +%s)"

# Ganhi, comme etape4. Le retrait est le point de reference des distances.
PICKUP_LAT="6.3654";  PICKUP_LON="2.4183"
DROPOFF_LAT="6.3520"; DROPOFF_LON="2.3810"

# DIX KILOMETRES AU NORD. Un degre de latitude vaut environ 111 km : +0,09°
# met le livreur a ~10 km, largement au-dela du dernier rayon (6 km). C'est
# tout l'interet du test — le moteur ne peut pas le trouver.
LOIN_LAT="6.4554"; LOIN_LON="2.4183"

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

post() {
  local route="$1" corps="$2" jeton="${3:-}" extra="${4:-}"
  local args=(-sS -X POST "$GATEWAY$route" -H 'Content-Type: application/json' -d "$corps")
  [ -n "$jeton" ] && args+=(-H "Authorization: Bearer $jeton")
  [ -n "$extra" ] && args+=(-H "$extra")
  curl "${args[@]}"
}

get() { curl -sS "$GATEWAY$1" -H "Authorization: Bearer $2"; }

# --------------------------------------------------------- Images a jour --

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  etape "0. Construction (SKIP_BUILD=1 pour sauter)"
  for service in Identity Driver Dispatch Payment; do
    printf '  %s…\n' "$service"
    docker compose -f "src/Services/$service/docker-compose.yml" build >/dev/null \
      || echouer "la construction de $service a echoue."
  done
  printf '  Gateway…\n'
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build >/dev/null \
    || echouer "la construction de la gateway a echoue."

  docker compose -f src/Services/Driver/docker-compose.yml up -d --force-recreate >/dev/null
  docker compose -f src/Services/Dispatch/docker-compose.yml up -d --force-recreate >/dev/null
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml up -d --force-recreate >/dev/null
fi

restaurer() {
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" up -d --force-recreate identity >/dev/null 2>&1 || true
  docker compose -f "$PAYMENT_DIR/docker-compose.yml"  up -d --force-recreate payment  >/dev/null 2>&1 || true
  rm -rf "$TEMPO"
}
trap restaurer EXIT

etape "0 bis. Identity en code OTP fixe, Payment en paiement factice"
docker compose -f "$IDENTITY_DIR/docker-compose.yml" -f "$IDENTITY_DIR/compose.dev-otp.yml" \
  up -d --force-recreate identity >/dev/null
docker compose -f "$PAYMENT_DIR/docker-compose.yml" -f "$PAYMENT_DIR/compose.dev-loopback.yml" \
  up -d --force-recreate payment >/dev/null
bleu "Attente du redemarrage…"
sleep 12

# ------------------------------------------------------------- 1. Route --

etape "1. La route existe-t-elle ?"
# Sans jeton, une route qui existe rend 401 ; une route absente rend 404.
CODE="$(curl -sS -o /dev/null -w '%{http_code}' -X POST \
  "$GATEWAY/api/admin/v1/deliveries/00000000-0000-0000-0000-000000000000/offer" \
  -H 'Content-Type: application/json' -d '{"driverId":"x"}')"
[ "$CODE" != "404" ] || echouer "POST /api/admin/v1/deliveries/{id}/offer est absente : image de passerelle perimee."
attendu "Route presente (401 sans jeton)" "401" "$CODE"

# ---------------------------------------------------------- 2. Comptes --

etape "2. Administrateur"
ADMIN="$(post /api/web/v1/auth/login \
  "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"offre\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$ADMIN" ] || echouer "Identity refuse $ADMIN_EMAIL."
vert "Connecte."

creer_compte() {
  local canal="$1" tel="$2" nom="$3" defi reponse
  defi="$(post "/api/$canal/v1/auth/otp/request" "{\"phone\":\"$tel\",\"deviceId\":\"offre\"}" \
    | jq -r '.challengeId // empty')"
  [ -n "$defi" ] || echouer "pas de defi OTP pour $tel."

  reponse="$(post "/api/$canal/v1/auth/otp/verify" \
    "{\"challengeId\":\"$defi\",\"code\":\"000000\",\"deviceId\":\"offre\",\"displayName\":\"$nom\"}")"
  echo "$reponse" | jq -r '.accessToken // empty' > "$TEMPO/jeton"
  echo "$reponse" | jq -r '.principal.driverId // empty' > "$TEMPO/id"
  [ -s "$TEMPO/jeton" ] || echouer "pas de jeton pour $tel — $reponse"
}

etape "3. Un client, un livreur loin, un livreur hors ligne"
creer_compte client "+2290195${HORODATAGE: -6}" "Client Offre"
CLIENT="$(cat "$TEMPO/jeton")"

creer_compte driver "+2290196${HORODATAGE: -6}" "Livreur Loin"
LOIN_JETON="$(cat "$TEMPO/jeton")"; LOIN_ID="$(cat "$TEMPO/id")"
[ -n "$LOIN_ID" ] || echouer "le jeton livreur ne porte pas driver_id."

creer_compte driver "+2290197${HORODATAGE: -6}" "Livreur Absent"
ABSENT_ID="$(cat "$TEMPO/id")"
[ -n "$ABSENT_ID" ] || echouer "le jeton livreur ne porte pas driver_id."

vert "Loin   $LOIN_ID"
vert "Absent $ABSENT_ID"

bleu "Attente de la creation des profils par le service Driver (Kafka)…"
sleep 8

etape "4. Dossiers valides, puis un seul passe en ligne"
for identifiant in "$LOIN_ID" "$ABSENT_ID"; do
  STATUT="$(post "/api/admin/v1/drivers/$identifiant/kyc" '{"approved":true}' "$ADMIN" \
    | jq -r '.verificationStatus // empty')"
  attendu "Dossier ${identifiant:0:8} valide" "2" "$STATUT"
done

post /api/driver/v1/presence/online "{\"latitude\":$LOIN_LAT,\"longitude\":$LOIN_LON}" "$LOIN_JETON" \
  > "$TEMPO/online.json"
attendu "Livreur Loin disponible" "2" "$(jq -r '.operationalStatus // empty' "$TEMPO/online.json")"
vert "Livreur Absent reste hors ligne, volontairement."

# ------------------------------------------------------- 5. Une course --

etape "5. Devis, course, paiement"
DEVIS="$(post /api/client/v1/quotes \
  "{\"pickupLatitude\":$PICKUP_LAT,\"pickupLongitude\":$PICKUP_LON,\"dropoffLatitude\":$DROPOFF_LAT,\"dropoffLongitude\":$DROPOFF_LON,\"packageWeightGrams\":2000}" \
  "$CLIENT" | jq -r '.quoteId // empty')"
[ -n "$DEVIS" ] || echouer "pas de devis."

CORPS=$(cat <<JSON
{
  "quoteId": "$DEVIS",
  "pickup":  {"latitude":$PICKUP_LAT,"longitude":$PICKUP_LON,"landmark":"Ganhi","phone":"+2290195${HORODATAGE: -6}","contactName":"Client Offre"},
  "dropoff": {"latitude":$DROPOFF_LAT,"longitude":$DROPOFF_LON,"landmark":"Fidjrosse","phone":"+2290195${HORODATAGE: -6}","contactName":"Destinataire Offre"},
  "recipientName": "Destinataire Offre",
  "recipientPhone": "+2290195${HORODATAGE: -6}",
  "packageDescription": "Colis offre manuelle",
  "packageWeightGrams": 2000
}
JSON
)
COURSE="$(post /api/client/v1/deliveries "$CORPS" "$CLIENT" "Idempotency-Key: offre-$HORODATAGE" \
  | jq -r '.delivery.id // empty')"
[ -n "$COURSE" ] || echouer "pas de course creee."
vert "Course $COURSE"

# ON N'ATTEND PAS LE STATUT « RECHERCHE LIVREUR » (4), ET C'EST LE PIEGE DE CE
# SCENARIO — la premiere version de ce script s'y est cassee.
#
# Delivery ne quitte PAYEE qu'en recevant OfferSent : cote client, c'est la
# PREMIERE OFFRE qui ouvre la recherche, pas l'ouverture de la recherche
# elle-meme. Or ici personne n'est a portee — c'est tout le sujet du test —
# donc les trois vagues ne publient aucune offre, et le statut ne bougera pas
# avant NO_DRIVER_FOUND. Attendre 4 serait attendre pour rien.
#
# Que le client lise « payee » pendant quatre-vingt-dix secondes alors que le
# moteur cherche est un defaut a part entiere, signale le 27 septembre 2026.
# Ce script ne le corrige pas : il l'evite, et le dit.
bleu "Attente du paiement (fournisseur factice)…"
PAYEE="non"
for _ in $(seq 1 25); do
  sleep 2
  ETAT="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT" | jq -r '.status // empty')"
  printf '  statut : %s\n' "${ETAT:-?}"
  case "$ETAT" in
    3|4|6) PAYEE="oui"; break ;;
    2) echouer "le paiement factice a echoue : verifiez LOOPBACK_OUTCOME." ;;
    5) echouer "la course est deja en NO_DRIVER_FOUND : le test a demarre trop tard." ;;
  esac
done
[ "$PAYEE" = "oui" ] || echouer "la course n'a jamais ete payee."
vert "Course payee."

# ----------------------- 6. La recherche existe, et elle refuse un inconnu --

offrir() {
  local livreur="$1" jeton="${2:-$ADMIN}"
  post "/api/admin/v1/deliveries/$COURSE/offer" \
    "{\"driverId\":\"$livreur\",\"reason\":\"script offre-manuelle\"}" "$jeton"
}

offrir_code() {
  curl -sS -o "$TEMPO/sonde.json" -w '%{http_code}' -X POST \
    "$GATEWAY/api/admin/v1/deliveries/$COURSE/offer" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN" \
    -d "{\"driverId\":\"$1\"}"
}

# CE QUE L'OFFRE MANUELLE EXIGE VRAIMENT : que l'agregat de recherche existe.
# Il nait de DeliveryConfirmed, cote Dispatch. Tant que Dispatch ne l'a pas, la
# route rend 404 ; des qu'il l'a, elle rend un refus nomme. Cette sonde est
# donc aussi l'assertion « un livreur inconnu est refuse, pas accepte en
# silence » : deux mesures pour un appel, parce que c'est le meme appel.
etape "6. La recherche est ouverte, et un livreur inconnu y est refuse"
PRETE="non"
for _ in $(seq 1 20); do
  CODE="$(offrir_code "11111111-1111-1111-1111-111111111111")"
  if [ "$CODE" = "200" ]; then PRETE="oui"; break; fi
  printf '  recherche pas encore ouverte (HTTP %s)…\n' "$CODE"
  sleep 2
done
[ "$PRETE" = "oui" ] || echouer "Dispatch n'a jamais ouvert la recherche pour cette course."
attendu "Refus nomme" "DRIVER_NOT_FOUND" "$(jq -r '.rejectionCode // empty' "$TEMPO/sonde.json")"

# --------------------------------- 7. Le moteur ne trouve pas notre homme --

# C'EST LA MESURE QUI DONNE SON SENS AU RESTE. A dix kilometres, il est hors
# des trois rayons : si une offre lui parvenait ici, l'etape 9 ne prouverait
# plus rien. On laisse d'abord au planificateur le temps d'ouvrir la premiere
# vague — constater une absence avant que le moteur ait tourne ne prouverait
# rien non plus.
etape "7. Le moteur, lui, ne lui propose rien"
sleep 10
# Une offre absente rend un message vide, pas une erreur : « je n'ai rien en
# cours » est la reponse normale d'un livreur en ligne qui attend.
attendu "Aucune offre du moteur" "" \
  "$(get /api/driver/v1/offers/current "$LOIN_JETON" | jq -r '.id // ""')"

etape "8. Un livreur hors ligne est refuse"
attendu "Refus nomme" "DRIVER_NOT_AVAILABLE" \
  "$(offrir "$ABSENT_ID" | jq -r '.rejectionCode // empty')"

# ------------------------------------------------------- 9. L'offre passe --

etape "9. L'exploitation propose la course au livreur lointain"
offrir "$LOIN_ID" > "$TEMPO/offre.json"
attendu "Offre envoyee" "true" "$(jq -r '.sent' "$TEMPO/offre.json")"

# LA DISTANCE EST MESUREE, PAS NULLE. Un zero se lirait « sur place », et
# c'est precisement l'erreur qu'on ne veut pas faire dire au systeme.
DISTANCE="$(jq -r '.offer.distanceToPickupMeters // 0' "$TEMPO/offre.json")"
attendu "Distance mesuree, pas zero" "true" "$([ "$DISTANCE" -gt 8000 ] && echo true || echo false)"
printf '  \033[1;32m✓\033[0m %-46s %s m\n' "Distance rendue" "$DISTANCE"

etape "10. Le livreur la voit, avec la vague zero"
OFFRE="$(get /api/driver/v1/offers/current "$LOIN_JETON")"
echo "$OFFRE" > "$TEMPO/vue-livreur.json"
OFFRE_ID="$(jq -r '.id // empty' "$TEMPO/vue-livreur.json")"
[ -n "$OFFRE_ID" ] || echouer "le livreur ne voit pas l'offre — $OFFRE"

# VAGUE ZERO SIGNIFIE « A LA MAIN ». Si cette valeur devenait 1, l'offre
# aurait consomme un tour du moteur, et la course aurait perdu un rayon.
attendu "Vague de l'offre" "0" "$(jq -r '.waveNumber // 0' "$TEMPO/vue-livreur.json")"
attendu "Repere de collecte transmis" "Ganhi" "$(jq -r '.pickupLandmark // empty' "$TEMPO/vue-livreur.json")"

# L'ADRESSE DE DESTINATION N'EST PAS DANS L'OFFRE, manuelle ou non : le
# referentiel l'interdit avant acceptation.
attendu "Pas d'adresse de destination" "false" \
  "$(jq 'has("dropoff") or has("dropoffLandmark")' "$TEMPO/vue-livreur.json")"

etape "11. Une deuxieme offre au meme livreur est refusee"
attendu "Refus nomme" "ALREADY_OFFERED" "$(offrir "$LOIN_ID" | jq -r '.rejectionCode // empty')"

etape "12. Un livreur ne peut pas poser d'offre"
CODE="$(curl -sS -o "$TEMPO/refus.json" -w '%{http_code}' -X POST \
  "$GATEWAY/api/admin/v1/deliveries/$COURSE/offer" \
  -H 'Content-Type: application/json' -H "Authorization: Bearer $LOIN_JETON" \
  -d "{\"driverId\":\"$LOIN_ID\"}")"
case "$CODE" in
  401|403) vert "Refus ($CODE)." ;;
  *) rouge "Un livreur a pose une offre ($CODE) :"; cat "$TEMPO/refus.json" >&2; exit 1 ;;
esac

# --------------------------------------------------- 13 a 14. Acceptation --

etape "13. Le livreur accepte, la course lui est affectee"
post "/api/driver/v1/offers/$OFFRE_ID/accept" '{}' "$LOIN_JETON" > "$TEMPO/accept.json"
attendu "Offre acceptee" "$LOIN_ID" "$(jq -r '.driverId // empty' "$TEMPO/accept.json")"

bleu "Attente de la propagation vers Delivery (Kafka)…"
AFFECTEE="non"
for _ in $(seq 1 15); do
  sleep 2
  ETAT="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT" | jq -r '.status // empty')"
  if [ "$ETAT" = "6" ]; then AFFECTEE="oui"; break; fi
done
attendu "Course en DRIVER_ASSIGNED" "oui" "$AFFECTEE"

etape "14. On ne propose plus une course deja prise"
attendu "Refus nomme" "DISPATCH_CLOSED" "$(offrir "$LOIN_ID" | jq -r '.rejectionCode // empty')"

printf '\n\033[1;32mL'"'"'offre manuelle tient de bout en bout : hors rayon, mesuree, refusable, tracee.\033[0m\n'
