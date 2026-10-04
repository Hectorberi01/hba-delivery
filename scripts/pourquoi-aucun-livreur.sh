#!/usr/bin/env bash
#
# POURQUOI LA CARTE DU CLIENT DIT « AUCUN LIVREUR DANS CE SECTEUR ».
#
# TROIS FILTRES SE SUIVENT, ET UN SEUL SUFFIT A VIDER LA CARTE :
#
#   1. LOCATION a-t-il un point FRAIS pour ce livreur ? Un battement plus vieux
#      que la fenetre de fraicheur disparait de l'index, et le livreur avec.
#   2. Ce point est-il DANS LES 3 KM du client ? Le rayon est en dur dans la
#      passerelle, et il ne se regle pas depuis l'application.
#   3. DRIVER le juge-t-il eligible ? Verifie, disponible, et habilite au colis.
#
# CE SCRIPT LES SEPARE, parce que le bandeau de l'application dit la meme phrase
# dans les trois cas — et que la phrase est fausse dans deux d'entre eux : un
# livreur en course ou hors ligne n'est pas « absent du secteur ».
#
# IL NE REGARDE QUE CE QUE LE SERVEUR SAIT. Il ne touche a rien et n'ecrit rien.
#
#   ./scripts/pourquoi-aucun-livreur.sh 48.922370 2.387968
#   ./scripts/pourquoi-aucun-livreur.sh            # sans position du client
#
set -euo pipefail

cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

GATEWAY="${GATEWAY:-http://localhost:5100}"
ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"

# LE RAYON EST RECOPIE DE LA PASSERELLE, pas devine : voir RayonMetres dans
# ClientNearbyEndpoints. S'il y change, il doit changer ici.
RAYON_METRES=3000

LAT="${1:-}"
LNG="${2:-}"

rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
titre() { printf '\n\033[1;34m══ %s\033[0m\n' "$*"; }
echouer() { rouge "ECHEC : $*"; exit 1; }

command -v jq >/dev/null || echouer "jq est requis (brew install jq)."

titre "1. Connexion a la console"
JETON="$(curl -sS -X POST "$GATEWAY/api/web/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"diagnostic\"}" \
  | jq -r '.accessToken // empty')"
[ -n "$JETON" ] || echouer "Identity refuse $ADMIN_EMAIL. La passerelle tourne-t-elle ?"
vert "Connecte."

titre "2. Ce que Location tient comme positions"
CARTE="$(curl -sS "$GATEWAY/api/admin/v1/drivers/positions" -H "Authorization: Bearer $JETON")"

FRAICHEUR="$(echo "$CARTE" | jq -r '.freshnessSeconds // "?"')"
NOMBRE="$(echo "$CARTE" | jq '.positions | length')"

echo "   Fenetre de fraicheur : $FRAICHEUR s"
echo "   Positions tenues     : $NOMBRE"

if [ "$NOMBRE" = "0" ]; then
  cat <<'FIN'

   AUCUN POINT DU TOUT. Ce n'est donc ni le rayon ni l'eligibilite : c'est le
   battement de position qui n'arrive pas, ou qui a vieilli.

   CE QU'IL FAUT REGARDER, dans cet ordre :
     - l'application livreur est-elle AU PREMIER PLAN et en ligne ? Le battement
       s'arrete quand le systeme met l'application en veille ;
     - POST /api/driver/v1/position rend-il 204 ? Les journaux de la passerelle
       le disent ;
     - le service Location tourne-t-il, et son Redis repond-il ?
FIN
  exit 0
fi

echo
echo "$CARTE" | jq -r '.positions[] |
  "   \(.displayName)\n     etat      : \(.operationalStatus)\n     vehicule  : \(.vehicleType)\n     position  : \(.latitude), \(.longitude)\n     vu a      : \(.seenAt)"'

titre "3. Qui passerait les filtres de la carte client"

if [ -z "$LAT" ] || [ -z "$LNG" ]; then
  cat <<FIN
   PAS DE POSITION CLIENT DONNEE, donc la distance n'est pas calculee. Relance
   avec les coordonnees que l'application affiche :

     ./scripts/pourquoi-aucun-livreur.sh <lat> <lng>

   Sans elles, seule l'eligibilite ci-dessous est verifiee.
FIN
  LAT=""
fi

echo "$CARTE" | LAT="$LAT" LNG="$LNG" RAYON="$RAYON_METRES" jq -r '
  def distance($lat1; $lon1; $lat2; $lon2):
    # Haversine, rayon terrestre moyen. La meme formule que Redis GEO, a
    # quelques metres pres — assez pour dire « dedans » ou « dehors ».
    (3.141592653589793 / 180) as $rad
    | (($lat2 - $lat1) * $rad) as $dlat
    | (($lon2 - $lon1) * $rad) as $dlon
    | (($dlat / 2) | sin) as $a1
    | (($dlon / 2) | sin) as $a2
    | ($a1 * $a1 + (($lat1 * $rad) | cos) * (($lat2 * $rad) | cos) * $a2 * $a2) as $a
    | 2 * 6371000 * (($a | sqrt) | asin);

  (env.LAT | select(. != "") | tonumber) as $lat
  | (env.LNG | tonumber) as $lng
  | (env.RAYON | tonumber) as $rayon
  | .positions[]
  | distance($lat; $lng; .latitude; .longitude) as $d
  | "   \(.displayName)\n     distance  : \($d | round) m  (\(if $d <= $rayon then "DANS le rayon de \($rayon) m" else "HORS du rayon de \($rayon) m" end))\n     eligible  : \(if .operationalStatus == "AVAILABLE" then "oui, si son dossier est valide" else "NON — etat \(.operationalStatus), la carte client l_ecarte" end)"
' 2>/dev/null || echo "$CARTE" | jq -r '.positions[] | "   \(.displayName) : etat \(.operationalStatus)"'

cat <<'FIN'

══ Lecture
   DISTANCE HORS RAYON    -> ce n'est pas une panne. Le client et le livreur ne
                             sont pas dans le meme secteur ; la carte dit vrai.
   ETAT != AVAILABLE      -> le livreur est hors ligne, reserve ou en mission.
                             La carte client l'ecarte, et son bandeau ment : il
                             n'est pas absent du secteur, il est indisponible.
   TOUT PASSE ICI         -> alors le refus vient du dossier (non valide) ou de
                             l'habilitation au colis. ./scripts/valider-kyc.sh
                             affiche le premier.
FIN
