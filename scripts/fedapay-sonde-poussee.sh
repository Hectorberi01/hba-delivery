#!/usr/bin/env bash
# Pourquoi FedaPay repond « Operation non autorisee » a la poussee.
#
# OU EN EST-ON. Le chemin est trouve : POST /v1/{mode} a la racine, avec mode
# parmi mtn_open, moov, sbin, mtn_ci, moov_tg, togocel, free_sn, airtel_ne,
# mtn_open_gn. Il a rendu 400 la ou les trois ecritures precedentes rendaient
# 404 — un 404 muet veut dire « route inconnue », un 400 veut dire « route
# connue, demande refusee ». On ne cherche donc plus le chemin.
#
# CE QUI RESTE A DISTINGUER, et le message du fournisseur ne le dit pas : son
# corps est « {"message":"Operation non autorisee","errors":{},"model":null} »,
# sans un seul champ nomme. Trois causes tiennent dans cette phrase :
#
#   1. L'OPERATEUR N'EST PAS OUVERT SUR LE COMPTE. Chaque methode de collecte
#      s'active cote fournisseur ; une methode fermee refuse l'operation sans
#      rien nommer.
#   2. LE NUMERO N'EST PAS DE CET OPERATEUR-LA. Pousser MTN vers un numero Moov
#      ne peut pas aboutir, et l'agregateur le refuse avant l'operateur.
#   3. LE CLIENT EST INCOMPLET. Nos transactions apparaissent au tableau de
#      bord sous « Unknown Unknown » : l'objet customer qu'on envoie ne porte
#      qu'un telephone. La documentation du fournisseur donne firstname,
#      lastname et email dans tous ses exemples, et precise que l'email doit
#      etre unique — ce qui laisse entendre que c'est LUI qui identifie le
#      client chez eux.
#
# COMMENT ON LES SEPARE. Une transaction NEUVE par essai, donc un jeton neuf :
# aucun essai ne peut etre explique par l'etat laisse par le precedent. On fait
# varier UNE chose a la fois — l'operateur, puis la completude du client — et
# on compare les reponses.
#
# LA CLE NE PASSE JAMAIS EN ARGUMENT. « ps » montre la ligne de commande de
# tous les processus. Elle est ecrite dans un fichier de configuration curl, en
# 600, efface a la sortie, et seul son prefixe de compte est affiche.
#
# CE SCRIPT CREE DE VRAIES TRANSACTIONS DANS LE BAC A SABLE. Aucune n'est
# payee ; elles expirent seules. Il refuse de tourner sur un compte « live ».
set -euo pipefail

cd "$(dirname "$0")/.."

ENV_FILE="src/Services/Payment/.env"
[ -f "$ENV_FILE" ] || { echo "Pas de $ENV_FILE : rien a sonder." >&2; exit 1; }

lire() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -1; }

CLE=$(lire FEDAPAY_SECRET_KEY)
MILIEU=$(lire FEDAPAY_ENVIRONMENT)
PAYS=$(lire FEDAPAY_CUSTOMER_COUNTRY)
: "${PAYS:=bj}"
: "${MILIEU:=sandbox}"

[ -n "$CLE" ] || { echo "FEDAPAY_SECRET_KEY absent de $ENV_FILE." >&2; exit 1; }
[ "$MILIEU" != "live" ] || { echo "FEDAPAY_ENVIRONMENT=live : cette sonde ne touche pas a un compte reel." >&2; exit 1; }

BASE="https://sandbox-api.fedapay.com/v1"

# LE NUMERO SE DONNE EN ARGUMENT, et il faut qu'il soit CELUI QUE TU AS SAISI
# DANS L'APP. Un numero d'exemple ne repondrait pas a la question 2.
NUMERO="${1:-+22997808080}"

echo "Cle     : $(printf '%s' "$CLE" | cut -d_ -f1-2)_… (${#CLE} caracteres)"
echo "API     : $BASE"
echo "Numero  : $NUMERO (pays $PAYS)"
echo

TRAVAIL=$(mktemp -d)
trap 'rm -rf "$TRAVAIL"' EXIT
CONF="$TRAVAIL/curl.conf"
umask 077
printf 'header = "Authorization: Bearer %s"\nheader = "Content-Type: application/json"\n' "$CLE" > "$CONF"

joli() { python3 -m json.tool 2>/dev/null || cat; }

appel() {
  local methode="$1" chemin="$2" corps="${3:-}"
  local arguments=(-sS -K "$CONF" -X "$methode" "$BASE/$chemin" --max-time 25 -w '\n%{http_code}')
  [ -n "$corps" ] && arguments+=(--data "$corps")
  local sortie
  sortie=$(curl "${arguments[@]}") || { echo "000"; echo "curl a echoue (reseau ?)"; return 0; }
  printf '%s\n' "$(printf '%s' "$sortie" | tail -1)"
  printf '%s' "$sortie" | sed '$d'
}

champ() { python3 -c '
import json,sys
d = json.load(sys.stdin)
t = d.get("v1/transaction") or d.get("transaction") or d
print(t.get(sys.argv[1]) or "")
' "$1" 2>/dev/null || true; }

# ── 1. La cle repond-elle ? ──────────────────────────────────────────────────
echo "══ 1. La cle repond-elle ?"
reponse=$(appel GET "transactions?per_page=1")
code=$(printf '%s\n' "$reponse" | head -1)
echo "   GET /transactions → HTTP $code"
if [ "$code" = "000" ]; then
  printf '%s\n' "$reponse" | tail -n +2
  echo "   FedaPay n'a pas ete joint : c'est le reseau de CETTE machine, pas la"
  echo "   cle. Relance depuis un terminal qui sort librement."
  exit 1
fi
[ "$code" = "200" ] || { printf '%s\n' "$reponse" | tail -n +2 | joli; exit 1; }
echo

# ── 2. Les essais ────────────────────────────────────────────────────────────
# Un essai = un operateur + une forme de client. Chacun refait une transaction.
essai() {
  local titre="$1" mode="$2" client_complet="$3"

  echo "══ $titre"

  local client
  if [ "$client_complet" = "oui" ]; then
    client=$(cat <<JSON
{ "firstname": "Sonde", "lastname": "HBA",
  "email": "sonde-$(date +%s)-$RANDOM@hba-delivery.test",
  "phone_number": { "number": "$NUMERO", "country": "$PAYS" } }
JSON
)
  else
    client=$(cat <<JSON
{ "phone_number": { "number": "$NUMERO", "country": "$PAYS" } }
JSON
)
  fi

  local reponse code corps id jeton
  reponse=$(appel POST "transactions" "$(cat <<JSON
{ "description": "Sonde HBA $mode", "amount": 100,
  "currency": { "iso": "XOF" }, "customer": $client }
JSON
)")
  code=$(printf '%s\n' "$reponse" | head -1)
  corps=$(printf '%s\n' "$reponse" | tail -n +2)
  if [ "$code" != "200" ] && [ "$code" != "201" ]; then
    echo "   Creation refusee (HTTP $code) :"
    printf '%s' "$corps" | joli
    echo
    return 0
  fi

  id=$(printf '%s' "$corps" | champ id)
  jeton=$(printf '%s' "$corps" | champ payment_token)

  if [ -z "$jeton" ]; then
    reponse=$(appel POST "transactions/$id/token")
    jeton=$(printf '%s\n' "$reponse" | tail -n +2 | champ token)
  fi

  # LE JETON EST UN SECRET DE COURTE VIE : qui l'a peut ouvrir la page de
  # paiement de cette transaction. Seule sa longueur sort.
  [ -n "$jeton" ] || { echo "   Transaction $id creee mais aucun jeton rendu."; echo; return 0; }
  echo "   Transaction $id, jeton de ${#jeton} caracteres."

  reponse=$(appel POST "$mode" "{ \"token\": \"$jeton\", \"phone_number\": { \"number\": \"$NUMERO\", \"country\": \"$PAYS\" } }")
  code=$(printf '%s\n' "$reponse" | head -1)
  echo "   POST /$mode → HTTP $code"
  printf '%s\n' "$reponse" | tail -n +2 | joli
  echo

  if [ "$code" = "200" ] || [ "$code" = "201" ]; then
    echo "   ⟵ ACCEPTE. Le telephone $NUMERO devrait sonner maintenant."
    echo
    REUSSI="$mode / client $client_complet"
  fi
}

REUSSI=""
essai "A. MTN, client complet  (nom + courriel + telephone)" "mtn_open" "oui"
[ -z "$REUSSI" ] && essai "B. MTN, client minimal  (telephone seul, comme le code aujourd'hui)" "mtn_open" "non"
[ -z "$REUSSI" ] && essai "C. Moov, client complet" "moov" "oui"

# ── 3. Lecture ───────────────────────────────────────────────────────────────
echo "══ Lecture"
if [ -n "$REUSSI" ]; then
  echo "   Un essai est passe : $REUSSI"
  echo "   Compare-le aux essais refuses : ce qui les separe est la cause."
else
  cat <<'FIN'
   Les trois essais sont refuses. Alors ce n'est ni l'operateur ni la forme du
   client : c'est le compte. « Operation non autorisee » designe une methode de
   collecte qui n'est pas ouverte cote fournisseur.

   DEUX ENDROITS OU LE VERIFIER, dans cet ordre :
     1. Le tableau de bord FedaPay, section des moyens de paiement : MTN et
        Moov y sont-ils actifs sur le compte sandbox ?
     2. Le support, si rien ne s'active seul :
        « Compte sandbox. POST /v1/mtn_open avec un jeton valide rend 400
          "Operation non autorisee" sans champ en erreur. Quelle activation
          manque-t-il pour la collecte directe sans page hebergee ? »
FIN
fi
