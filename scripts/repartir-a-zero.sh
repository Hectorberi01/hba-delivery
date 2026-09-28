#!/usr/bin/env bash
#
# REPARTIR D'UNE BASE VIERGE, EN GARDANT L'ADMINISTRATEUR.
#
# CE SCRIPT DETRUIT DES DONNEES, ET C'EST TOUT CE QU'IL FAIT. Il n'y a aucune
# sauvegarde, aucune corbeille, aucun retour en arriere. Il est ecrit pour un
# poste de developpement et il refuse de tourner sans une confirmation tapee a
# la main.
#
# POURQUOI UN SCRIPT ET PAS QUATRE COMMANDES. Parce que l'ordre compte : arreter
# les services AVANT de supprimer les volumes (sinon ils ecrivent pendant qu'on
# efface, et ils redemarrent sur un schema absent), recreer les topics AVANT de
# demarrer les services (sinon « Unknown topic or partition »), appliquer les
# migrations AVANT de les demarrer (en Production, Database:AutoMigrate vaut
# faux), et ne les demarrer qu'a la fin — c'est le demarrage d'Identity qui
# recree l'administrateur. Une seule etape sautee laisse une pile qui repond a
# /health et ne fonctionne pas.
#
# L'ADMINISTRATEUR N'EST PAS SAUVEGARDE, IL EST RECREE. AdminBootstrap cree le
# premier administrateur QUAND IL N'Y EN A AUCUN, a partir de
# BOOTSTRAP_ADMIN_EMAIL et BOOTSTRAP_ADMIN_PASSWORD du .env d'Identity. Vider la
# base et redemarrer suffit donc : aucune suppression selective, aucune ligne
# epargnee a la main, aucun risque d'en oublier une qui referencait le reste.
#
#   ./scripts/repartir-a-zero.sh
#
set -euo pipefail

racine="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$racine"

compose="deploy/docker-compose.yml"
mot="VIDER"

SERVICES="src/Services/Identity src/Services/Directory src/Services/Delivery
src/Services/Pricing src/Services/Dispatch src/Services/Driver
src/Services/Payment src/Services/Notification src/Services/Media
src/Gateways/Hba.Gateway"

rouge()  { printf '\033[31m%s\033[0m\n' "$*"; }
jaune()  { printf '\033[33m%s\033[0m\n' "$*"; }
bleu()   { printf '\033[34m%s\033[0m\n' "$*"; }
etape()  { printf '\n\033[1m═══ %s\033[0m\n' "$*"; }

command -v docker >/dev/null 2>&1 || { rouge "docker introuvable."; exit 1; }
[ -f "$compose" ] || { rouge "$compose introuvable : lancez depuis la racine du depot."; exit 1; }

# ---------------------------------------------------- Ce qui va disparaitre ---
#
# ON MONTRE AVANT DE DETRUIRE. Une confirmation demandee sans dire ce qu'on
# perd n'est pas une confirmation, c'est une formalite : on la tape sans lire.
etape "Ce qui va etre detruit"

psql() { docker compose -f "$compose" exec -T postgres psql -U "${POSTGRES_USER:-hba}" -tAc "$2" -d "$1" 2>/dev/null || echo "?"; }

if docker compose -f "$compose" ps --status running postgres >/dev/null 2>&1; then
  printf '  %-14s %s\n' "comptes"    "$(psql hba_identity  'select count(*) from identity.accounts;')"
  printf '  %-14s %s\n' "dont admin" "$(psql hba_identity  "select count(*) from identity.accounts where roles like '%admin%';")"
  printf '  %-14s %s\n' "fiches"     "$(psql hba_directory 'select count(*) from directory.customers;')"
  printf '  %-14s %s\n' "commercants" "$(psql hba_directory 'select count(*) from directory.merchants;')"
  printf '  %-14s %s\n' "livraisons" "$(psql hba_delivery  'select count(*) from delivery.deliveries;')"
  printf '  %-14s %s\n' "livreurs"   "$(psql hba_driver    'select count(*) from driver.drivers;')"
else
  jaune "  (postgres ne tourne pas : impossible de compter, mais les volumes seront supprimes)"
fi

echo
echo "  Seront supprimes : les 8 bases, les topics Kafka et leurs offsets,"
echo "  le cache Redis, et le stockage objet (pieces des dossiers livreurs)."
echo
bleu "  L'administrateur sera RECREE au redemarrage, depuis le .env d'Identity."
echo "  La cle de signature d'Identity, elle, est dans un autre volume et survit."
echo

# ------------------------------------------------------------ Confirmation ---
printf 'Tapez %s pour confirmer (n%s\047importe quoi d\047autre annule) : ' "$mot" ""
read -r reponse

if [ "$reponse" != "$mot" ]; then
  jaune "Annule. Rien n'a ete touche."
  exit 0
fi

# --------------------------------------------------------- 1. On arrete tout --
etape "1. Arret des services"
# AVANT LA SUPPRESSION, ET PAS APRES. Un service encore debout pendant qu'on
# efface son volume ecrit dans une base qui disparait sous lui, et se retrouve
# au mieux en erreur, au pire avec un schema a moitie recree.
echecs=""
for d in $SERVICES; do
  printf -- '-- %s ' "$d"
  if docker compose -f "$d/docker-compose.yml" down >/dev/null 2>&1; then
    echo "arrete"
  else
    echo "ECHEC"
    echecs="$echecs $d"
  fi
done

# UN ARRET QUI ECHOUE NE SE TAIT PAS, ET C'EST UNE FAUTE QUE J'AI FAITE ICI.
# La premiere version ecrivait « || true » : l'echec disparaissait. Or un
# conteneur qui n'a pas ete arrete est TOUJOURS LA a l'etape 7, et « up -d » ne
# le recree pas s'il tourne deja — il repart donc avec son ancien environnement,
# celui d'avant la modification du .env. La panne apparait alors beaucoup plus
# loin, sous la forme d'une variable manquante que le .env contient pourtant.
if [ -n "$echecs" ]; then
  echo
  rouge "Ces piles ne se sont pas arretees :$echecs"
  rouge "Elles garderaient leur ancien environnement. Arretez-les a la main,"
  rouge "puis relancez. Rien n'a encore ete supprime."
  exit 1
fi

# ------------------------------------------------- 2. On supprime les volumes --
etape "2. Suppression des volumes de donnees"
# « down -v » sur la pile de dependances emporte pgdata, kafkadata, garagemeta
# et garagedata. Redis n'a pas de volume — « appendonly no » — donc ses cles
# meurent avec le conteneur. Le volume identity-keys appartient a la pile
# d'Identity et n'est PAS touche : la cle de signature survit, ce qui evite de
# la reposer.
docker compose -f "$compose" down -v

# ------------------------------------------------ 3. On redemarre l'infra ---
etape "3. Redemarrage des dependances"
docker network inspect hba-internal >/dev/null 2>&1 || docker network create hba-internal
docker compose -f "$compose" up -d

echo "   Attente de postgres…"
for _ in $(seq 1 60); do
  if docker compose -f "$compose" exec -T postgres pg_isready -U "${POSTGRES_USER:-hba}" >/dev/null 2>&1; then
    break
  fi
  sleep 2
done
docker compose -f "$compose" exec -T postgres pg_isready -U "${POSTGRES_USER:-hba}" >/dev/null 2>&1 \
  || { rouge "postgres n'est pas pret apres deux minutes. Regardez : make logs"; exit 1; }

echo "   Attente de kafka…"
for _ in $(seq 1 60); do
  if docker compose -f "$compose" ps --status running kafka >/dev/null 2>&1; then break; fi
  sleep 2
done
# Le conteneur « running » ne veut pas dire « broker pret » : kafka-init echoue
# parfois sur un broker qui ecoute sans avoir fini son election. On lui laisse
# un temps de grace plutot que de le voir echouer et croire les topics crees.
sleep 10

# ---------------------------------------------------------- 4. Les topics ---
etape "4. Recreation des topics Kafka"
docker compose -f "$compose" run --rm kafka-init

# ------------------------------------------------------ 5. Stockage objet ---
etape "5. Reinitialisation du stockage objet"
# LA CLE D'ACCES CHANGE, ET LE SCRIPT LA REPORTE LUI-MEME dans le .env de
# Driver. Sans cette etape, le service demarre avec l'ancienne cle et echoue au
# premier depot de piece — longtemps apres, sur un ecran qui ne dit pas
# pourquoi.
./scripts/garage-init.sh

# --------------------------------------------------------- 6. Migrations ---
etape "6. Application des migrations"
# EN PRODUCTION, Database:AutoMigrate VAUT FAUX : les services ne creent pas le
# schema au demarrage. Sans cette etape, ils demarrent sur des bases vides et
# echouent sur « relation does not exist ».
./scripts/apply-migrations.sh

# ------------------------------------------------------- 7. Les services ---
etape "7. Redemarrage des services"
# EN DERNIER, ET C'EST ICI QUE L'ADMIN REVIENT : AdminBootstrap tourne au
# demarrage d'Identity et ne cree le compte que s'il n'en existe aucun.
# « --force-recreate » POUR LA MEME RAISON QUE make rebuild : le .env n'est lu
# qu'a la CREATION du conteneur. Apres cette operation, la cle d'acces au
# stockage objet a change dans le .env de Driver — un conteneur reutilise
# repartirait avec l'ancienne, et echouerait au premier depot de piece.
for d in $SERVICES; do
  echo "-- $d"
  docker compose -f "$d/docker-compose.yml" up -d --force-recreate
done

etape "Termine"
echo "Verifiez que l'administrateur est bien revenu :"
echo
echo "  make fiche          # liste les cinq derniers comptes clients (vide, normal)"
echo "  docker compose -f $compose exec -T postgres \\"
echo "    psql -U ${POSTGRES_USER:-hba} -d hba_identity \\"
echo "    -c \"select \\\"Id\\\", email, roles from identity.accounts;\""
