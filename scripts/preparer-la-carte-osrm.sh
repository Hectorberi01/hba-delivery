#!/usr/bin/env bash
#
# PREPARE LA CARTE DU BENIN POUR OSRM, PUIS BASCULE ROUTING DESSUS.
#
# POURQUOI CE SCRIPT EXISTE. Le depot contient un service Routing complet —
# CalculateRoute, CalculateMatrix, cache Redis, repli a vol d'oiseau — et un
# conteneur OSRM pret dans deploy/docker-compose.yml. Mais « Routing:Provider »
# vaut « haversine » dans appsettings.json ET en Development, et
# deploy/.data/osrm/ n'existe pas : AUCUNE MESURE DE CE DEPOT N'EST JAMAIS
# PASSEE PAR UN VRAI MOTEUR D'ITINERAIRE.
#
# CE QUE CELA COUTE TANT QUE C'EST VRAI. Une distance a vol d'oiseau est
# sous-estimee d'environ un tiers en ville, davantage des que le trajet
# contourne la lagune. Trois consequences, toutes silencieuses :
#
#   - PRICING fige un prix FERME a la commande sur cette distance. Il facture
#     donc moins que la course ne coute, et rien ne le signale avant la fin du
#     mois.
#   - DISPATCH compare les ETA de plusieurs chauffeurs. A vol d'oiseau, celui
#     qui est de l'autre cote d'un pont parait le plus proche.
#   - L'ETA montre au client, le jour ou il existera, serait optimiste du meme
#     tiers — et un client juge le service sur cette promesse-la.
#
# LA PREPARATION EST LONGUE ET ON NE LA REFAIT PAS. Chaque passe est sautee si
# son resultat est deja la : relancer le script apres une coupure reprend ou il
# s'etait arrete, au lieu de tout recommencer.
#
# LA BASCULE N'A LIEU QU'APRES VERIFICATION. Le script ne touche aux
# appsettings QUE si OSRM a reellement repondu a un itineraire REEL dans la
# region preparee. Basculer d'abord et verifier ensuite aurait laisse une
# configuration qui ment, et le repli automatique de CalculateRoute aurait
# masque la panne.
#
# LA REGION EST UN ARGUMENT, PARCE QUE LE BANC D'ESSAI N'EST PAS LE TERRAIN.
# L'application est essayee en FRANCE avant d'etre deployee au Benin. Une carte
# du Benin ne route rien a Aubervilliers : OSRM accroche les deux points au
# reseau le plus proche qu'il connaisse — a quatre mille kilometres — et rend
# soit « NoRoute », soit une absurdite. Les deux cartes peuvent coexister dans
# .data/osrm : chacune se prepare une fois, et l'on bascule de l'une a l'autre
# en relancant ce script avec l'autre region, sans rien retelecharger.
#
#   ./scripts/preparer-la-carte-osrm.sh ile-de-france   (le banc d'essai)
#   ./scripts/preparer-la-carte-osrm.sh benin           (le terrain)
#
# POURQUOI L'ILE-DE-FRANCE ET NON LA FRANCE ENTIERE. Le fichier de la France
# pese plusieurs gigaoctets et son extraction en demande plusieurs dizaines en
# memoire : sur un portable, la passe se fait tuer sans message clair. L'extrait
# regional couvre Paris et la petite couronne, ce qui est exactement le
# perimetre d'un essai.
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DONNEES="$RACINE/deploy/.data/osrm"
COMPOSE="$RACINE/deploy/docker-compose.yml"
ENVIRONNEMENT="$RACINE/deploy/.env"
IMAGE="ghcr.io/project-osrm/osrm-backend:latest"

REGION="${1:-benin}"

# LES DEUX SONDES SONT DES TRAJETS QUE L'ON PEUT VERIFIER A L'OEIL. Un itineraire
# de quelques kilometres annonce en quarante secondes voudrait dire que la carte
# chargee n'est pas celle qu'on croit.
case "$REGION" in
  benin)
    BASE="benin-latest"
    CHEMIN="africa"
    DEPART="2.3912,6.3703"   # Cotonou, Etoile Rouge
    ARRIVEE="2.4300,6.3600"  # vers Akpakpa
    ;;
  ile-de-france|idf)
    REGION="ile-de-france"
    BASE="ile-de-france-latest"
    CHEMIN="europe/france"
    DEPART="2.3831,48.9145"  # Aubervilliers, mairie
    ARRIVEE="2.3522,48.8566" # Paris, Notre-Dame
    ;;
  *)
    echo "Region inconnue : « $REGION ». Attendu : benin ou ile-de-france." >&2
    exit 1
    ;;
esac

PBF="$BASE.osm.pbf"
SOURCE="https://download.geofabrik.de/$CHEMIN/$PBF"

# LE PROFIL EST CELUI DE LA VOITURE, ET UNE SEULE INSTANCE TOURNE.
#
# Le profil d'OSRM est fige AU PRE-TRAITEMENT, pas a l'appel : le segment
# « /driving/ » de l'URL est decoratif. Servir vraiment la moto et le velo
# demanderait deux cartes de plus et deux conteneurs de plus. La voiture et la
# moto empruntent les memes voies a Cotonou — c'est le velo qui est approxime,
# et RoutingOptions le dit deja.
PROFIL="/opt/car.lua"

dire() { printf '\n== %s\n' "$*"; }

command -v docker >/dev/null || {
  echo "docker est introuvable sur cette machine." >&2
  exit 1
}

mkdir -p "$DONNEES"

# ---------------------------------------------------------------- telechargement
if [[ -s "$DONNEES/$PBF" ]]; then
  dire "Fichier source deja present ($(du -h "$DONNEES/$PBF" | cut -f1))."
else
  dire "Telechargement de la carte « $REGION » depuis Geofabrik."
  curl -fL --progress-bar -o "$DONNEES/$PBF.partiel" "$SOURCE"

  # ON NE RENOMME QU'APRES COUP. Une coupure reseau laisserait sinon un fichier
  # tronque portant le bon nom, que la passe suivante accepterait d'ouvrir avant
  # d'echouer sans dire pourquoi.
  mv "$DONNEES/$PBF.partiel" "$DONNEES/$PBF"
  dire "Telecharge ($(du -h "$DONNEES/$PBF" | cut -f1))."
fi

osrm() {
  docker run --rm -t -v "$DONNEES:/data" "$IMAGE" "$@"
}

# ---------------------------------------------------------------------- extract
#
# LA PASSE LA PLUS LOURDE : elle lit tout le fichier et construit le graphe.
# C'est aussi la seule qui demande de la memoire — quelques gigaoctets pour le
# Benin. Si Docker Desktop est limite plus bas, elle se fait tuer sans message
# clair, et c'est la premiere chose a verifier en cas d'echec ici.
if [[ -f "$DONNEES/$BASE.osrm.ebg" ]]; then
  dire "Extraction deja faite."
else
  dire "Extraction du graphe (la passe la plus longue)."
  osrm osrm-extract -p "$PROFIL" "/data/$PBF"
fi

# -------------------------------------------------------------------- partition
if [[ -f "$DONNEES/$BASE.osrm.partition" ]]; then
  dire "Partition deja faite."
else
  dire "Partition."
  osrm osrm-partition "/data/$BASE.osrm"
fi

# -------------------------------------------------------------------- customize
if [[ -f "$DONNEES/$BASE.osrm.cells" ]]; then
  dire "Personnalisation deja faite."
else
  dire "Personnalisation."
  osrm osrm-customize "/data/$BASE.osrm"
fi

# ------------------------------------------------------------------- demarrage
# LA CARTE CHOISIE EST ECRITE DANS deploy/.env, QUE COMPOSE LIT TOUT SEUL. Sans
# cela, un « docker compose up » lance a la main plus tard rechargerait la carte
# par defaut — celle du Benin — et le banc d'essai francais cesserait de router
# sans que rien ne l'annonce.
dire "Carte retenue : $BASE (ecrite dans deploy/.env)."
touch "$ENVIRONNEMENT"
if grep -q '^OSRM_CARTE=' "$ENVIRONNEMENT"; then
  if sed --version >/dev/null 2>&1; then
    sed -i "s|^OSRM_CARTE=.*|OSRM_CARTE=$BASE|" "$ENVIRONNEMENT"
  else
    sed -i '' "s|^OSRM_CARTE=.*|OSRM_CARTE=$BASE|" "$ENVIRONNEMENT"
  fi
else
  printf '\nOSRM_CARTE=%s\n' "$BASE" >> "$ENVIRONNEMENT"
fi

dire "Demarrage du conteneur OSRM."
docker compose -f "$COMPOSE" --profile routing up -d --force-recreate osrm

# ------------------------------------------------------------------ verification
#
# UN ITINERAIRE REEL, PAS UN PING. Le conteneur repond sur le port des qu'il
# ecoute, bien avant d'avoir charge la carte ; et une carte absente le laisse
# ecouter sans jamais router. Seule une reponse « Ok » sur deux points de
# Cotonou prouve que la preparation a servi.
dire "Verification : un itineraire reel dans « $REGION »."

REPONSE=""
for essai in $(seq 1 30); do
  REPONSE="$(curl -fsS "http://localhost:5000/route/v1/driving/$DEPART;$ARRIVEE?overview=false" 2>/dev/null || true)"
  [[ "$REPONSE" == *'"code":"Ok"'* ]] && break
  printf '.'
  sleep 2
done
printf '\n'

if [[ "$REPONSE" != *'"code":"Ok"'* ]]; then
  echo "OSRM n'a pas rendu d'itineraire. La configuration n'a PAS ete modifiee." >&2
  echo "Journal : docker compose -f deploy/docker-compose.yml logs osrm" >&2
  exit 1
fi

# LA DUREE ET LA DISTANCE SONT AFFICHEES PARCE QU'ELLES SE VERIFIENT A L'OEIL.
# Quelques kilometres annonces en quarante secondes voudraient dire que la carte
# chargee n'est pas celle qu'on croit.
echo "$REPONSE" | tr ',' '\n' | grep -E '"duration"|"distance"' | head -2

# ---------------------------------------------------------------------- bascule
dire "Bascule de Routing sur « osrm »."

for fichier in \
  "$RACINE/src/Services/Routing/Hba.Routing.Api/appsettings.json" \
  "$RACINE/src/Services/Routing/Hba.Routing.Api/appsettings.Development.json"
do
  [[ -f "$fichier" ]] || continue
  if grep -q '"Provider": *"osrm"' "$fichier"; then
    echo "  deja sur osrm : ${fichier#$RACINE/}"
    continue
  fi
  # sed -i exige un argument vide sur BSD (macOS) et le refuse sur GNU.
  if sed --version >/dev/null 2>&1; then
    sed -i 's/"Provider": *"haversine"/"Provider": "osrm"/' "$fichier"
  else
    sed -i '' 's/"Provider": *"haversine"/"Provider": "osrm"/' "$fichier"
  fi
  echo "  bascule : ${fichier#$RACINE/}"
done

cat <<'FIN'

La carte est prete et Routing est configure pour OSRM.

Il reste a recreer le service pour qu'il relise sa configuration :

    make rebuild D=src/Services/Routing

Ce qui change a partir de maintenant : les devis de Pricing et les
comparaisons de Dispatch sont mesures sur le reseau routier reel. Les
reponses cessent de porter « FromFallback », SAUF quand OSRM est injoignable —
auquel cas CalculateRoute replie tout seul et le marque, comme avant.
FIN
