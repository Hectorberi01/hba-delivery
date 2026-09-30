#!/usr/bin/env bash
#
# POURQUOI « AJOUTER UNE ADRESSE » RETOMBE EN 500.
#
# LE JOURNAL DE DIRECTORY MONTRE UN « UPDATE » LA OU ON ATTEND UN « INSERT » :
#
#   UPDATE directory.customer_favorite_addresses SET created_at = @p0, ...
#   WHERE "Id" = @p10;
#   -> DbUpdateConcurrencyException: expected to affect 1 row(s), actually 0
#
# EF croit donc modifier une ligne qui existe deja. Deux causes produisent
# exactement cette trace, et LE JOURNAL NE LES DEPARTAGE PAS parce que les
# valeurs des parametres y sont masquees :
#
#   A. LA TABLE CONTIENT DEJA DES LIGNES pour ce client. EF les a chargees
#      (le LEFT JOIN de la requete precedente), les suit comme « inchangees »,
#      et quelque chose les a ensuite marquees modifiees en entier.
#      -> le script affiche des lignes ci-dessous.
#
#   B. LA TABLE EST VIDE, et c'est la NOUVELLE adresse qu'EF tente de mettre a
#      jour. La cle est posee cote client (Guid.CreateVersion7) alors que le
#      modele la declare « ValueGeneratedOnAdd » : EF voit une cle deja
#      renseignee et en conclut que l'entite existe.
#      -> le script n'affiche aucune ligne.
#
# La distinction commande deux corrections differentes. On regarde avant
# d'ecrire quoi que ce soit.
#
#   ./scripts/adresses-client.sh 01a0e4d9-f401-7894-a8d8-ba6af1ba7c7c
#   ./scripts/adresses-client.sh            # toutes les lignes de la table

set -euo pipefail

racine="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose="$racine/deploy/docker-compose.yml"
client="${1:-}"

if ! command -v docker >/dev/null 2>&1; then
  echo "docker introuvable." >&2
  exit 1
fi

# LE PSQL EST CELUI DU CONTENEUR, pas celui du Mac : toujours de la bonne
# version, et rien a installer.
psql() {
  docker compose -f "$compose" exec -T postgres \
    psql -U "${POSTGRES_USER:-hba}" -d hba_directory -v ON_ERROR_STOP=1 "$@"
}

filtre="TRUE"
if [ -n "$client" ]; then
  filtre="a.\"CustomerId\" = '$client'"
fi

echo "── Les adresses favorites en base ──"
psql -c "
  SELECT a.\"Id\", a.\"CustomerId\", a.label, a.is_default, a.created_at
  FROM directory.customer_favorite_addresses a
  WHERE $filtre
  ORDER BY a.created_at;
"

echo
echo "── Combien, et pour combien de clients ──"
psql -c "
  SELECT count(*) AS lignes, count(DISTINCT a.\"CustomerId\") AS clients
  FROM directory.customer_favorite_addresses a;
"

echo
echo "── La colonne de la photo existe-t-elle deja ? ──"
# ELLE N'A RIEN A VOIR AVEC CE BOGUE-CI, mais elle explique l'autre panne : le
# modele EF la declare depuis le lot 2, et tant que la migration n'est pas
# generee, TOUTE lecture de client echoue en 42703.
psql -c "
  SELECT column_name
  FROM information_schema.columns
  WHERE table_schema = 'directory' AND table_name = 'customers'
    AND column_name = 'photo_media_id';
"

echo
echo "CE QU'IL FAUT LIRE :"
echo "  - des lignes au-dessus  -> cause A, la table n'etait pas vide ;"
echo "  - aucune ligne          -> cause B, la cle posee cote client."
