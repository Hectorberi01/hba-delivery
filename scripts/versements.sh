#!/usr/bin/env bash
#
# LE VERSEMENT D'UN LIVREUR, DE LA COURSE LIVREE AU DEBIT DU GRAND LIVRE.
#
# CE QUE CE SCRIPT PROUVE, ET QUI NE SE VOIT NULLE PART AILLEURS : le compte du
# livreur NE BOUGE PAS a l'approbation. Il bouge quand la finance consigne la
# reference du virement, et pas avant. Entre les deux, le script relit le
# releve et verifie que « deja verse » vaut encore zero — c'est la seule
# maniere de montrer qu'« approuve » et « verse » ne sont pas la meme chose.
#
# Il verifie aussi les refus : demander plus que le disponible, demander deux
# fois, refuser sans motif, consigner sans reference, sauter l'approbation,
# refuser une demande deja approuvee, et un livreur qui essaie d'instruire la
# file de la finance.
#
#   ./scripts/versements.sh
#   SKIP_BUILD=1 ./scripts/versements.sh
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

# Ganhi, comme les autres scenarios. Le livreur est place SUR le retrait : ici
# on ne teste pas le moteur, on veut une course livree le plus vite possible.
PICKUP_LAT="6.3654";  PICKUP_LON="2.4183"
DROPOFF_LAT="6.3520"; DROPOFF_LON="2.3810"

vert()  { printf '  \033[1;32m✓\033[0m %s\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
jaune() { printf '  \033[1;33m%s\033[0m\n' "$*"; }
bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }
echouer() { rouge "ECHEC : $*"; exit 1; }

attendu() {
  if [ "$2" = "$3" ]; then
    printf '  \033[1;32m✓\033[0m %-52s %s\n' "$1" "$3"
  else
    printf '  \033[1;31m✗\033[0m %-52s attendu «%s», obtenu «%s»\n' "$1" "$2" "$3" >&2
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

# Code HTTP seul, corps dans $TEMPO/sonde.json. Sert aux refus : c'est le code
# qui distingue « regle metier » de « route absente ».
#
# TOUT REFUS METIER SORT EN 409, ET CE N'EST PAS UNE ERREUR DE CE SCRIPT. La
# passerelle traduit FailedPrecondition en 409, et l'intercepteur des services
# y envoie toute DomainException qui n'est ni « introuvable », ni « interdit »,
# ni « validation ». « Montant superieur au disponible » arrive donc en 409, la
# ou un 422 se lirait mieux. C'est le code lui-meme — PAYOUT_EXCEEDS_BALANCE —
# qui porte le sens, et c'est lui qu'on verifie ensuite.
code_post() {
  curl -sS -o "$TEMPO/sonde.json" -w '%{http_code}' -X POST "$GATEWAY$1" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $3" -d "$2"
}

# --------------------------------------------------------- Images a jour --

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  etape "0. Construction (SKIP_BUILD=1 pour sauter)"
  for service in Identity Driver Dispatch Payment Delivery; do
    printf '  %s…\n' "$service"
    docker compose -f "src/Services/$service/docker-compose.yml" build >/dev/null \
      || echouer "la construction de $service a echoue."
  done
  printf '  Gateway…\n'
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build >/dev/null \
    || echouer "la construction de la gateway a echoue."

  docker compose -f src/Services/Driver/docker-compose.yml up -d --force-recreate >/dev/null
  docker compose -f src/Services/Dispatch/docker-compose.yml up -d --force-recreate >/dev/null
  docker compose -f src/Services/Delivery/docker-compose.yml up -d --force-recreate >/dev/null
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

# ------------------------------------------------------------- 1. Routes --

etape "1. Les routes existent-elles ?"
# Sans jeton, une route qui existe rend 401 ; une route absente rend 404. Une
# image de passerelle perimee se voit ici, pas trente etapes plus loin.
for sonde in \
  "GET:/api/admin/v1/payouts" \
  "POST:/api/admin/v1/payouts/00000000-0000-0000-0000-000000000000/approve" \
  "POST:/api/admin/v1/payouts/00000000-0000-0000-0000-000000000000/reject" \
  "POST:/api/admin/v1/payouts/00000000-0000-0000-0000-000000000000/paid" \
  "POST:/api/driver/v1/earnings/payouts" \
  "GET:/api/admin/v1/drivers/x/earnings"
do
  METHODE="${sonde%%:*}"; ROUTE="${sonde#*:}"
  if [ "$METHODE" = "GET" ]; then
    CODE="$(curl -sS -o /dev/null -w '%{http_code}' "$GATEWAY$ROUTE")"
  else
    CODE="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$GATEWAY$ROUTE" \
      -H 'Content-Type: application/json' -d '{}')"
  fi
  [ "$CODE" != "404" ] || echouer "$METHODE $ROUTE est absente : image de passerelle perimee."
  attendu "$METHODE $ROUTE" "401" "$CODE"
done

# ---------------------------------------------------------- 2. Comptes --

etape "2. Administrateur"
ADMIN="$(post /api/web/v1/auth/login \
  "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"versements\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$ADMIN" ] || echouer "Identity refuse $ADMIN_EMAIL."
vert "Connecte."

creer_compte() {
  local canal="$1" tel="$2" nom="$3" defi reponse
  defi="$(post "/api/$canal/v1/auth/otp/request" "{\"phone\":\"$tel\",\"deviceId\":\"versements\"}" \
    | jq -r '.challengeId // empty')"
  [ -n "$defi" ] || echouer "pas de defi OTP pour $tel."

  reponse="$(post "/api/$canal/v1/auth/otp/verify" \
    "{\"challengeId\":\"$defi\",\"code\":\"000000\",\"deviceId\":\"versements\",\"displayName\":\"$nom\"}")"
  echo "$reponse" | jq -r '.accessToken // empty' > "$TEMPO/jeton"
  echo "$reponse" | jq -r '.principal.driverId // empty' > "$TEMPO/id"
  [ -s "$TEMPO/jeton" ] || echouer "pas de jeton pour $tel — $reponse"
}

etape "3. Un client et un livreur"
creer_compte client "+2290193${HORODATAGE: -6}" "Client Versement"
CLIENT="$(cat "$TEMPO/jeton")"
TEL_CLIENT="+2290193${HORODATAGE: -6}"

creer_compte driver "+2290194${HORODATAGE: -6}" "Livreur Versement"
LIVREUR="$(cat "$TEMPO/jeton")"; LIVREUR_ID="$(cat "$TEMPO/id")"
[ -n "$LIVREUR_ID" ] || echouer "le jeton livreur ne porte pas driver_id."
vert "Livreur $LIVREUR_ID"

bleu "Attente de la creation du profil par le service Driver (Kafka)…"
sleep 8

etape "4. Dossier valide, livreur en ligne sur le retrait"
attendu "Dossier valide" "2" \
  "$(post "/api/admin/v1/drivers/$LIVREUR_ID/kyc" '{"approved":true}' "$ADMIN" | jq -r '.verificationStatus // empty')"
attendu "Livreur disponible" "2" \
  "$(post /api/driver/v1/presence/online "{\"latitude\":$PICKUP_LAT,\"longitude\":$PICKUP_LON}" "$LIVREUR" | jq -r '.operationalStatus // empty')"

etape "5. Le compte est vide au depart"
RELEVE="$(get /api/driver/v1/earnings "$LIVREUR")"
attendu "Gagne" "0" "$(echo "$RELEVE" | jq -r '.earnedXof // 0')"
attendu "Deja verse" "0" "$(echo "$RELEVE" | jq -r '.paidOutXof // 0')"
attendu "Reste du" "0" "$(echo "$RELEVE" | jq -r '.dueXof // 0')"

# UN COMPTE VIDE NE PEUT RIEN VERSER, et c'est le premier refus a verifier :
# si celui-la passait, tous les autres controles seraient decoratifs.
CODE="$(code_post /api/driver/v1/earnings/payouts '{"amountXof":1000}' "$LIVREUR")"
attendu "Demander sur un compte vide est refuse" "409" "$CODE"
attendu "Code metier" "PAYOUT_EXCEEDS_BALANCE" "$(jq -r '.code // empty' "$TEMPO/sonde.json")"

# ------------------------------------------------------- 6. Une course --

etape "6. Devis, course, paiement"
DEVIS="$(post /api/client/v1/quotes \
  "{\"pickupLatitude\":$PICKUP_LAT,\"pickupLongitude\":$PICKUP_LON,\"dropoffLatitude\":$DROPOFF_LAT,\"dropoffLongitude\":$DROPOFF_LON,\"packageWeightGrams\":2000}" \
  "$CLIENT")"
DEVIS_ID="$(echo "$DEVIS" | jq -r '.quoteId // empty')"
[ -n "$DEVIS_ID" ] || echouer "pas de devis."

# LA PART DU LIVREUR NE SE LIT PAS SUR LE DEVIS DU CLIENT, et c'est normal :
# la route des devis ne la rend pas. Le client paie un prix, ce que touche le
# livreur ne le regarde pas. Le chiffre sera releve a l'etape 7, sur l'OFFRE —
# c'est-a-dire sur ce que le livreur avait sous les yeux en acceptant, qui est
# precisement ce qui doit se retrouver au grand livre.

CORPS=$(cat <<JSON
{
  "quoteId": "$DEVIS_ID",
  "pickup":  {"latitude":$PICKUP_LAT,"longitude":$PICKUP_LON,"landmark":"Ganhi","phone":"$TEL_CLIENT","contactName":"Client Versement"},
  "dropoff": {"latitude":$DROPOFF_LAT,"longitude":$DROPOFF_LON,"landmark":"Fidjrosse","phone":"$TEL_CLIENT","contactName":"Destinataire Versement"},
  "recipientName": "Destinataire Versement",
  "recipientPhone": "$TEL_CLIENT",
  "packageDescription": "Colis versement",
  "packageWeightGrams": 2000
}
JSON
)
COURSE="$(post /api/client/v1/deliveries "$CORPS" "$CLIENT" "Idempotency-Key: versement-$HORODATAGE" \
  | jq -r '.delivery.id // empty')"
[ -n "$COURSE" ] || echouer "pas de course creee."
vert "Course $COURSE"

etape "7. Le livreur accepte, roule, et remet le colis"
OFFRE=""
for _ in $(seq 1 30); do
  sleep 3
  OFFRE="$(get /api/driver/v1/offers/current "$LIVREUR" | jq -r '.offer.id // empty')"
  [ -n "$OFFRE" ] && break
done
[ -n "$OFFRE" ] || echouer "aucune offre n'est arrivee au livreur en 90 s."
vert "Offre $OFFRE"

# CE QUE LE LIVREUR AVAIT SOUS LES YEUX. Ce montant est fige au devis et
# voyage avec l'evenement de remise ; il devra se retrouver a l'identique au
# grand livre. Un recalcul au moment de la remise ferait dependre la paie
# d'une grille qui a pu changer, alors qu'il avait accepte sur ce chiffre.
PART="$(get /api/driver/v1/offers/current "$LIVREUR" | jq -r '.offer.driverEarningXof // empty')"
if [ -n "$PART" ] && [ "$PART" != "0" ]; then
  vert "Part annoncee au livreur : $PART F"
else
  PART=""
  jaune "L'offre ne porte pas la part du livreur : la verification portera sur le releve seul."
fi

post "/api/driver/v1/offers/$OFFRE/accept" '{}' "$LIVREUR" >/dev/null
sleep 4

post "/api/driver/v1/missions/$COURSE/arrived" '{}' "$LIVREUR" "Idempotency-Key: arr-$HORODATAGE" >/dev/null
post "/api/driver/v1/missions/$COURSE/picked-up" '{}' "$LIVREUR" "Idempotency-Key: pic-$HORODATAGE" >/dev/null

# L'OTP VIENT DE LA VUE DU CLIENT, jamais de celle du livreur ni de l'admin :
# c'est la seule ligne de la matrice de visibilite ou l'admin voit moins que
# tout le monde. Ce script detient le jeton du client de test, il a donc le
# droit de le lire. Aucune regle n'est levee.
OTP="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT" | jq -r '.deliveryOtp // empty')"
[ -n "$OTP" ] || echouer "le client ne voit pas le code de remise."

post "/api/driver/v1/missions/$COURSE/deliver" "{\"otp\":\"$OTP\"}" "$LIVREUR" \
  "Idempotency-Key: liv-$HORODATAGE" >/dev/null
vert "Colis remis."

# ------------------------------------------------ 8. Le credit arrive seul --

etape "8. Le credit arrive par l'evenement, pas par un appel"
bleu "Attente de DeliveryCompleted chez Payment (Kafka)…"
GAGNE="0"
for _ in $(seq 1 20); do
  sleep 3
  GAGNE="$(get /api/driver/v1/earnings "$LIVREUR" | jq -r '.earnedXof // 0')"
  printf '  gagne : %s F\n' "$GAGNE"
  [ "$GAGNE" != "0" ] && break
done
[ "$GAGNE" != "0" ] || echouer "aucun credit n'a ete ecrit : le consommateur de Payment n'a pas vu la course livree."

RELEVE="$(get /api/driver/v1/earnings "$LIVREUR")"
attendu "Une seule ligne au compte" "1" "$(echo "$RELEVE" | jq -r '.totalEntries // 0')"
attendu "Sens de la ligne" "LEDGER_DIRECTION_CREDIT" "$(echo "$RELEVE" | jq -r '.entries[0].direction // empty')"
attendu "Nature" "LEDGER_ENTRY_KIND_DELIVERY_EARNING" "$(echo "$RELEVE" | jq -r '.entries[0].kind // empty')"
attendu "Reste du = gagne" "$GAGNE" "$(echo "$RELEVE" | jq -r '.dueXof // 0')"
if [ -n "$PART" ]; then
  attendu "Le credit est la part figee au devis" "$PART" "$GAGNE"
fi

# ------------------------------------------------------- 9. La demande --

etape "9. Le livreur demande, et rien d'autre ne peut demander pour lui"
CODE="$(code_post /api/driver/v1/earnings/payouts "{\"amountXof\":$((GAGNE + 1))}" "$LIVREUR")"
attendu "Au-dela du disponible : refuse" "409" "$CODE"
attendu "Code metier" "PAYOUT_EXCEEDS_BALANCE" "$(jq -r '.code // empty' "$TEMPO/sonde.json")"

DEMANDE="$(post /api/driver/v1/earnings/payouts "{\"amountXof\":$GAGNE}" "$LIVREUR")"
DEMANDE_ID="$(echo "$DEMANDE" | jq -r '.id // empty')"
[ -n "$DEMANDE_ID" ] || echouer "la demande n'a pas ete creee — $DEMANDE"
attendu "Etat" "PAYOUT_STATUS_REQUESTED" "$(echo "$DEMANDE" | jq -r '.status // empty')"
attendu "Montant fige" "$GAGNE" "$(echo "$DEMANDE" | jq -r '.amountXof // 0')"
vert "Demande $DEMANDE_ID"

CODE="$(code_post /api/driver/v1/earnings/payouts "{\"amountXof\":$GAGNE}" "$LIVREUR")"
attendu "Deuxieme demande en cours : refusee" "409" "$CODE"
attendu "Code metier" "PAYOUT_ALREADY_PENDING" "$(jq -r '.code // empty' "$TEMPO/sonde.json")"

attendu "Le compte n'a pas bouge (demander n'est pas recevoir)" "$GAGNE" \
  "$(get /api/driver/v1/earnings "$LIVREUR" | jq -r '.dueXof // 0')"

# ---------------------------------------------- 10. La file de la finance --

etape "10. La file du back-office"
FILE="$(get "/api/admin/v1/payouts?limit=100" "$ADMIN")"
attendu "La demande est dans la file d'attente" "1" \
  "$(echo "$FILE" | jq -r --arg id "$DEMANDE_ID" '[.payouts[] | select(.id == $id)] | length')"
attendu "Le livreur y est nomme" "$LIVREUR_ID" \
  "$(echo "$FILE" | jq -r --arg id "$DEMANDE_ID" '.payouts[] | select(.id == $id) | .driverId')"

# UN LIVREUR N'INSTRUIT PAS LA FILE. Verifie avec SON jeton : la politique de
# groupe de la passerelle le rejette, et le service le rejetterait aussi.
CODE="$(curl -sS -o /dev/null -w '%{http_code}' "$GATEWAY/api/admin/v1/payouts" \
  -H "Authorization: Bearer $LIVREUR")"
attendu "Un livreur ne lit pas la file" "403" "$CODE"

CODE="$(code_post "/api/admin/v1/payouts/$DEMANDE_ID/approve" '{}' "$LIVREUR")"
attendu "Un livreur n'approuve pas" "403" "$CODE"

etape "11. Refuser sans motif, verser sans reference : les deux sont refuses"
CODE="$(code_post "/api/admin/v1/payouts/$DEMANDE_ID/reject" '{"reason":"  "}' "$ADMIN")"
attendu "Refus sans motif" "400" "$CODE"
attendu "Code" "MISSING_REJECTION_REASON" "$(jq -r '.code // empty' "$TEMPO/sonde.json")"

CODE="$(code_post "/api/admin/v1/payouts/$DEMANDE_ID/paid" '{"paymentReference":""}' "$ADMIN")"
attendu "Versement sans reference" "400" "$CODE"
attendu "Code" "MISSING_PAYMENT_REFERENCE" "$(jq -r '.code // empty' "$TEMPO/sonde.json")"

# PAS DE RACCOURCI « DEMANDEE → VERSEE ». La machine a etats l'interdit, et
# c'est ce qui garantit qu'un virement a toujours ete decide avant d'etre paye.
CODE="$(code_post "/api/admin/v1/payouts/$DEMANDE_ID/paid" '{"paymentReference":"RACCOURCI"}' "$ADMIN")"
attendu "Verser sans approuver" "409" "$CODE"
attendu "Le compte n'a pas bouge" "$GAGNE" \
  "$(get /api/driver/v1/earnings "$LIVREUR" | jq -r '.dueXof // 0')"

# --------------------------------------- 12. LE COEUR : approuver ne paie pas --

etape "12. Approuver n'ecrit RIEN au grand livre"
APPROUVEE="$(post "/api/admin/v1/payouts/$DEMANDE_ID/approve" '{}' "$ADMIN")"
attendu "Etat" "PAYOUT_STATUS_APPROVED" "$(echo "$APPROUVEE" | jq -r '.status // empty')"
[ "$(echo "$APPROUVEE" | jq -r '.decidedBy // empty')" != "" ] \
  || echouer "l'approbation ne dit pas qui a decide."
vert "Approuvee par $(echo "$APPROUVEE" | jq -r '.decidedBy')"

RELEVE="$(get /api/driver/v1/earnings "$LIVREUR")"
attendu "Deja verse : TOUJOURS zero" "0" "$(echo "$RELEVE" | jq -r '.paidOutXof // 0')"
attendu "Reste du : inchange" "$GAGNE" "$(echo "$RELEVE" | jq -r '.dueXof // 0')"
attendu "Toujours une seule ligne au compte" "1" "$(echo "$RELEVE" | jq -r '.totalEntries // 0')"
jaune "C'est le point de tout ce chantier : l'accord ne vaut pas l'argent."

CODE="$(code_post "/api/admin/v1/payouts/$DEMANDE_ID/reject" '{"reason":"trop tard"}' "$ADMIN")"
attendu "Refuser une approuvee : refuse" "409" "$CODE"
jaune "Consequence connue, point 15 des points a trancher : rien ne sort d'une approbation."

# --------------------------------------- 13. Consigner le virement : le debit --

etape "13. Consigner la reference ecrit le debit"
REFERENCE="VIR-$HORODATAGE"
VERSEE="$(post "/api/admin/v1/payouts/$DEMANDE_ID/paid" "{\"paymentReference\":\"$REFERENCE\"}" "$ADMIN")"
attendu "Etat" "PAYOUT_STATUS_PAID" "$(echo "$VERSEE" | jq -r '.status // empty')"
attendu "Reference consignee" "$REFERENCE" "$(echo "$VERSEE" | jq -r '.paymentReference // empty')"

RELEVE="$(get /api/driver/v1/earnings "$LIVREUR")"
attendu "Deux lignes au compte" "2" "$(echo "$RELEVE" | jq -r '.totalEntries // 0')"
attendu "Deja verse" "$GAGNE" "$(echo "$RELEVE" | jq -r '.paidOutXof // 0')"
attendu "Reste du" "0" "$(echo "$RELEVE" | jq -r '.dueXof // 0')"
attendu "Sens de la ligne la plus recente" "LEDGER_DIRECTION_DEBIT" \
  "$(echo "$RELEVE" | jq -r '.entries[0].direction // empty')"
attendu "Nature" "LEDGER_ENTRY_KIND_PAYOUT" "$(echo "$RELEVE" | jq -r '.entries[0].kind // empty')"

# LE DEBIT PORTE L'ORIGINE, et c'est ce qui empeche de payer deux fois : un
# index unique en base s'appuie sur ce champ.
attendu "Le debit designe la demande" "$DEMANDE_ID" \
  "$(echo "$RELEVE" | jq -r '.entries[0].payoutId // empty')"

CODE="$(code_post "/api/admin/v1/payouts/$DEMANDE_ID/paid" "{\"paymentReference\":\"$REFERENCE-BIS\"}" "$ADMIN")"
attendu "Consigner deux fois : refuse" "409" "$CODE"
attendu "Toujours deux lignes" "2" "$(get /api/driver/v1/earnings "$LIVREUR" | jq -r '.totalEntries // 0')"

etape "14. Ce que le livreur lit de sa propre demande"
# CES TROIS LECTURES SONT CELLES DE L'ECRAN GAINS. Si l'une tombe, l'ecran
# n'affiche pas une valeur fausse : il affiche un tiret ou une carte d'erreur,
# ce qui est pire a diagnostiquer qu'un echec ici.
MIENNES="$(get "/api/driver/v1/earnings/payouts" "$LIVREUR")"
attendu "Sa demande figure dans sa liste" "1" \
  "$(echo "$MIENNES" | jq -r --arg id "$DEMANDE_ID" '[.payouts[] | select(.id == $id)] | length')"
attendu "Etat rendu" "PAYOUT_STATUS_PAID" \
  "$(echo "$MIENNES" | jq -r --arg id "$DEMANDE_ID" '.payouts[] | select(.id == $id) | .status')"
attendu "Il voit la reference du virement" "$REFERENCE" \
  "$(echo "$MIENNES" | jq -r --arg id "$DEMANDE_ID" '.payouts[] | select(.id == $id) | .paymentReference')"

# LE LIVREUR NE VOIT PAS QUI A DECIDE, et c'est la passerelle qui tranche :
# l'identifiant d'un compte interne ne lui apprend rien et n'a pas a sortir.
attendu "Il ne voit pas qui a decide" "null" \
  "$(echo "$MIENNES" | jq -r --arg id "$DEMANDE_ID" '.payouts[] | select(.id == $id) | .decidedBy // "null"')"

etape "15. La demande sort de la file, et la place est libre"
FILE="$(get "/api/admin/v1/payouts?limit=100" "$ADMIN")"
attendu "Plus dans la file d'attente" "0" \
  "$(echo "$FILE" | jq -r --arg id "$DEMANDE_ID" '[.payouts[] | select(.id == $id)] | length')"
attendu "Presente dans l'historique des versees" "1" \
  "$(get "/api/admin/v1/payouts?status=PAID&limit=100" "$ADMIN" \
     | jq -r --arg id "$DEMANDE_ID" '[.payouts[] | select(.id == $id)] | length')"
attendu "Un etat inconnu est une erreur, pas « tout »" "400" \
  "$(curl -sS -o /dev/null -w '%{http_code}' "$GATEWAY/api/admin/v1/payouts?status=VERSEE" \
     -H "Authorization: Bearer $ADMIN")"

etape "16. Le releve du back-office dit la meme chose que celui du livreur"
VU_FINANCE="$(get "/api/admin/v1/drivers/$LIVREUR_ID/earnings" "$ADMIN")"
attendu "Gagne" "$GAGNE" "$(echo "$VU_FINANCE" | jq -r '.earnedXof // 0')"
attendu "Deja verse" "$GAGNE" "$(echo "$VU_FINANCE" | jq -r '.paidOutXof // 0')"
attendu "Reste du" "0" "$(echo "$VU_FINANCE" | jq -r '.dueXof // 0')"

etape "17. Le compte est solde : plus rien a demander"
CODE="$(code_post /api/driver/v1/earnings/payouts '{"amountXof":1}' "$LIVREUR")"
attendu "Demander apres versement complet" "409" "$CODE"
attendu "Code metier" "PAYOUT_EXCEEDS_BALANCE" "$(jq -r '.code // empty' "$TEMPO/sonde.json")"

printf '\n\033[1;32mTout est verifie.\033[0m\n'
printf '  Course   %s\n' "$COURSE"
printf '  Livreur  %s\n' "$LIVREUR_ID"
printf '  Demande  %s  (%s F, reference %s)\n' "$DEMANDE_ID" "$GAGNE" "$REFERENCE"
printf '\n  Console : %s/versements\n' "${CONSOLE:-http://localhost:3000}"
