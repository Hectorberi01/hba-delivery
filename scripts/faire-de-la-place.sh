#!/usr/bin/env bash
#
# RECUPERER DE LA PLACE DANS DOCKER, SANS TOUCHER AUX DONNEES.
#
# CE SCRIPT EXISTE POUR QU'ON NE TAPE PAS L'AUTRE COMMANDE. « docker system
# prune --volumes » libere beaucoup, tres vite, et detruit AU PASSAGE les
# volumes nommes de ce projet : la base Postgres entiere, les journaux Kafka,
# les objets Garage, et la cle de signature d'Identity. Les trois premiers,
# c'est tout le jeu de donnees de recette ; la quatrieme invalide TOUS les
# jetons deja emis, donc deconnecte les deux applications.
#
# CE QU'IL SUPPRIME, ET POURQUOI C'EST SANS RISQUE :
#
#   le cache de construction   Il se reconstruit. C'est presque toujours le
#                              plus gros poste : chaque « dotnet publish » en
#                              laisse une couche, et il y en a une par service
#                              et par tentative. Le prochain build sera plus
#                              long, et c'est le seul prix.
#   les images orphelines      Celles qu'aucune etiquette ne designe plus.
#                              « up --build » en fabrique une a chaque passage :
#                              l'ancienne perd son nom au profit de la nouvelle
#                              et reste sur le disque sans que rien ne la cite.
#   les conteneurs arretes     Leur couche d'ecriture occupe la place pour rien.
#
# CE QU'IL NE SUPPRIME PAS, ET N'AURA JAMAIS LE DROIT DE SUPPRIMER :
#
#   les volumes                Il les LISTE, il n'en efface aucun.
#   les reseaux                « docker network prune » emporterait
#                              hba-internal, et les conteneurs qui tournent
#                              resteraient attaches a un reseau disparu — ils
#                              ne se resoudraient plus entre eux jusqu'a etre
#                              RECREES. Voir pourquoi-kafka-injoignable.sh.
#   les images utilisees       On s'en tient aux orphelines : « image prune -a »
#                              emporterait aussi postgres, kafka et redis des
#                              qu'ils sont arretes, qu'il faudrait retelecharger.
set -euo pipefail

command -v docker >/dev/null || { echo "docker est introuvable."; exit 1; }

# Les volumes de ce projet, par leur nom reel : « <projet>_<volume> ».
PROTEGES=(
  hba-delivery-dev_pgdata
  hba-delivery-dev_kafkadata
  hba-delivery-dev_garagemeta
  hba-delivery-dev_garagedata
  hba-infra_pgdata
  hba-infra_kafkadata
  hba-infra_redisdata
  hba-infra_garagemeta
  hba-infra_garagedata
  hba-infra_letsencrypt
  hba-identity_identity-keys
)

echo "── Avant ─────────────────────────────────────────────────────────"
docker system df

echo
echo "── Ce qui ne sera pas touche ─────────────────────────────────────"
for v in "${PROTEGES[@]}"; do
  if docker volume inspect "$v" >/dev/null 2>&1; then
    printf '  %-32s present\n' "$v"
  else
    printf '  %-32s absent (jamais cree, ou deja perdu)\n' "$v"
  fi
done

echo
echo "── Le cache de construction ──────────────────────────────────────"
# « -a » ET NON LE DEFAUT. Sans lui, seules les entrees non referencees
# partent, et sur une machine qui vient de reconstruire seize services, elles
# ne sont presque rien : le gros est le cache des couches encore citees.
docker builder prune -af

echo
echo "── Les images orphelines ─────────────────────────────────────────"
docker image prune -f

echo
echo "── Les conteneurs arretes ────────────────────────────────────────"
docker container prune -f

echo
echo "── Les volumes sans proprietaire, POUR LECTURE ───────────────────"
echo "AUCUN N'EST SUPPRIME ICI. Un volume orphelin peut etre la base d'un"
echo "service dont le conteneur est simplement arrete : le lire a l'oeil"
echo "avant d'en effacer un, et un par un."
orphelins="$(docker volume ls -qf dangling=true || true)"
if [[ -z "$orphelins" ]]; then
  echo "  aucun"
else
  while read -r v; do
    [[ -z "$v" ]] && continue
    garde="a examiner"
    for p in "${PROTEGES[@]}"; do [[ "$v" == "$p" ]] && garde="NE PAS TOUCHER"; done
    taille="$(docker volume inspect "$v" --format '{{.Mountpoint}}' 2>/dev/null || echo '?')"
    printf '  %-34s %-15s %s\n' "$v" "$garde" "$taille"
  done <<< "$orphelins"
  echo
  echo "  Pour en retirer un, nommement :  docker volume rm <nom>"
fi

echo
echo "── Apres ─────────────────────────────────────────────────────────"
docker system df

echo
echo "── Si cela ne suffit toujours pas ────────────────────────────────"
echo "Le disque de Docker Desktop est une taille FIXE, independante de celle"
echo "du Mac : un Mac a moitie vide n'y change rien. Elle se releve dans"
echo "Docker Desktop, Settings puis Resources, « Virtual disk limit ». Le"
echo "fichier est creux — relever la limite ne coute rien tant que la place"
echo "n'est pas utilisee."
echo
echo "ET RECONSTRUIRE MOINS. « make relancer » refait les seize services et"
echo "laisse seize images orphelines derriere lui. Un seul service a la fois :"
echo "  make rebuild D=src/Services/Rating"
