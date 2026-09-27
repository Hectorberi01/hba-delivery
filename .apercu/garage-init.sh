#!/usr/bin/env bash
#
# INITIALISATION DE GARAGE : disposition, bucket, cle d'acces.
#
# Sans elle, le stockage repond « no storage nodes » a la moindre ecriture :
# Garage ne sert rien tant qu'une disposition n'est pas appliquee. C'est la
# difference qui surprend quand on vient de MinIO.
#
# CE SCRIPT TOURNE SUR L'HOTE, PAS DANS LE CONTENEUR, et ce n'est pas un
# detail de confort : l'image de Garage ne contient QUE le binaire. Pas de
# shell, pas de coreutils. Un script monte dedans echoue sur
# « no such file or directory » — l'interpreteur manquant, pas le script.
# Chaque commande passe donc par « docker compose exec ».
#
# Idempotent : relance sans risque.
#
#   ./scripts/garage-init.sh
#   COMPOSE=deploy/compose.infra.yml ./scripts/garage-init.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

COMPOSE="${COMPOSE:-deploy/docker-compose.yml}"
BUCKET="${GARAGE_BUCKET:-hba-driver-documents}"
CLE="${GARAGE_KEY_NAME:-hba-driver}"

bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[1;33m%s\033[0m\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }

echouer() { rouge "ECHEC : $*"; exit 1; }

# -T : pas de pseudo-terminal. Sans lui, la sortie arrive avec des retours
# chariot qui cassent toute comparaison de chaine.
g() { docker compose -f "$COMPOSE" exec -T garage /garage "$@"; }

command -v docker >/dev/null || echouer "docker est requis."

# --------------------------------------------------------------- 1. Le noeud --

etape "1. Attente du noeud"

i=0
until g status >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -gt 30 ]; then
    rouge "Le noeud ne repond pas apres 30 s."
    rouge ""
    rouge "Verifiez d'abord qu'il tourne :"
    rouge "  docker compose -f $COMPOSE ps garage"
    rouge "  docker compose -f $COMPOSE logs --tail 50 garage"
    exit 1
  fi
  sleep 1
done

# L'identifiant est une empreinte de la cle publique du noeud : il change si
# le volume de metadonnees est recree. On le relit a chaque passage plutot que
# de le figer quelque part.
#
# « node id » est la source sure ; il rend « <id>@<adresse> », d'ou la coupe.
# Le repli lit « status » pour les versions qui n'ont pas cette commande.
NOEUD="$(g node id -q 2>/dev/null | tr -d '\r' | cut -d@ -f1 || true)"

if [ -z "$NOEUD" ]; then
  NOEUD="$(g status 2>/dev/null | tr -d '\r' \
    | awk '/HEALTHY NODES/{lu=1; next} lu && $1 ~ /^[0-9a-f]{8,}$/ {print $1; exit}')"
fi

[ -n "$NOEUD" ] || {
  rouge "Identifiant de noeud introuvable. Sortie de « garage status » :"
  g status >&2 || true
  exit 1
}

# DEUX ECRITURES DU MEME IDENTIFIANT, ET IL FAUT LES DEUX.
#
# « node id » rend les 64 caracteres ; « layout show » n'en affiche que 16.
# Chercher la forme longue dans la sortie de « layout show » ne trouve donc
# JAMAIS rien — le script concluait « rien n'est en place » sur une
# disposition parfaitement appliquee.
#
# Les commandes prennent la forme longue ; les recherches, la courte.
NOEUD_COURT="${NOEUD:0:16}"

bleu "Noeud $NOEUD_COURT" 

# --------------------------------------------------------- 2. La disposition --

etape "2. Disposition"

# GARAGE IMPRIME LUI-MEME LA COMMANDE A TAPER, numero de version compris :
#
#     garage layout apply --version 1
#
# C'est cette ligne qu'on lit, et non « Current cluster layout version », qui
# apparait plusieurs fois dans la sortie.
#
# LE « || true » N'EST PAS DECORATIF. Le script tourne sous « set -o pipefail » :
# quand il n'y a rien a appliquer, les deux grep ne trouvent rien, rendent 1,
# et le pipeline entier rend 1 — ce qui tuait le script a l'endroit meme ou il
# devait dire « rien a faire ». Une absence de resultat n'est pas une erreur.
version_a_appliquer() {
  echo "$1" | grep -oE 'apply[[:space:]]+--version[[:space:]]+[0-9]+' \
    | grep -oE '[0-9]+$' | tail -1 || true
}

# Une section de « layout show », entre son titre et le ==== suivant.
section() {
  echo "$1" | awk -v titre="$2" '$0 ~ titre {lu=1; next} lu && /^====/{exit} lu {print}'
}

DISPOSITION="$(g layout show 2>/dev/null | tr -d '\r' || true)"
COURANTE="$(section "$DISPOSITION" 'CURRENT CLUSTER LAYOUT')"
PREPAREE="$(section "$DISPOSITION" 'STAGED ROLE CHANGES')"

if echo "$COURANTE" | grep -q "$NOEUD_COURT"; then
  vert "Noeud deja dans la disposition courante."
elif echo "$PREPAREE" | grep -q "$NOEUD_COURT"; then
  # DEJA PREPARE, PAS ENCORE APPLIQUE — l'etat que laisse un « apply »
  # interrompu. Reassigner par-dessus fait echouer Garage ; on enchaine
  # directement sur l'application.
  jaune "Changement deja prepare, non applique. On l'applique."
else
  # LA SORTIE N'EST PLUS JETEE. La version precedente faisait
  # « assign ... >/dev/null », et Garage ecrit ses erreurs sur la SORTIE
  # STANDARD : un echec d'assignation ne laissait donc qu'un « Error 1 » nu,
  # sans la moindre indication de cause. C'est exactement le genre de silence
  # qui coute une demi-heure.
  if ! SORTIE="$(g layout assign -z cotonou -c 10G "$NOEUD" 2>&1)"; then
    rouge "L'assignation a echoue. Garage a repondu :"
    printf '%s\n' "$SORTIE" | sed 's/^/    /' >&2
    exit 1
  fi
  DISPOSITION="$(g layout show 2>/dev/null | tr -d '\r' || true)"
fi

VERSION="$(version_a_appliquer "$DISPOSITION")"

if [ -n "$VERSION" ]; then
  if ! SORTIE="$(g layout apply --version "$VERSION" 2>&1)"; then
    rouge "L'application de la version $VERSION a echoue. Garage a repondu :"
    printf '%s\n' "$SORTIE" | sed 's/^/    /' >&2
    rouge ""
    rouge "Etat courant :"
    g layout show 2>&1 | sed 's/^/    /' >&2 || true
    exit 1
  fi
  vert "Appliquee en version $VERSION."
elif echo "$COURANTE" | grep -q "$NOEUD_COURT"; then
  vert "Rien a appliquer."
else
  rouge "Des changements sont prepares mais Garage ne suggere aucune version."
  rouge "Sortie de « garage layout show » :"
  printf '%s\n' "$DISPOSITION" | sed 's/^/    /' >&2
  exit 1
fi

# -------------------------------------------------------------- 3. Le bucket --

etape "3. Bucket $BUCKET"

if g bucket list 2>/dev/null | tr -d '\r' | grep -qw "$BUCKET"; then
  vert "Existe deja."
else
  g bucket create "$BUCKET"
  vert "Cree."
fi

# ----------------------------------------------------------------- 4. La cle --

etape "4. Cle d'acces $CLE"

CLE_EXISTE=0
if g key list 2>/dev/null | tr -d '\r' | grep -qw "$CLE"; then
  CLE_EXISTE=1
  jaune "Existe deja."
else
  g key create "$CLE"
  vert "Creee."
fi

g bucket allow --read --write --owner "$BUCKET" --key "$CLE"
vert "Droits accordes sur $BUCKET."

# ------------------------------------------------------------- 5. Les secrets --

etape "5. Report dans src/Services/Driver/.env"

ENV_DRIVER="src/Services/Driver/.env"

# LE SECRET NE S'AFFICHE QU'UNE FOIS, ET LA RECOPIE A LA MAIN A DEJA ECHOUE
# DEUX FOIS. Le script connait les deux valeurs au moment de la creation : il
# les ecrit lui-meme. Le .env est ignore par git, c'est sa place.
if [ "$CLE_EXISTE" = "1" ]; then
  jaune "La cle existait deja : GARAGE NE REAFFICHE JAMAIS UN SECRET."
  jaune ""
  jaune "Si src/Services/Driver/.env ne l'a pas, supprimez la cle et relancez :"
  jaune "  docker compose -f $COMPOSE exec -T garage /garage key delete --yes $CLE"
  jaune "  ./scripts/garage-init.sh"
  echo
  g key info "$CLE" 2>/dev/null || true
  exit 0
fi

INFOS="$(g key info "$CLE" --show-secret 2>&1 | tr -d '\r')"

ID="$(echo "$INFOS"     | grep -iE '^Key ID:'     | awk '{print $NF}')"
SECRET="$(echo "$INFOS" | grep -iE '^Secret key:' | awk '{print $NF}')"

if [ -z "$ID" ] || [ -z "$SECRET" ]; then
  rouge "Impossible de lire la cle dans la sortie de Garage :"
  printf '%s\n' "$INFOS" | sed 's/^/    /' >&2
  rouge ""
  rouge "Reportez-les a la main dans $ENV_DRIVER :"
  rouge "  OBJECTSTORE_ACCESS_KEY=…"
  rouge "  OBJECTSTORE_SECRET_KEY=…"
  exit 1
fi

if [ ! -f "$ENV_DRIVER" ]; then
  rouge "$ENV_DRIVER est absent. Copiez d'abord le .env.example a cote."
  exit 1
fi

# Un seul passage de sed, sur une copie, puis remplacement : une interruption
# au milieu ne doit pas laisser un .env a moitie ecrit.
TMP_ENV="$(mktemp)"
sed -e "s|^OBJECTSTORE_ACCESS_KEY=.*|OBJECTSTORE_ACCESS_KEY=$ID|" \
    -e "s|^OBJECTSTORE_SECRET_KEY=.*|OBJECTSTORE_SECRET_KEY=$SECRET|" \
    "$ENV_DRIVER" > "$TMP_ENV"
mv "$TMP_ENV" "$ENV_DRIVER"

grep -q "^OBJECTSTORE_ACCESS_KEY=$ID$" "$ENV_DRIVER" \
  || { rouge "Le report dans $ENV_DRIVER a echoue."; exit 1; }

vert "Cles ecrites dans $ENV_DRIVER."
bleu "  OBJECTSTORE_ACCESS_KEY=$ID"
bleu "  OBJECTSTORE_SECRET_KEY=${SECRET:0:6}… (masque)"
echo
bleu "Il reste a recreer le service :"
bleu "  docker compose -f src/Services/Driver/docker-compose.yml up -d --force-recreate"
