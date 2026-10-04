#!/usr/bin/env bash
#
# CREE LES BASES QUI MANQUENT DANS UN POSTGRES DEJA INITIALISE.
#
# POURQUOI CE SCRIPT EXISTE, ET POURQUOI IL EXISTERA ENCORE AU PROCHAIN
# SERVICE. « deploy/postgres/init-databases.sh » est monte dans
# /docker-entrypoint-initdb.d : l'image Postgres n'execute ce repertoire QU'UNE
# FOIS, a la creation du volume. Ajouter une ligne a ce fichier ne cree donc
# rien sur une machine ou le volume existe deja — et la seule facon de le
# rejouer serait de detruire pgdata, c'est-a-dire tout le jeu de donnees.
#
# LE SYMPTOME NE DESIGNE PAS LA CAUSE. « make migrate » echoue sur
# « 3D000: database "hba_<service>" does not exist », ce qui ressemble a une
# faute dans la chaine de connexion alors que tout y est correct.
#
# LA LISTE N'EST PAS RECOPIEE ICI, ELLE EST LUE LA-BAS. Deux listes finiraient
# par differer, et c'est la seconde — celle qu'on oublie — qui ferait echouer le
# prochain service.
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$RACINE/deploy/postgres/init-databases.sh"

UTILISATEUR="${POSTGRES_USER:-hba}"
MOTDEPASSE="${POSTGRES_PASSWORD:-hba}"
HOTE="${POSTGRES_HOST:-127.0.0.1}"
PORT="${POSTGRES_PORT:-5432}"

command -v psql >/dev/null || {
  echo "psql est introuvable sur cette machine." >&2
  echo "Le conteneur en a un : " >&2
  echo "  docker compose -f deploy/docker-compose.yml exec postgres \\" >&2
  echo "    psql -U $UTILISATEUR -d postgres -c 'CREATE DATABASE hba_<service>;'" >&2
  exit 1
}

# « for db in a b c; do » — on prend ce qui est entre « in » et « ; ».
BASES="$(sed -n 's/^for db in \(.*\); do$/\1/p' "$SOURCE")"
[[ -n "$BASES" ]] || { echo "Liste des bases illisible dans $SOURCE" >&2; exit 1; }

# LE MOT DE PASSE PASSE PAR L'ENVIRONNEMENT, JAMAIS PAR LA LIGNE DE COMMANDE :
# « ps » est lisible par tout le monde sur la machine.
export PGPASSWORD="$MOTDEPASSE"

existe() {
  psql -h "$HOTE" -p "$PORT" -U "$UTILISATEUR" -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname = '$1'" 2>/dev/null | grep -q 1
}

creees=0
for db in $BASES; do
  if existe "$db"; then
    printf '  %-20s presente\n' "$db"
    continue
  fi

  printf '  %-20s ABSENTE — creation\n' "$db"
  psql -v ON_ERROR_STOP=1 -h "$HOTE" -p "$PORT" -U "$UTILISATEUR" -d postgres \
    -c "CREATE DATABASE $db;" >/dev/null

  # POSTGIS DANS CHAQUE BASE, COMME A L'INITIALISATION. Une extension est
  # installee PAR BASE, pas par serveur : l'omettre ici ferait echouer la
  # premiere migration qui pose une colonne geographique, et seulement
  # celle-la.
  psql -v ON_ERROR_STOP=1 -h "$HOTE" -p "$PORT" -U "$UTILISATEUR" -d "$db" \
    -c "CREATE EXTENSION IF NOT EXISTS postgis;" >/dev/null

  creees=$((creees + 1))
done

echo
if (( creees == 0 )); then
  echo "Rien a creer."
else
  echo "$creees base(s) creee(s). Enchainez sur :  make migrate"
fi
