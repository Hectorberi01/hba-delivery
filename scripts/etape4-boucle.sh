#!/usr/bin/env bash
#
# ETAPE 4 — FERMER LA BOUCLE, DE BOUT EN BOUT.
#
# Paiement -> DeliveryConfirmed -> dispatch -> offre -> acceptation ->
# DRIVER_ASSIGNED cote client, ON_MISSION cote livreur.
#
# Le script ne simule rien : il passe par la gateway, comme les applications.
# Deux surcouches de developpement sont posees le temps du test, et retirees a
# la sortie quoi qu'il arrive :
#   - Identity en code OTP fixe (000000), pour creer des comptes sans SMS ;
#   - Payment en fournisseur factice, parce que le bac a sable FedaPay refuse
#     toute transaction sur un compte non verifie.
#
#   ./scripts/etape4-boucle.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
IDENTITY_DIR="src/Services/Identity"
PAYMENT_DIR="src/Services/Payment"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"

# Cotonou : Ganhi (enlevement) -> Fidjrosse (livraison), ~4 km.
PICKUP_LAT="6.3654"; PICKUP_LON="2.4183"
DROPOFF_LAT="6.3520"; DROPOFF_LON="2.3810"
# Le livreur attend a 600 m de l'enlevement : dans la premiere vague (2 km).
DRIVER_LAT="6.3700"; DRIVER_LON="2.4210"

HORODATAGE="$(date +%s)"
TEL_CLIENT="${TEL_CLIENT:-+2290190${HORODATAGE: -6}}"
TEL_LIVREUR="${TEL_LIVREUR:-+2290191${HORODATAGE: -6}}"

bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }

echouer() { rouge "ECHEC : $*"; exit 1; }

# --------------------------------------------------------------- Prealables --

command -v jq >/dev/null || echouer "jq est requis (brew install jq)."
command -v docker >/dev/null || echouer "docker est requis."

curl -fsS --max-time 5 "$GATEWAY/health" >/dev/null \
  || echouer "La gateway ne repond pas sur $GATEWAY/health. Lancez « make up » puis « make services-up »."

# ------------------------------------------------------ Images a jour --

# LE PIEGE QUI A COUTE TROIS DEPANNAGES AUJOURD'HUI : « up -d » sans « --build »
# relance l'image precedente. Tout ce que ce script exerce — jeton de service,
# dispatch, paiement factice — est du code neuf : contre une image d'hier, il
# echoue en racontant autre chose que la vraie cause.
#
# La construction fait aussi office de compilation : une erreur de code
# s'arrete ici, avant qu'un seul compte ne soit cree.
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  etape "0. Construction des images (SKIP_BUILD=1 pour sauter)"
  for service in Identity Driver Dispatch Delivery Payment; do
    printf '  %s…\n' "$service"
    docker compose -f "src/Services/$service/docker-compose.yml" build >/dev/null \
      || echouer "la construction de $service a echoue. Relancez-la sans -q pour voir l'erreur :
  docker compose -f src/Services/$service/docker-compose.yml build"
  done
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build >/dev/null \
    || echouer "la construction de la gateway a echoue."

  etape "0 bis. Redemarrage des services sur les nouvelles images"
  for service in Driver Dispatch Delivery; do
    docker compose -f "src/Services/$service/docker-compose.yml" up -d --force-recreate
  done
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml up -d --force-recreate
  bleu "Si les migrations de Driver et Dispatch ne sont pas appliquees, lancez « make migrate »."
  sleep 8
fi

# ------------------------------------------------- Surcouches de developpement --

restaurer() {
  etape "Restauration de la configuration normale"
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" up -d --force-recreate identity >/dev/null 2>&1 || true
  docker compose -f "$PAYMENT_DIR/docker-compose.yml" up -d --force-recreate payment >/dev/null 2>&1 || true
  bleu "Identity et Payment sont revenus a leur configuration de production."
}
trap restaurer EXIT

etape "Bascule d'Identity en code OTP fixe et de Payment en paiement factice"
docker compose -f "$IDENTITY_DIR/docker-compose.yml" -f "$IDENTITY_DIR/compose.dev-otp.yml" \
  up -d --force-recreate identity
docker compose -f "$PAYMENT_DIR/docker-compose.yml" -f "$PAYMENT_DIR/compose.dev-loopback.yml" \
  up -d --force-recreate payment

bleu "Attente du redemarrage des deux services…"
sleep 12

# ------------------------------------------------------------------ Outils --

# post <route> <corps-json> [jeton] [en-tete supplementaire]
post() {
  local route="$1" corps="$2" jeton="${3:-}" extra="${4:-}"
  local args=(-sS -X POST "$GATEWAY$route" -H 'Content-Type: application/json' -d "$corps")
  [ -n "$jeton" ] && args+=(-H "Authorization: Bearer $jeton")
  [ -n "$extra" ] && args+=(-H "$extra")
  curl "${args[@]}"
}

get() {
  curl -sS "$GATEWAY$1" -H "Authorization: Bearer $2"
}

# Verifie qu'une reponse est bien du JSON et non une page d'erreur.
json_ou_echec() {
  local reponse="$1" contexte="$2"
  echo "$reponse" | jq -e . >/dev/null 2>&1 || echouer "$contexte : reponse inattendue — $reponse"
  echo "$reponse"
}

# --------------------------------------------------------- 1. Administrateur --

etape "1. Connexion de l'administrateur"
REPONSE="$(post /api/web/v1/auth/login \
  "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"etape4\"}")"
json_ou_echec "$REPONSE" "connexion admin" >/dev/null
ADMIN_TOKEN="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
if [ -z "$ADMIN_TOKEN" ]; then
  rouge "Identity refuse $ADMIN_EMAIL. Reponse : $REPONSE"
  rouge ""
  rouge "Ce que disent les journaux d'Identity a propos de l'amorcage :"
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" logs --tail 300 identity 2>&1 \
    | grep -iE "administrateur|amor|bootstrap" | tail -10 | sed 's/^/    /' \
    || true
  rouge ""
  rouge "Trois causes possibles, dans l'ordre de vraisemblance :"
  rouge "  1. « Un administrateur existe deja » : un compte admin a ete cree avant que"
  rouge "     BOOTSTRAP_ADMIN_* soient renseignes. L'amorcage est idempotent et ne"
  rouge "     refait rien. Donnez le mot de passe de ce compte :"
  rouge "       BOOTSTRAP_ADMIN_EMAIL=... BOOTSTRAP_ADMIN_PASSWORD=... make etape4"
  rouge "  2. Aucune ligne d'amorcage du tout : le conteneur tourne sur une image ou"
  rouge "     AdminBootstrap n'existe pas encore. Relancez sans SKIP_BUILD."
  rouge "  3. Variables absentes : verifiez $IDENTITY_DIR/.env puis"
  rouge "       docker compose -f $IDENTITY_DIR/docker-compose.yml config | grep Bootstrap"
  exit 1
fi
vert "Administrateur connecte."

# ---------------------------------------------------------------- 2. Client --

etape "2. Creation du compte client ($TEL_CLIENT)"
REPONSE="$(post /api/client/v1/auth/otp/request "{\"phone\":\"$TEL_CLIENT\",\"deviceId\":\"etape4-client\"}")"
DEFI="$(json_ou_echec "$REPONSE" "OTP client" | jq -r '.challengeId')"
REPONSE="$(post /api/client/v1/auth/otp/verify \
  "{\"challengeId\":\"$DEFI\",\"code\":\"000000\",\"deviceId\":\"etape4-client\",\"displayName\":\"Client Etape4\"}")"
CLIENT_TOKEN="$(json_ou_echec "$REPONSE" "verification OTP client" | jq -r '.accessToken // empty')"
[ -n "$CLIENT_TOKEN" ] || echouer "pas de jeton client — $REPONSE"
vert "Client connecte."

# --------------------------------------------------------------- 3. Livreur --

etape "3. Creation du compte livreur ($TEL_LIVREUR)"
REPONSE="$(post /api/driver/v1/auth/otp/request "{\"phone\":\"$TEL_LIVREUR\",\"deviceId\":\"etape4-livreur\"}")"
DEFI="$(json_ou_echec "$REPONSE" "OTP livreur" | jq -r '.challengeId')"
REPONSE="$(post /api/driver/v1/auth/otp/verify \
  "{\"challengeId\":\"$DEFI\",\"code\":\"000000\",\"deviceId\":\"etape4-livreur\",\"displayName\":\"Livreur Etape4\"}")"
json_ou_echec "$REPONSE" "verification OTP livreur" >/dev/null
DRIVER_TOKEN="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
DRIVER_ID="$(echo "$REPONSE" | jq -r '.principal.driverId // empty')"
[ -n "$DRIVER_TOKEN" ] || echouer "pas de jeton livreur — $REPONSE"
if [ -z "$DRIVER_ID" ]; then
  rouge "ECHEC : le jeton livreur ne porte pas de driver_id."
  rouge ""
  rouge "Sans ce claim, rien ne marche cote livreur : la passerelle refuse"
  rouge "presence/online, et Delivery ne sait pas filtrer ses courses."
  rouge ""
  rouge "L'identifiant du profil livreur EST celui du compte : Driver ne"
  rouge "genere rien, il reprend l'identifiant recu dans AccountRegistered."
  rouge "AccountViewMapper.DriverIdOf en tire le claim des que le compte"
  rouge "porte le role driver."
  rouge ""
  rouge "Si le claim manque, c'est presque toujours l'image d'Identity qui"
  rouge "est perimee. Reconstruisez-la :"
  rouge "  docker compose -f $IDENTITY_DIR/docker-compose.yml build"
  rouge ""
  rouge "Reponse complete : $REPONSE"
  exit 1
fi
vert "Livreur connecte : $DRIVER_ID"

bleu "Attente de la creation du profil livreur par le service Driver (evenement Kafka)…"
sleep 6

# ------------------------------------------------------------------- 4. KYC --

etape "4. Validation du KYC par l'administrateur"
REPONSE="$(post "/api/admin/v1/drivers/$DRIVER_ID/kyc" '{"approved":true}' "$ADMIN_TOKEN")"
json_ou_echec "$REPONSE" "validation KYC" >/dev/null
echo "$REPONSE" | jq -r '"Statut de verification : \(.verificationStatus // "?")"'

# -------------------------------------------------------------- 5. En ligne --

etape "5. Passage en ligne du livreur"
REPONSE="$(post /api/driver/v1/presence/online \
  "{\"latitude\":$DRIVER_LAT,\"longitude\":$DRIVER_LON}" "$DRIVER_TOKEN")"
json_ou_echec "$REPONSE" "passage en ligne" >/dev/null
echo "$REPONSE" | jq -r '"Statut operationnel : \(.operationalStatus // "?")"'

# -------------------------------------------------------------- 6. Livraison --

etape "6. Devis puis creation de la course"
REPONSE="$(post /api/client/v1/quotes \
  "{\"pickupLatitude\":$PICKUP_LAT,\"pickupLongitude\":$PICKUP_LON,\"dropoffLatitude\":$DROPOFF_LAT,\"dropoffLongitude\":$DROPOFF_LON,\"packageWeightGrams\":2000}" \
  "$CLIENT_TOKEN")"
json_ou_echec "$REPONSE" "devis" >/dev/null
DEVIS="$(echo "$REPONSE" | jq -r '.quoteId')"
PRIX="$(echo "$REPONSE" | jq -r '.totalXof')"
[ -n "$DEVIS" ] && [ "$DEVIS" != "null" ] || echouer "pas de devis — $REPONSE"
vert "Devis $DEVIS : $PRIX XOF"

CORPS=$(cat <<JSON
{
  "quoteId": "$DEVIS",
  "pickup":  {"latitude":$PICKUP_LAT,"longitude":$PICKUP_LON,"landmark":"Ganhi","phone":"$TEL_CLIENT","contactName":"Client Etape4"},
  "dropoff": {"latitude":$DROPOFF_LAT,"longitude":$DROPOFF_LON,"landmark":"Fidjrosse","phone":"$TEL_CLIENT","contactName":"Destinataire Etape4"},
  "recipientName": "Destinataire Etape4",
  "recipientPhone": "$TEL_CLIENT",
  "packageDescription": "Colis de test etape 4",
  "packageWeightGrams": 2000
}
JSON
)
REPONSE="$(post /api/client/v1/deliveries "$CORPS" "$CLIENT_TOKEN" "Idempotency-Key: etape4-$HORODATAGE")"
json_ou_echec "$REPONSE" "creation de la course" >/dev/null
COURSE="$(echo "$REPONSE" | jq -r '.delivery.id // empty')"
[ -n "$COURSE" ] || echouer "pas de course creee — $REPONSE"
vert "Course $COURSE creee, en attente de paiement."

# --------------------------------------------------------------- 7. Paiement --

etape "7. Attente du paiement (fournisseur factice)"
PAYEE="non"
for _ in $(seq 1 20); do
  sleep 2
  ETAT="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT_TOKEN" | jq -r '.status // empty')"
  printf '  statut : %s\n' "${ETAT:-?}"
  case "$ETAT" in
    3|4|6|DELIVERY_STATUS_PAID|DELIVERY_STATUS_SEARCHING_DRIVER|DELIVERY_STATUS_DRIVER_ASSIGNED|Paid|SearchingDriver|DriverAssigned)
      PAYEE="oui"; break ;;
    2|DELIVERY_STATUS_PAYMENT_FAILED|PaymentFailed)
      echouer "le paiement factice a echoue : verifiez LOOPBACK_OUTCOME." ;;
  esac
done
[ "$PAYEE" = "oui" ] || echouer "la course n'a jamais ete payee. Journaux : docker compose -f $PAYMENT_DIR/docker-compose.yml logs --tail 50 payment"
vert "Course payee."

# ----------------------------------------------------------------- 8. Offre --

etape "8. Attente de l'offre cote livreur (3 vagues, 90 s au plus)"
OFFRE=""
for _ in $(seq 1 50); do
  sleep 2
  REPONSE="$(get /api/driver/v1/offers/current "$DRIVER_TOKEN")"
  # GetCurrentOffer rend une offre VIDE quand il n'y en a pas : c'est la
  # reponse normale d'un livreur qui attend, pas une erreur.
  OFFRE="$(echo "$REPONSE" | jq -r 'select(.id != null and .id != "") | .id' 2>/dev/null || true)"
  [ -n "$OFFRE" ] && break
  printf '.'
done
printf '\n'
[ -n "$OFFRE" ] || echouer "aucune offre recue. C'est ici que se voit un jeton de service manquant : docker compose -f src/Services/Dispatch/docker-compose.yml logs --tail 80 dispatch"
vert "Offre recue : $OFFRE"

# ----------------------------------------------------------- 9. Acceptation --

etape "9. Acceptation de l'offre"
REPONSE="$(post "/api/driver/v1/offers/$OFFRE/accept" '{}' "$DRIVER_TOKEN" "Idempotency-Key: accept-$OFFRE")"
json_ou_echec "$REPONSE" "acceptation" >/dev/null
echo "$REPONSE" | jq -c .

# --------------------------------------------------------- 10. Verification --

etape "10. La boucle est-elle fermee ?"
AFFECTEE="non"
for _ in $(seq 1 15); do
  sleep 2
  VUE="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT_TOKEN")"
  ETAT="$(echo "$VUE" | jq -r '.status // empty')"
  printf '  statut : %s\n' "${ETAT:-?}"
  case "$ETAT" in
    6|DELIVERY_STATUS_DRIVER_ASSIGNED|DriverAssigned) AFFECTEE="oui"; break ;;
  esac
done

if [ "$AFFECTEE" != "oui" ]; then
  rouge "La course n'est pas passee en DRIVER_ASSIGNED."
  rouge "Regardez les journaux de Delivery : c'est lui qui consomme OfferAccepted."
  rouge "  docker compose -f src/Services/Delivery/docker-compose.yml logs --tail 80 delivery"
  exit 1
fi

echo
vert "BOUCLE FERMEE."
echo "$VUE" | jq '{id, status, driver}'
echo
bleu "Course  : $COURSE"
bleu "Livreur : $DRIVER_ID"
