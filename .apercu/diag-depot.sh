#!/usr/bin/env bash
# Diagnostic du depot de piece : quelle instruction SQL n'affecte aucune ligne.
#
# POURQUOI CE SCRIPT PLUTOT QU'UNE HYPOTHESE. EF Core leve
# DbUpdateConcurrencyException des qu'un UPDATE ou un DELETE touche zero
# ligne. Le message ne dit jamais LAQUELLE des instructions du lot a echoue.
# Sans cette information, toute correction est une devinette : on fait donc
# parler PostgreSQL, qui sait exactement ce qu'il a recu.
#
# Aucun service n'est reconstruit : on ne touche qu'a la journalisation de
# PostgreSQL, rechargee a chaud.
set -euo pipefail

COMPOSE="deploy/docker-compose.yml"
BASE="hba_driver"
UTILISATEUR="${POSTGRES_USER:-hba}"

psqlq() {
  docker compose -f "$COMPOSE" exec -T postgres \
    psql -v ON_ERROR_STOP=1 -U "$UTILISATEUR" -d "$1" -c "$2"
}

titre() { printf '\n\033[1m── %s\033[0m\n' "$1"; }

usage() {
  cat <<'USAGE'
Usage :
  scripts/diag-depot.sh <driver_id>            Etat actuel + journalisation ON
  scripts/diag-depot.sh --apres <driver_id>    Instructions recues + journalisation OFF
USAGE
  exit 64
}

APRES=0
if [ "${1:-}" = "--apres" ]; then APRES=1; shift; fi
LIVREUR="${1:-}"
[ -n "$LIVREUR" ] || usage

if [ "$APRES" -eq 0 ]; then
  titre "1. Le livreur"
  psqlq "$BASE" "SELECT id, verification_status, submitted_at, profile_photo_key, xmin
                 FROM driver.drivers WHERE id = '$LIVREUR';"

  titre "2. Ses pieces deja enregistrees"
  # LA REPONSE EST ICI POUR MOITIE : si une piece de la nature deposee existe
  # deja, le depot suivant passe par le chemin « remplacement » (DELETE de
  # l'ancienne + INSERT de la nouvelle). Si la table est vide, c'est un simple
  # INSERT — et un INSERT ne peut pas affecter zero ligne.
  psqlq "$BASE" "SELECT id, type, object_key, size_bytes, uploaded_at
                 FROM driver.driver_documents WHERE driver_id = '$LIVREUR'
                 ORDER BY type;"

  titre "3. Journalisation des instructions : ON"
  psqlq postgres "ALTER SYSTEM SET log_statement = 'all';"
  psqlq postgres "SELECT pg_reload_conf();"

  MARQUE="diag-depot-debut-$(date +%s)"
  psqlq "$BASE" "SELECT '$MARQUE';" >/dev/null
  echo "$MARQUE" > .diag-depot-marque
  echo "Marque posee : $MARQUE"

  cat <<EOF

MAINTENANT : refais le depot de la piece dans l'application, une seule fois.
Puis lance :

    scripts/diag-depot.sh --apres $LIVREUR

EOF
  exit 0
fi

MARQUE="$(cat .diag-depot-marque 2>/dev/null || true)"

titre "4. Instructions recues depuis la marque"
if [ -n "$MARQUE" ]; then
  docker compose -f "$COMPOSE" logs --no-log-prefix postgres 2>/dev/null \
    | awk -v m="$MARQUE" 'index($0, m) {vu=1} vu' \
    | grep -Ei 'driver_documents|driver\.drivers|BEGIN|COMMIT|ROLLBACK' \
    | grep -v 'pg_catalog' \
    | tail -80
else
  echo "Pas de marque : affichage des 80 dernieres instructions."
  docker compose -f "$COMPOSE" logs --no-log-prefix --tail 4000 postgres 2>/dev/null \
    | grep -Ei 'driver_documents|driver\.drivers' | tail -80
fi

titre "5. Etat des pieces apres l'essai"
psqlq "$BASE" "SELECT id, type, object_key, uploaded_at
               FROM driver.driver_documents WHERE driver_id = '$LIVREUR'
               ORDER BY type;"

titre "6. Journalisation : OFF"
psqlq postgres "ALTER SYSTEM SET log_statement = 'none';"
psqlq postgres "SELECT pg_reload_conf();"
rm -f .diag-depot-marque
