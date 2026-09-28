#!/usr/bin/env bash
#
# L'etat reel de la chaine webhook, de ngrok jusqu'au conteneur.
#
# POURQUOI CE SCRIPT EXISTE. Un webhook qui n'arrive pas ne produit AUCUNE
# erreur : la page de paiement s'ouvre, le client paie, et la livraison reste
# en attente. Il n'y a rien a lire dans les journaux du service, puisque le
# service n'a rien recu. La seule preuve se trouve en amont — chez ngrok, qui
# enregistre chaque requete recue, meme celles qui finissent en 404.
#
# QUATRE CHOSES SE DESYNCHRONISENT, ET AUCUNE NE PREVIENT :
#   1. ngrok n'est pas lance ;
#   2. son adresse a change, et le tableau de bord FedaPay pointe l'ancienne ;
#   3. le .env a la bonne adresse mais le conteneur porte encore l'ancienne,
#      « env_file » n'etant lu qu'a la CREATION du conteneur ;
#   4. le secret de webhook manque, et tout est refuse en 400.
#
#   ./scripts/webhook-etat.sh

set -euo pipefail

racine="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_paiement="$racine/src/Services/Payment/.env"
ngrok_api="http://127.0.0.1:4040/api"

echo "═══ 1. Le tunnel"
if ! curl -sf --max-time 3 "$ngrok_api/tunnels" -o /tmp/hba-tunnels.json 2>/dev/null; then
  echo "  ngrok ne repond pas sur 127.0.0.1:4040."
  echo "  Sans lui, FedaPay ne peut joindre personne :  make tunnel"
  exit 1
fi

publique="$(python3 -c '
import json
t = json.load(open("/tmp/hba-tunnels.json"))["tunnels"]
print(next((x["public_url"] for x in t if x["public_url"].startswith("https://")), ""))
')"

if [ -z "$publique" ]; then
  echo "  ngrok tourne mais n'expose aucune adresse https."
  exit 1
fi
echo "  adresse publique : $publique"
echo "  webhook attendu  : $publique/webhooks/fedapay"

echo
echo "═══ 2. Ce que le .env et le conteneur croient"
callback_env="$(sed -n 's/^FEDAPAY_CALLBACK_URL=//p' "$env_paiement" 2>/dev/null | tail -1)"
echo "  .env             : ${callback_env:-<absent>}"

if [ -n "${callback_env:-}" ] && [ "${callback_env#"$publique"}" = "$callback_env" ]; then
  echo "  DECALAGE : le .env ne pointe pas sur le tunnel courant.  make tunnel"
fi

if command -v docker >/dev/null 2>&1; then
  callback_conteneur="$(docker compose -f "$racine/src/Services/Payment/docker-compose.yml" \
    exec -T payment printenv FedaPay__CallbackUrl 2>/dev/null || true)"
  callback_conteneur="${callback_conteneur%$'\r'}"
  echo "  conteneur        : ${callback_conteneur:-<non lisible ou service arrete>}"

  if [ -n "$callback_conteneur" ] && [ -n "${callback_env:-}" ] \
     && [ "$callback_conteneur" != "$callback_env" ]; then
    # C'EST LE PIEGE QUE LE SCRIPT DE TUNNEL DOCUMENTE. Un « restart » relit le
    # code, jamais le env_file : la variable reste celle de la creation.
    echo "  DECALAGE : le conteneur porte une autre valeur que le .env."
    echo "  Un restart ne suffit pas, il faut RECREER :"
    echo "    docker compose -f src/Services/Payment/docker-compose.yml up -d --force-recreate payment"
  fi

  secret_present="$(docker compose -f "$racine/src/Services/Payment/docker-compose.yml" \
    exec -T payment sh -c 'test -n "$FedaPay__WebhookSecret" && echo oui || echo non' 2>/dev/null || echo '?')"
  secret_present="${secret_present%$'\r'}"
  echo "  secret webhook   : $secret_present (valeur jamais affichee)"
  if [ "$secret_present" = "non" ]; then
    echo "  SANS SECRET, TOUT EST REFUSE EN 400. Le verificateur refuse plutot"
    echo "  que de laisser n'importe qui marquer une course comme payee."
  fi
else
  echo "  docker introuvable : verification du conteneur ignoree."
fi

echo
echo "═══ 3. Ce que le tunnel a REELLEMENT recu"
curl -sf --max-time 5 "$ngrok_api/requests/http?limit=50" -o /tmp/hba-requetes.json 2>/dev/null || {
  echo "  Journal des requetes illisible."
  exit 1
}

python3 <<'PY'
import json

with open('/tmp/hba-requetes.json', encoding='utf-8') as f:
    requetes = json.load(f).get('requests', [])

webhooks = [r for r in requetes if '/webhooks/' in (r.get('request', {}).get('uri') or '')]

if not requetes:
    print('  Le tunnel n\'a recu AUCUNE requete depuis son ouverture.')
elif not webhooks:
    print('  %d requete(s) recue(s), mais AUCUNE sur /webhooks/.' % len(requetes))
    print()
    print('  FedaPay n\'appelle donc pas notre webhook. Les deux causes :')
    print('   - l\'adresse n\'est pas declaree dans le tableau de bord FedaPay ;')
    print('   - elle y est declaree, mais sous une ANCIENNE adresse ngrok.')
    print('  Tableau de bord FedaPay > Webhooks.')
else:
    # LE CODE DE REPONSE EST LE VERDICT. 200 : la signature tient et la
    # notification a ete traitee. 400 : elle est arrivee mais a ete refusee —
    # secret absent, signature fausse, ou horodatage hors tolerance. 404 : la
    # passerelle ne route pas, ou le tunnel ne vise pas le bon port.
    print('  %d appel(s) sur /webhooks/ :' % len(webhooks))
    print()
    for r in webhooks[:15]:
        req = r.get('request', {})
        rep = r.get('response', {}) or {}
        print('   %-24s %-6s %-42s -> %s' % (
            (r.get('start') or '')[:24],
            req.get('method', ''),
            req.get('uri', ''),
            rep.get('status', 'sans reponse')))
    print()
    codes = {}
    for r in webhooks:
        c = (r.get('response') or {}).get('status_code')
        codes[c] = codes.get(c, 0) + 1
    print('  Recapitulatif :', ', '.join('%s x%d' % (k, v) for k, v in sorted(codes.items(), key=lambda kv: str(kv[0]))))
PY
