#!/usr/bin/env bash
#
# LE DISPATCH SUR LE TERRAIN : une vraie course, un vrai telephone.
#
# Cree une course payee au carrefour des Quatre-Chemins (Aubervilliers) et
# regarde si TON livreur recoit la proposition sur son telephone.
#
# CE SCRIPT NE FAIT PAS PASSER LE LIVREUR EN LIGNE, ET C'EST VOULU : c'est
# exactement ce qu'on teste. Le passage en ligne se fait depuis l'application,
# avec le GPS du telephone. Si aucune position recente n'est connue, le script
# s'arrete et le dit, au lieu d'en fabriquer une qui ne prouverait rien.
#
# IL MESURE D'ABORD, IL CHOISIT ENSUITE. La distance entre le livreur et le
# retrait decide du chemin teste :
#   - dans les 6 km : le MOTEUR doit le trouver, par vagues de 2, 4 puis 6 km.
#     On le laisse travailler et on regarde les offres arriver.
#   - au-dela : aucune vague ne l'atteindra jamais. On pose alors l'offre A LA
#     MAIN, tout de suite — attendre l'echec des trois vagues fermerait la
#     recherche et rendrait l'offre manuelle impossible.
#
#   ./scripts/dispatch-terrain.sh                 # livreur par defaut
#   ./scripts/dispatch-terrain.sh +33612345678    # un autre numero
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
IDENTITY_DIR="src/Services/Identity"
PAYMENT_DIR="src/Services/Payment"
COMPOSE_INFRA="deploy/docker-compose.yml"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"

TEL_LIVREUR="${1:-+33618710738}"
HORODATAGE="$(date +%s)"

# Carrefour des Quatre-Chemins, a la limite d'Aubervilliers et de Pantin
# (station Aubervilliers–Pantin Quatre Chemins).
#
# CORRIGE LE 27 SEPTEMBRE 2026 : la valeur precedente, 48.8925, tombait dans
# le parc de la Villette, deux cents metres plus au sud. Le repere ecrit
# disait « Quatre-Chemins » et le point disait « Jardin des Bambous » ; c'est
# le point qui part a l'application de navigation, donc c'est lui qui gagne.
# Un libelle et des coordonnees qui se contredisent font douter du bon
# composant — ici, on a soupconne le bouton « Y aller », qui n'y etait pour
# rien.
#
# Position approchee de la station, pas un releve : ce qui compte pour le
# test est que le point et le repere designent le meme endroit.
RETRAIT_LAT="48.8944"; RETRAIT_LON="2.3897"
RETRAIT_REPERE="Quatre-Chemins, avenue Jean Jaures"

# Mairie d'Aubervilliers : environ 2,3 km, une course courte et credible.
DEPOT_LAT="48.9128";  DEPOT_LON="2.3819"
DEPOT_REPERE="Mairie d'Aubervilliers"

# Les trois rayons du moteur, tels que DispatchOptions les fixe par defaut.
DERNIER_RAYON=6000

vert()  { printf '  \033[1;32m✓\033[0m %s\n' "$*"; }
jaune() { printf '  \033[1;33m•\033[0m %s\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }
echouer() { rouge "ECHEC : $*"; exit 1; }

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

sql() {
  docker compose -f "$COMPOSE_INFRA" exec -T postgres \
    psql -tAq -U "${POSTGRES_USER:-hba}" -d "$1" -c "$2" 2>/dev/null
}

# Distance orthodromique en metres, sur le rayon terrestre de Redis GEO — le
# meme que celui du service, pour que les deux chiffres concordent.
distance() {
  awk -v a="$1" -v b="$2" -v c="$3" -v d="$4" 'BEGIN{
    r = 6372797.560856; p = atan2(0, -1) / 180;
    dp = (c - a) * p; dl = (d - b) * p;
    h = sin(dp / 2) ^ 2 + cos(a * p) * cos(c * p) * sin(dl / 2) ^ 2;
    if (h > 1) h = 1;
    printf "%d", 2 * r * atan2(sqrt(h), sqrt(1 - h));
  }'
}

# shellcheck disable=SC2317  # appelee par le trap EXIT, pas en ligne droite.
restaurer() {
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" up -d --force-recreate identity >/dev/null 2>&1 || true
  docker compose -f "$PAYMENT_DIR/docker-compose.yml"  up -d --force-recreate payment  >/dev/null 2>&1 || true
  rm -rf "$TEMPO"
}
trap restaurer EXIT

# ------------------------------------------------- 0. Modes de developpement --

# IDENTITY EN CODE FIXE SERT AU CLIENT, PAS AU LIVREUR. Il faut un compte
# client pour passer commande, et on ne va pas faire recevoir un SMS a
# quelqu'un pour ca. Le livreur, lui, est deja inscrit : son jeton est signe
# par la cle montee, un redemarrage d'Identity ne le casse pas.
etape "0. Identity en code OTP fixe, Payment en paiement factice"
docker compose -f "$IDENTITY_DIR/docker-compose.yml" -f "$IDENTITY_DIR/compose.dev-otp.yml" \
  up -d --force-recreate identity >/dev/null
docker compose -f "$PAYMENT_DIR/docker-compose.yml" -f "$PAYMENT_DIR/compose.dev-loopback.yml" \
  up -d --force-recreate payment >/dev/null
bleu "Attente du redemarrage…"
sleep 12

# LA ROUTE D'ABORD, SANS JETON. Elle sert de sonde a l'etape 6, et un 404
# venu d'une image de passerelle perimee y serait indistinguable d'un 404
# « la recherche n'existe pas encore » : on aurait cherche pendant quarante
# secondes une recherche absente alors que c'est la route qui manque.
etape "1. La route d'offre existe-t-elle ?"
CODE_ROUTE="$(curl -sS -o /dev/null -w '%{http_code}' -X POST \
  "$GATEWAY/api/admin/v1/deliveries/00000000-0000-0000-0000-000000000000/offer" \
  -H 'Content-Type: application/json' -d '{"driverId":"x"}')"
if [ "$CODE_ROUTE" = "404" ]; then
  rouge "POST /api/admin/v1/deliveries/{id}/offer est absente de la passerelle."
  rouge "Reconstruis les images avant de tester :"
  rouge "  docker compose -f src/Services/Dispatch/docker-compose.yml build"
  rouge "  docker compose -f src/Services/Driver/docker-compose.yml build"
  rouge "  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build"
  exit 1
fi
vert "Presente (HTTP $CODE_ROUTE sans jeton)."

etape "2. Administrateur"
ADMIN="$(post /api/web/v1/auth/login \
  "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"terrain\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$ADMIN" ] || echouer "Identity refuse $ADMIN_EMAIL."
vert "Connecte."

# ------------------------------------------------------------ 3. Le livreur --

etape "3. Le livreur $TEL_LIVREUR"
get "/api/admin/v1/drivers?pageSize=200" "$ADMIN" > "$TEMPO/annuaire.json"
jq -e '.drivers' "$TEMPO/annuaire.json" >/dev/null 2>&1 \
  || echouer "annuaire illisible — $(cat "$TEMPO/annuaire.json")"

TROUVE="$(jq --arg t "$TEL_LIVREUR" '[.drivers[] | select((.phone // "") == $t)]' "$TEMPO/annuaire.json")"
NOMBRE="$(echo "$TROUVE" | jq 'length')"

if [ "$NOMBRE" != "1" ]; then
  rouge "« $TEL_LIVREUR » designe $NOMBRE livreur(s). Livreurs connus :"
  jq -r '.drivers[] | "  \(.phone // "?")  \(.displayName // "sans nom")"' "$TEMPO/annuaire.json" >&2
  exit 1
fi

LIVREUR_ID="$(echo "$TROUVE" | jq -r '.[0].id')"
LIVREUR_NOM="$(echo "$TROUVE" | jq -r '.[0].displayName // "sans nom"')"
VERIF="$(echo "$TROUVE" | jq -r '.[0].verificationStatus // 0')"
vert "$LIVREUR_NOM — $LIVREUR_ID"

# 2 = VERIFIED. Sans dossier valide, il ne peut ni passer en ligne ni recevoir
# d'offre : autant le dire maintenant que le decouvrir dans le silence.
if [ "$VERIF" != "2" ]; then
  jaune "Dossier non valide (statut $VERIF) — validation…"
  NOUVEAU="$(post "/api/admin/v1/drivers/$LIVREUR_ID/kyc" '{"approved":true}' "$ADMIN" \
    | jq -r '.verificationStatus // empty')"
  [ "$NOUVEAU" = "2" ] || echouer "le service n'a pas valide le dossier (statut rendu : ${NOUVEAU:-aucun})."
  vert "Dossier valide. Tire l'accueil de l'application vers le bas pour le relire."
else
  vert "Dossier deja valide."
fi

# ------------------------------------------------- 4. Ou est-il, maintenant --

etape "4. Sa position, et ce qu'elle implique"
get /api/admin/v1/drivers/positions "$ADMIN" > "$TEMPO/positions.json"
ICI="$(jq --arg id "$LIVREUR_ID" '[.positions[] | select(.driverId == $id)][0] // empty' "$TEMPO/positions.json")"

if [ -z "$ICI" ]; then
  rouge "Aucune position recente pour $LIVREUR_NOM."
  rouge ""
  rouge "C'EST LA CONDITION DU TEST, PAS UNE PANNE. Le moteur ne propose une"
  rouge "course qu'a un livreur DISPONIBLE dont le telephone a donne signe de"
  rouge "vie dans la fenetre de fraicheur."
  rouge ""
  rouge "  1. Ouvre l'application livreur sur le Samsung."
  rouge "  2. Passe le commutateur sur EN LIGNE, ecran d'accueil."
  rouge "  3. Verifie que la carte affiche ta position."
  rouge "  4. Relance : ./scripts/dispatch-terrain.sh $TEL_LIVREUR"
  exit 1
fi

LAT="$(echo "$ICI" | jq -r '.latitude')"
LON="$(echo "$ICI" | jq -r '.longitude')"
ETAT_OP="$(echo "$ICI" | jq -r '.operationalStatus // "?"')"
VU="$(echo "$ICI" | jq -r '.seenAt // "?"')"
ECART="$(distance "$RETRAIT_LAT" "$RETRAIT_LON" "$LAT" "$LON")"

vert "Position $LAT, $LON — vu a $VU"
vert "Etat operationnel : $ETAT_OP"
printf '  \033[1;32m✓\033[0m %-46s %s m\n' "Distance au retrait (Quatre-Chemins)" "$ECART"

if [ "$ETAT_OP" != "OPERATIONAL_STATUS_AVAILABLE" ]; then
  rouge "Il n'est pas DISPONIBLE mais $ETAT_OP."
  rouge "Le moteur ne s'adresse qu'aux livreurs disponibles, et l'offre"
  rouge "manuelle sera refusee pour la meme raison. Termine ou annule sa course"
  rouge "en cours, puis relance."
  exit 1
fi

if [ "$ECART" -le "$DERNIER_RAYON" ]; then
  CHEMIN="moteur"
  jaune "Il est dans les $DERNIER_RAYON m : LE MOTEUR doit le trouver seul."
else
  CHEMIN="manuel"
  jaune "Il est HORS des $DERNIER_RAYON m : aucune vague ne l'atteindra."
  jaune "On testera donc l'offre MANUELLE, posee tout de suite."
fi

# --------------------------------------------------- 5. Le client, la course --

etape "5. Un client de test, puis la course"
TEL_CLIENT="+2290199${HORODATAGE: -6}"
DEFI="$(post /api/client/v1/auth/otp/request "{\"phone\":\"$TEL_CLIENT\",\"deviceId\":\"terrain\"}" \
  | jq -r '.challengeId // empty')"
[ -n "$DEFI" ] || echouer "pas de defi OTP pour le client."

CLIENT="$(post /api/client/v1/auth/otp/verify \
  "{\"challengeId\":\"$DEFI\",\"code\":\"000000\",\"deviceId\":\"terrain\",\"displayName\":\"Client Aubervilliers\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$CLIENT" ] || echouer "pas de jeton client."
vert "Client $TEL_CLIENT"

bleu "Attente de la creation du profil par Directory (Kafka)…"
sleep 6

DEVIS_REPONSE="$(post /api/client/v1/quotes \
  "{\"pickupLatitude\":$RETRAIT_LAT,\"pickupLongitude\":$RETRAIT_LON,\"dropoffLatitude\":$DEPOT_LAT,\"dropoffLongitude\":$DEPOT_LON,\"packageWeightGrams\":1500}" \
  "$CLIENT")"
DEVIS="$(echo "$DEVIS_REPONSE" | jq -r '.quoteId // empty')"
PRIX="$(echo "$DEVIS_REPONSE" | jq -r '.totalXof // "?"')"

# UN DEVIS REFUSE ICI VEUT PROBABLEMENT DIRE « AUCUNE ZONE ». La resolution de
# zone est encore une valeur de repli configuree : vide, elle ne rend aucune
# zone, donc aucun prix — et cela vaut pour Cotonou comme pour Aubervilliers.
[ -n "$DEVIS" ] || echouer "pas de devis. Zones__FallbackZoneCode est-il renseigne ? — $DEVIS_REPONSE"
vert "Devis $DEVIS : $PRIX XOF"

CORPS=$(cat <<JSON
{
  "quoteId": "$DEVIS",
  "pickup":  {"latitude":$RETRAIT_LAT,"longitude":$RETRAIT_LON,"landmark":"$RETRAIT_REPERE","phone":"$TEL_CLIENT","contactName":"Client Aubervilliers"},
  "dropoff": {"latitude":$DEPOT_LAT,"longitude":$DEPOT_LON,"landmark":"$DEPOT_REPERE","phone":"$TEL_CLIENT","contactName":"Destinataire Aubervilliers"},
  "recipientName": "Destinataire Aubervilliers",
  "recipientPhone": "$TEL_CLIENT",
  "packageDescription": "Colis de test terrain",
  "packageWeightGrams": 1500
}
JSON
)
COURSE="$(post /api/client/v1/deliveries "$CORPS" "$CLIENT" "Idempotency-Key: terrain-$HORODATAGE" \
  | jq -r '.delivery.id // empty')"
[ -n "$COURSE" ] || echouer "pas de course creee."
vert "Course $COURSE"

etape "6. Paiement (fournisseur factice)"
PAYEE="non"
for _ in $(seq 1 25); do
  sleep 2
  ETAT="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT" | jq -r '.status // empty')"
  printf '  statut : %s\n' "${ETAT:-?}"
  case "$ETAT" in
    3|4|6) PAYEE="oui"; break ;;
    2) echouer "le paiement factice a echoue : verifiez LOOPBACK_OUTCOME." ;;
  esac
done
[ "$PAYEE" = "oui" ] || echouer "la course n'a jamais ete payee."
vert "Course payee. Dispatch ouvre la recherche."

# ------------------------------------------- 7. La recherche est-elle la ? --

# Tant que Dispatch n'a pas consomme DeliveryConfirmed, la route d'offre rend
# 404. Des qu'il a la recherche, elle rend un refus nomme pour un livreur
# inconnu. C'est la sonde la plus directe.
etape "7. La recherche est ouverte cote Dispatch"
PRETE="non"
for _ in $(seq 1 20); do
  CODE="$(curl -sS -o "$TEMPO/sonde.json" -w '%{http_code}' -X POST \
    "$GATEWAY/api/admin/v1/deliveries/$COURSE/offer" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $ADMIN" \
    -d '{"driverId":"11111111-1111-1111-1111-111111111111"}')"
  if [ "$CODE" = "200" ]; then PRETE="oui"; break; fi
  printf '  pas encore (HTTP %s)…\n' "$CODE"
  sleep 2
done
[ "$PRETE" = "oui" ] || echouer "Dispatch n'a jamais ouvert la recherche pour cette course."
vert "Recherche ouverte."

# ------------------------------------------------------- 8. L'offre manuelle --

if [ "$CHEMIN" = "manuel" ]; then
  etape "8. Offre posee a la main (il est hors de portee des vagues)"
  post "/api/admin/v1/deliveries/$COURSE/offer" \
    "{\"driverId\":\"$LIVREUR_ID\",\"reason\":\"test terrain Aubervilliers\"}" "$ADMIN" \
    > "$TEMPO/offre.json"

  if [ "$(jq -r '.sent' "$TEMPO/offre.json")" != "true" ]; then
    echouer "offre refusee : $(jq -r '.rejectionCode // "sans code"' "$TEMPO/offre.json")"
  fi
  vert "Offre envoyee, a $(jq -r '.offer.distanceToPickupMeters' "$TEMPO/offre.json") m du retrait."
fi

# ------------------------------------------------------ 9. Ce qui se passe --

etape "9. REGARDE TON TELEPHONE"
bleu "On suit les offres de cette course en base, toutes les trois secondes."
bleu "Statuts d'offre : 1 en attente, 2 acceptee, 3 refusee, 4 expiree, 5 caduque."
printf '\n'

OFFRES_VUES=""
ACCEPTEE="non"

for _ in $(seq 1 40); do
  LIGNES="$(sql hba_dispatch "
    SELECT o.driver_id || ' vague ' || o.wave_number || ' statut ' || o.status
      FROM dispatch.offers o
      JOIN dispatch.dispatches d ON d.id = o.dispatch_id
     WHERE d.delivery_id = '$COURSE'
     ORDER BY o.sent_at;" || true)"

  if [ -n "$LIGNES" ] && [ "$LIGNES" != "$OFFRES_VUES" ]; then
    printf '\033[1;34m  — offres —\033[0m\n'
    echo "$LIGNES" | sed 's/^/    /'
    OFFRES_VUES="$LIGNES"
  fi

  if echo "$LIGNES" | grep -q "statut 2"; then ACCEPTEE="oui"; break; fi

  ETAT="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT" | jq -r '.status // empty')"
  case "$ETAT" in
    5) jaune "La course est passee en AUCUN LIVREUR TROUVE."; break ;;
    6) ACCEPTEE="oui"; break ;;
  esac

  sleep 3
done

printf '\n'
etape "10. Verdict"

if [ "$ACCEPTEE" = "oui" ]; then
  VUE="$(get "/api/client/v1/deliveries/$COURSE" "$CLIENT")"
  vert "Course acceptee par $(echo "$VUE" | jq -r '.driver.displayName // "?"')."
  vert "Statut de la course : $(echo "$VUE" | jq -r '.status')  (6 = livreur affecte)"

  # LE CODE DE REMISE VIENT DE LA VUE DU CLIENT, ET C'EST TOUT L'INTERET.
  #
  # La matrice de visibilite le donne au client donneur d'ordre et au
  # destinataire, JAMAIS a l'admin ni au livreur — c'est la seule ligne du
  # tableau ou l'admin voit moins que tout le monde, et ce n'est pas un
  # oubli : l'OTP est ce qui distingue « le colis a ete remis » de
  # « quelqu'un a clique ». Ce script detient le jeton du client de test, il
  # a donc le droit de le lire. Aucune regle n'est levee pour l'afficher ici,
  # et rien de ce qui suit ne pourrait partir en production.
  OTP="$(echo "$VUE" | jq -r '.deliveryOtp // empty')"
  if [ -n "$OTP" ]; then
    printf '\n  \033[1;33mCODE DE REMISE : %s\033[0m\n' "$OTP"
    bleu "  A saisir dans l'application livreur au moment de la remise."
  else
    jaune "Pas de code de remise dans la reponse : la course est peut-etre deja close."
  fi

  printf '\n\033[1;32mLe livreur a recu la proposition et l'"'"'a prise.\033[0m\n'
  exit 0
fi

if [ -z "$OFFRES_VUES" ]; then
  rouge "AUCUNE OFFRE N'A ETE EMISE pour cette course."
  rouge ""
  rouge "Le livreur etait a $ECART m du retrait, disponible, avec une position"
  rouge "fraiche. Si le chemin teste etait « $CHEMIN », c'est un vrai defaut."
  rouge "Les journaux du planificateur diront quelle vague s'est ouverte :"
  rouge "  docker compose -f src/Services/Dispatch/docker-compose.yml logs --tail 80 dispatch"
  exit 1
fi

jaune "Des offres sont parties, mais aucune n'a ete acceptee :"
echo "$OFFRES_VUES" | sed 's/^/    /'
jaune ""
jaune "Rien d'anormal en soi : une offre non prise expire au bout du delai."
jaune "Si le telephone n'a rien affiche, c'est la notification qu'il faut"
jaune "regarder, pas le dispatch — l'offre, elle, est bien partie."
exit 0
