#!/usr/bin/env bash
# Ouvre un tunnel ngrok vers la gateway et cable FedaPay dessus.
#
# POURQUOI CE SCRIPT EXISTE. FedaPay doit joindre notre webhook depuis
# l'exterieur, et renvoyer le payeur sur une adresse publique. En local,
# 127.0.0.1:5100 ne remplit ni l'une ni l'autre condition — et l'echec est
# silencieux : la page de paiement s'ouvre, le client paie, et la livraison
# reste en PENDING_PAYMENT sans qu'aucun journal ne mentionne d'erreur.
#
# Trois choses sont faites ici, et la troisieme est celle qu'on oublie :
#   1. demarrer ngrok sur le port de la gateway ;
#   2. ecrire FEDAPAY_CALLBACK_URL dans le .env du service Payment ;
#   3. RECREER le conteneur payment. « env_file » est lu a la CREATION du
#      conteneur, pas a son demarrage : un simple restart garde l'ancienne
#      valeur, et on cherche pendant une demi-heure pourquoi la variable
#      « qui est pourtant dans le .env » n'arrive pas.
set -euo pipefail

cd "$(dirname "$0")/.."

PORT="${GATEWAY_PORT:-5100}"
PAYMENT_ENV="src/Services/Payment/.env"
NGROK_API="http://127.0.0.1:4040/api/tunnels"

# Le domaine fixe vit dans le .env du service, pas dans ce script : il est
# propre au poste, et ce fichier-ci est versionne.
if [ -z "${NGROK_DOMAIN:-}" ] && [ -f "$PAYMENT_ENV" ]; then
  NGROK_DOMAIN=$(sed -n 's/^NGROK_DOMAIN=//p' "$PAYMENT_ENV" | tail -1)
fi

if ! command -v ngrok >/dev/null 2>&1; then
  echo "ngrok introuvable. Sur macOS : brew install ngrok" >&2
  echo "Puis, une seule fois : ngrok config add-authtoken <votre-jeton>" >&2
  exit 1
fi

# La gateway doit deja ecouter : ngrok ouvrirait sinon un tunnel vers le vide,
# et FedaPay recevrait des 502 qu'il compterait dans ses neuf tentatives.
if ! (exec 3<>/dev/tcp/127.0.0.1/"$PORT") 2>/dev/null; then
  echo "Rien n'ecoute sur 127.0.0.1:$PORT — la gateway n'est pas demarree." >&2
  echo >&2
  echo "  cd src/Gateways/Hba.Gateway && docker compose up -d" >&2
  exit 1
fi

# ngrok tourne peut-etre deja : son API locale le dit, et en relancer un second
# echouerait sur le port 4040 sans expliquer pourquoi.
if curl -sf --max-time 2 "$NGROK_API" >/dev/null 2>&1; then
  echo "ngrok tourne deja, on reprend son tunnel."
else
  echo "Demarrage de ngrok sur le port ${PORT}…"
  mkdir -p .ngrok
  if [ -n "${NGROK_DOMAIN:-}" ]; then
    # UN DOMAINE STATIQUE EVITE DE TOUT RECONFIGURER A CHAQUE FOIS. ngrok en
    # offre un par compte gratuit ; sans lui, l'adresse change a chaque
    # redemarrage et il faut la recoller dans le tableau de bord FedaPay.
    #
    # DEUX SYNTAXES COEXISTENT selon la version de l'agent : « --url » sur les
    # recentes, « --domain » avant. On tente la premiere et on retombe sur la
    # seconde, plutot que de laisser l'utilisateur devant « unknown flag ».
    echo "Domaine fixe : $NGROK_DOMAIN"
    ngrok http --url="$NGROK_DOMAIN" "$PORT" --log=stdout > .ngrok/ngrok.log 2>&1 &
    sleep 2
    if grep -qi "unknown flag\|unknown shorthand" .ngrok/ngrok.log 2>/dev/null; then
      ngrok http --domain="$NGROK_DOMAIN" "$PORT" --log=stdout > .ngrok/ngrok.log 2>&1 &
    fi
  else
    ngrok http "$PORT" --log=stdout > .ngrok/ngrok.log 2>&1 &
  fi

  for _ in $(seq 1 30); do
    curl -sf --max-time 2 "$NGROK_API" >/dev/null 2>&1 && break
    sleep 1
  done
fi

PUBLIC_URL=$(curl -sf --max-time 5 "$NGROK_API" \
  | python3 -c 'import json,sys; t=json.load(sys.stdin)["tunnels"]; print(next((x["public_url"] for x in t if x["public_url"].startswith("https://")), ""))')

if [ -z "$PUBLIC_URL" ]; then
  echo "ngrok n'a pas rendu d'adresse publique. Journal : .ngrok/ngrok.log" >&2
  tail -20 .ngrok/ngrok.log >&2 2>/dev/null || true
  exit 1
fi

CALLBACK="$PUBLIC_URL/paiement/retour"
WEBHOOK="$PUBLIC_URL/webhooks/fedapay"

touch "$PAYMENT_ENV"
if grep -q '^FEDAPAY_CALLBACK_URL=' "$PAYMENT_ENV"; then
  # -i '' est la forme BSD, celle de macOS.
  sed -i '' "s|^FEDAPAY_CALLBACK_URL=.*|FEDAPAY_CALLBACK_URL=$CALLBACK|" "$PAYMENT_ENV"
else
  printf '\nFEDAPAY_CALLBACK_URL=%s\n' "$CALLBACK" >> "$PAYMENT_ENV"
fi

echo
echo "Tunnel ouvert : $PUBLIC_URL"
echo "  retour payeur : $CALLBACK   (ecrit dans $PAYMENT_ENV)"
echo "  webhook       : $WEBHOOK"
echo

# « --build » EN PLUS DE « --force-recreate », ET LES DEUX SERVENT.
# force-recreate fait relire le .env, qui n'est lu qu'a la creation du
# conteneur. build fait reprendre le code : sans lui, on recree fidelement
# l'ANCIENNE image, et le service redemarre avec du code d'avant — panne
# d'autant plus deroutante que la variable, elle, est bien arrivee.
# Le cache de docker rend l'etape quasi gratuite quand rien n'a change.
echo "Reconstruction et recreation du conteneur payment…"
docker compose -f src/Services/Payment/docker-compose.yml up -d --build --force-recreate payment

echo
echo "─────────────────────────────────────────────────────────────────"
echo "A FAIRE A LA MAIN, une seule fois par adresse :"
echo "  Tableau de bord FedaPay > Webhooks > declarer"
echo "    $WEBHOOK"
echo "  puis recopier le secret genere dans FEDAPAY_WEBHOOK_SECRET"
echo "  ($PAYMENT_ENV) et relancer ce script."
echo
echo "L'adresse ngrok change a chaque redemarrage, sauf domaine statique :"
echo "  NGROK_DOMAIN=xxx.ngrok-free.app make tunnel"
echo "─────────────────────────────────────────────────────────────────"
