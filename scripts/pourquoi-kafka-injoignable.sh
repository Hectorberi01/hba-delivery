#!/usr/bin/env bash
#
# POURQUOI UN SERVICE NE RESOUT PAS « kafka » ?
#
# Le symptome est toujours le meme — « Failed to resolve 'kafka:9094': Name or
# service not known » — et il recouvre QUATRE causes qui ne se reparent pas de
# la meme facon :
#
#   1. Le broker n'est pas demarre. Le DNS interne de Docker ne connait que les
#      conteneurs VIVANTS : un kafka arrete n'a pas de nom, il n'a pas une
#      adresse injoignable. D'ou « Name or service not known » et non un refus
#      de connexion.
#   2. Le broker est mort au demarrage. Un disque plein laisse des repertoires
#      de journaux hors ligne, et le conteneur boucle sur « restart ».
#   3. LE RESEAU A ETE RECREE SOUS LES PIEDS DES CONTENEURS. C'est la cause la
#      plus traitre, parce que tout a l'air normal : « docker ps » montre les
#      deux conteneurs en vie, « docker network ls » montre hba-internal. Mais
#      un conteneur reste attache a l'IDENTIFIANT du reseau qu'il a rejoint au
#      demarrage. Si ce reseau a ete supprime — « docker system prune » emporte
#      les reseaux inutilises — puis recree par « make net », l'ancien
#      rattachement pend dans le vide et le DNS ne repond plus. Un simple
#      « restart » NE SUFFIT PAS : il faut RECREER le conteneur.
#   4. Le service n'est sur aucun reseau partage, par un compose modifie.
#
# Ce script les separe au lieu de les confondre. Il ne repare rien : il nomme.
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFRA="$RACINE/deploy/docker-compose.yml"
RESEAU="hba-internal"

command -v docker >/dev/null || { echo "docker est introuvable."; exit 1; }

echo "── Le reseau partage ─────────────────────────────────────────────"
if ! docker network inspect "$RESEAU" >/dev/null 2>&1; then
  echo "ABSENT. Aucun conteneur ne peut en nommer un autre."
  echo "  make net && make up && make topics"
  exit 0
fi

ID_RESEAU="$(docker network inspect "$RESEAU" --format '{{.Id}}')"
echo "$RESEAU present — $ID_RESEAU"
echo "Attaches, et vivants :"
docker network inspect "$RESEAU" \
  --format '{{range .Containers}}  {{.Name}}{{"\n"}}{{end}}' | sed '/^ *$/d'

echo
echo "── Le broker ─────────────────────────────────────────────────────"
ETAT_KAFKA="$(docker ps -a --filter 'name=kafka' --format '{{.Names}} :: {{.State}} :: {{.Status}}' || true)"
if [[ -z "$ETAT_KAFKA" ]]; then
  echo "AUCUN CONTENEUR KAFKA, meme arrete. L'infrastructure n'a jamais demarre."
  echo "  make up && make topics"
  exit 0
fi
echo "$ETAT_KAFKA"

NOM_KAFKA="$(docker ps -a --filter 'name=kafka' --filter 'name=^/.*kafka-[0-9]*$' --format '{{.Names}}' | head -1)"
NOM_KAFKA="${NOM_KAFKA:-$(docker ps -a --filter 'name=kafka' --format '{{.Names}}' | grep -v -- '-init\|-ui' | head -1)}"

if [[ -z "$(docker ps --filter "name=^/${NOM_KAFKA}$" --format '{{.Names}}')" ]]; then
  echo
  echo "LE BROKER EST ARRETE : c'est la cause, et le DNS n'y est pour rien."
  echo "Ses dernieres lignes, qui disent pourquoi il est tombe :"
  docker logs --tail 25 "$NOM_KAFKA" 2>&1 | sed 's/^/  /'
  echo
  echo "  make up"
  echo
  echo "S'IL REFUSE DE REPARTIR APRES UN DISQUE PLEIN, ses journaux sont"
  echo "probablement corrompus. Vider le volume « kafkadata » le fait repartir"
  echo "ET EFFACE TOUS LES EVENEMENTS NON ENCORE CONSOMMES — les boites"
  echo "d'envoi des services les republieront, les consommations deja faites"
  echo "sont perdues. C'est une decision, pas une manipulation de routine, et"
  echo "ce script ne la prend pas."
  exit 0
fi

echo
echo "── Le rattachement reseau, conteneur par conteneur ───────────────"
echo "UN IDENTIFIANT QUI DIFFERE DE CELUI DU HAUT EST LA PANNE : le conteneur"
echo "est reste sur un reseau supprime, et aucun redemarrage ne l'en sortira."
echo
for nom in $(docker ps --format '{{.Names}}'); do
  lignes="$(docker inspect "$nom" \
    --format '{{range $r, $c := .NetworkSettings.Networks}}{{$r}}={{$c.NetworkID}} {{end}}')"
  verdict=""
  case "$lignes" in
    *"$ID_RESEAU"*) verdict="ok" ;;
    *"$RESEAU="*)   verdict="DETACHE — reseau recree depuis son demarrage" ;;
    *)              verdict="hors $RESEAU" ;;
  esac
  printf '  %-34s %-9s %s\n' "$nom" "$verdict" "$lignes"
done

echo
echo "── La resolution, vue des services ───────────────────────────────"
# DEPUIS LE CONTENEUR, PAS DEPUIS L'HOTE. « ping kafka » sur le Mac echoue
# toujours et ne prouve rien : le nom n'existe que dans le reseau Docker.
for nom in $(docker ps --format '{{.Names}}' | grep -v 'kafka\|postgres\|redis\|garage\|osrm\|otel\|prometheus\|loki\|tempo\|grafana'); do
  if sortie="$(docker exec "$nom" getent hosts kafka 2>/dev/null)"; then
    printf '  %-34s resout kafka → %s\n' "$nom" "$(echo "$sortie" | awk '{print $1}')"
  else
    printf '  %-34s NE RESOUT PAS kafka\n' "$nom"
  fi
done

echo
echo "── Les topics ────────────────────────────────────────────────────"
# AUTO-CREATION DESACTIVEE : un topic manquant ne se cree pas tout seul, et le
# service echoue sur « Unknown topic or partition » — un message qui ne designe
# pas la cause. « make topics » rejoue la creation, qui est idempotente.
docker exec "$NOM_KAFKA" /opt/kafka/bin/kafka-topics.sh \
  --bootstrap-server localhost:9094 --list 2>/dev/null | sed 's/^/  /' \
  || echo "  Le broker ne repond pas encore a sa propre adresse."

echo
echo "── Ce qu'il faut faire ───────────────────────────────────────────"
echo "Un conteneur marque DETACHE se repare en le RECREANT, pas en le relancant :"
echo "  make rebuild D=src/Services/Dispatch        # ou le service concerne"
echo "  make relancer                               # tous, si plusieurs le sont"
echo
echo "Un service qui ne resout pas kafka alors que son rattachement est « ok »"
echo "et que le broker tourne : regarder KAFKA_BOOTSTRAP dans son .env."
