#!/usr/bin/env bash
#
# RETIRE DU RESEAU LES POINTS DE TERMINAISON QUI NE CORRESPONDENT A RIEN.
#
# LE SYMPTOME : « Error response from daemon: failed to set up container
# networking: endpoint with name <conteneur> already exists in network
# hba-internal ». Compose cree le conteneur, veut le brancher, et le reseau
# repond qu'un branchement de ce nom existe deja — celui de l'instance
# precedente, qui a disparu sans se detacher.
#
# QUAND CELA ARRIVE : un conteneur tue brutalement, un demon arrete en pleine
# ecriture, un disque plein au mauvais moment, un reseau supprime puis recree
# sous les pieds de conteneurs encore lances. Le registre du reseau garde alors
# une entree pour un conteneur qui ne tourne plus.
#
# LA REGLE QUI PERMET DE TRANCHER SANS RISQUE : un conteneur qui s'arrete
# proprement LIBERE son point de terminaison. Donc toute entree du reseau dont
# le conteneur n'est pas EN COURS D'EXECUTION est une entree morte. Ce script
# ne touche a rien d'autre — un conteneur vivant garde son branchement.
#
# IL NE SUPPRIME NI CONTENEUR, NI RESEAU, NI VOLUME.
set -euo pipefail

RESEAU="${1:-hba-internal}"

command -v docker >/dev/null || { echo "docker est introuvable." >&2; exit 1; }
docker network inspect "$RESEAU" >/dev/null 2>&1 || {
  echo "Le reseau $RESEAU n'existe pas." >&2
  exit 1
}

NOMS="$(docker network inspect "$RESEAU" \
  --format '{{range .Containers}}{{.Name}}{{"\n"}}{{end}}' | sed '/^$/d')"

if [[ -z "$NOMS" ]]; then
  echo "Aucun branchement declare sur $RESEAU."
  exit 0
fi

retires=0
while read -r nom; do
  [[ -z "$nom" ]] && continue

  if [[ -n "$(docker ps --filter "name=^/${nom}$" --quiet)" ]]; then
    printf '  %-40s vivant, on n y touche pas\n' "$nom"
    continue
  fi

  printf '  %-40s FANTOME — on le retire\n' "$nom"

  # « --force » EST LE SEUL QUI MARCHE ICI. Sans lui, Docker refuse de
  # detacher ce qu'il considere comme un conteneur absent : la commande se
  # plaint que le conteneur n'est pas connecte, alors que c'est precisement
  # l'entree qu'on veut retirer.
  docker network disconnect --force "$RESEAU" "$nom" 2>/dev/null \
    && retires=$((retires + 1)) \
    || printf '  %-40s refus — il reste a redemarrer Docker Desktop\n' "$nom"
done <<< "$NOMS"

echo
if (( retires == 0 )); then
  echo "Rien a retirer."
else
  echo "$retires entree(s) retiree(s). Enchainez sur :"
  echo "  make up"
  echo "  make rattacher"
fi

echo
echo "SI LE MEME MESSAGE REVIENT, LE REGISTRE DU RESEAU EST PLUS ABIME QUE CA."
echo "Il est reconstruit a neuf au demarrage du demon : redemarrer Docker"
echo "Desktop efface tous les fantomes d'un coup, sans toucher aux volumes."
