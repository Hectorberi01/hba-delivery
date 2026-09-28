#!/usr/bin/env bash
#
# Les dernieres transactions vues par FedaPay, telles que FedaPay les voit.
#
# POURQUOI CE SCRIPT EXISTE. Quand un paiement echoue, la page du fournisseur
# dit « Transaction echouee. Veuillez reessayer » et rien d'autre. La raison,
# elle, est dans l'objet transaction cote FedaPay : son statut, son mode, et le
# numero de telephone que le fournisseur a retenu pour le client. Deviner a
# partir du message de la page fait perdre une demi-journee.
#
# LA CLE N'EST JAMAIS AFFICHEE NI PASSEE EN ARGUMENT. Elle est lue dans le .env
# du service, qui n'est pas suivi par git. Un secret passe en argument se
# retrouve dans l'historique du shell et dans la liste des processus.
#
#   ./scripts/fedapay-transactions.sh          # les 3 dernieres
#   ./scripts/fedapay-transactions.sh 10       # les 10 dernieres
#   ./scripts/fedapay-transactions.sh 3 brut   # la reponse JSON complete

set -euo pipefail

nombre="${1:-3}"
forme="${2:-resume}"

racine="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_paiement="$racine/src/Services/Payment/.env"

if [ ! -f "$env_paiement" ]; then
  echo "Fichier introuvable : $env_paiement" >&2
  echo "Copiez .env.example en .env et renseignez FEDAPAY_SECRET_KEY." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
. "$env_paiement"
set +a

if [ -z "${FEDAPAY_SECRET_KEY:-}" ]; then
  echo "FEDAPAY_SECRET_KEY est absent de $env_paiement." >&2
  exit 1
fi

# L'HOTE DEPEND DU MODE, et se tromper d'hote donne un 401 qu'on lit comme une
# mauvaise cle. Une cle de bac a sable n'ouvre rien en production, et
# reciproquement.
if [ "${FEDAPAY_ENVIRONMENT:-sandbox}" = "live" ]; then
  hote="https://api.fedapay.com/v1"
else
  hote="https://sandbox-api.fedapay.com/v1"
fi

echo "Environnement : ${FEDAPAY_ENVIRONMENT:-sandbox}  ($hote)"
echo

# LE POINT DE LISTE A CHANGE, ET FEDAPAY NE DIT PAS LEQUEL LE REMPLACE
# EXACTEMENT. Au 28 septembre 2026, « GET /v1/transactions » repond :
#
#     HTTP 400
#     Invoquer /v1/transactions est obsolete. Utiliser plutot
#     /v1/transactions/search.
#
# La documentation publique de FedaPay ne decrit pas ce point : ni sa methode,
# ni son corps de requete. On ESSAIE donc, dans l'ordre, les trois formes
# plausibles, et le script dit laquelle a repondu. Coder une seule forme au
# juge aurait produit un second message d'erreur aussi opaque que le premier.
#
# CONSTATE LE 28 SEPTEMBRE 2026 : c'est la premiere forme qui repond,
# « GET /v1/transactions/search?per_page=N ». Les deux autres restent la
# volontairement — le jour ou celle-ci changera a son tour, le script le dira
# au lieu de s'arreter sur un code d'erreur.
essais=(
  "GET|$hote/transactions/search?per_page=$nombre|"
  "POST|$hote/transactions/search|{}"
  "GET|$hote/transactions?per_page=$nombre|"
)

reponse="$(mktemp)"
trap 'rm -f "$reponse"' EXIT

code=""
retenu=""

for essai in "${essais[@]}"; do
  methode="${essai%%|*}"
  reste="${essai#*|}"
  adresse="${reste%%|*}"
  corps="${reste#*|}"

  if [ -n "$corps" ]; then
    code="$(curl -sS --max-time 30 -o "$reponse" -w '%{http_code}' \
      -X "$methode" \
      -H "Authorization: Bearer $FEDAPAY_SECRET_KEY" \
      -H 'Accept: application/json' \
      -H 'Content-Type: application/json' \
      -d "$corps" \
      "$adresse")" || code="000"
  else
    code="$(curl -sS --max-time 30 -o "$reponse" -w '%{http_code}' \
      -X "$methode" \
      -H "Authorization: Bearer $FEDAPAY_SECRET_KEY" \
      -H 'Accept: application/json' \
      "$adresse")" || code="000"
  fi

  echo "  $methode ${adresse#"$hote"} -> HTTP $code"

  if [ "$code" = "200" ]; then
    retenu="$methode ${adresse#"$hote"}"
    break
  fi
done

echo

if [ "$code" != "200" ]; then
  echo "Aucune forme n'a abouti. Derniere reponse brute :" >&2
  cat "$reponse" >&2
  echo >&2
  exit 1
fi

echo "Point retenu : $retenu"
echo

# ET MAINTENANT LE POINT DONT NOTRE SERVICE DEPEND VRAIMENT.
#
# « GET /v1/transactions/{id} » N'EST PAS LE MEME CHEMIN QUE LA LISTE, mais il
# commence pareil — et c'est la liste que FedaPay vient de declarer obsolete.
# Or FedaPayClient.GetPaymentAsync appelle ce point a CHAQUE rappel de webhook,
# pour relire le statut a la source au lieu de croire le corps du message. S'il
# disparaissait a son tour, plus aucun paiement ne serait confirme, et le seul
# symptome serait des courses qui restent en attente de paiement.
#
# On le sonde donc ici, sur la premiere transaction rendue. Une ligne de
# journal vaut mieux qu'une supposition rassurante.
premier="$(python3 -c "
import json, sys
c = json.load(open(sys.argv[1], encoding='utf-8'))
for cle in ('v1/transactions', 'transactions', 'data'):
    if isinstance(c.get(cle), list) and c[cle]:
        print(c[cle][0].get('id', ''))
        break
" "$reponse" 2>/dev/null || true)"

if [ -n "$premier" ]; then
  detail="$(mktemp)"
  code_detail="$(curl -sS --max-time 20 -o "$detail" -w '%{http_code}' \
    -H "Authorization: Bearer $FEDAPAY_SECRET_KEY" \
    -H 'Accept: application/json' \
    "$hote/transactions/$premier")" || code_detail="000"

  if [ "$code_detail" = "200" ]; then
    echo "Chemin critique OK : GET /transactions/$premier -> HTTP 200"
    echo "(c'est celui que le service relit a chaque webhook)"
  else
    echo "ALERTE : GET /transactions/$premier -> HTTP $code_detail" >&2
    echo "Ce point est appele a chaque webhook par FedaPayClient.GetPaymentAsync." >&2
    echo "S'il ne repond plus, aucun paiement ne sera confirme. Reponse brute :" >&2
    cat "$detail" >&2
    echo >&2
  fi
  rm -f "$detail"
fi

echo

if [ "$forme" = "brut" ]; then
  python3 -m json.tool < "$reponse"
  exit 0
fi

python3 - "$reponse" <<'PY'
import json, sys

with open(sys.argv[1], encoding='utf-8') as f:
    corps = json.load(f)

# LA CLE DE PREMIER NIVEAU PORTE LE CHEMIN, pas un nom stable : la reponse
# arrive sous « v1/transactions ». On accepte les formes voisines plutot que de
# casser au prochain changement mineur du fournisseur.
lignes = None
for cle in ('v1/transactions', 'transactions', 'data'):
    if isinstance(corps.get(cle), list):
        lignes = corps[cle]
        break
if lignes is None:
    lignes = corps if isinstance(corps, list) else [corps]

if not lignes:
    print('Aucune transaction.')
    raise SystemExit

for t in lignes:
    if not isinstance(t, dict):
        continue

    print('--- transaction %s' % t.get('id'))
    for champ in ('reference', 'amount', 'status', 'mode', 'operation',
                  'last_error_code', 'customer_id', 'description',
                  'created_at', 'approved_at'):
        if t.get(champ) is not None:
            print('    %-16s %s' % (champ, t[champ]))

    client = t.get('customer')
    if isinstance(client, dict):
        # LE NUMERO RETENU PAR FEDAPAY EST LE POINT QUI COMPTE : c'est lui qui
        # dit si l'on a envoye le payeur ou quelqu'un d'autre.
        tel = client.get('phone_number')
        if isinstance(tel, dict):
            tel = '%s (%s)' % (tel.get('number'), tel.get('country'))
        print('    %-16s %s %s — %s' % (
            'client',
            client.get('firstname') or '',
            client.get('lastname') or '',
            tel or client.get('phone_number_str') or 'sans numero'))

    meta = t.get('custom_metadata')
    if isinstance(meta, dict) and meta:
        print('    %-16s %s' % ('metadonnees', meta))

    print()
PY
