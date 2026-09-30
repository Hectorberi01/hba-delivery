#!/usr/bin/env bash
# Demande a FedaPay ce qu'il est advenu d'une transaction, et sur quel compte.
#
# POURQUOI CE SCRIPT EXISTE. Quand la page de paiement affiche « Transaction
# echouee », elle ne dit pas pourquoi : le motif vit chez le fournisseur, dans
# « status » et « last_error_code ». Notre service le lit deja quand le webhook
# arrive — mais si rien n'arrive, personne ne va le chercher.
#
# LA PREMIERE VERSION MENTAIT, ET C'EST LA LECON. Elle ne regardait PAS le code
# HTTP : un 401 rend un corps JSON parfaitement valide, sans la cle attendue, et
# le lecteur en concluait « aucune transaction ». Un outil de diagnostic qui
# transforme une erreur d'authentification en constat metier est pire que pas
# d'outil du tout. Ici, tout ce qui n'est pas 200 est montre tel quel, et une
# reponse 200 dont la forme surprend fait AFFICHER SES CLES plutot que d'etre
# resumee a rien.
#
# LA CLE NE PASSE JAMAIS EN ARGUMENT : fichier de configuration curl en 600,
# efface a la sortie.
set -euo pipefail

cd "$(dirname "$0")/.."

ENV_FILE="src/Services/Payment/.env"
RECHERCHE="${1:-}"

[ -f "$ENV_FILE" ] || { echo "Pas de $ENV_FILE." >&2; exit 1; }

lire() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -1; }

CLE=$(lire FEDAPAY_SECRET_KEY)
MILIEU=$(lire FEDAPAY_ENVIRONMENT)
: "${MILIEU:=sandbox}"

[ -n "$CLE" ] || { echo "FEDAPAY_SECRET_KEY absent." >&2; exit 1; }

if [ "$MILIEU" = "live" ]; then
  BASE="https://api.fedapay.com/v1"
else
  BASE="https://sandbox-api.fedapay.com/v1"
fi

# LE PREFIXE DE LA CLE FACE A L'ENVIRONNEMENT : « sk_live_ » interroge le bac a
# sable, ou l'inverse, donne un 401 que rien d'autre n'explique. On montre les
# deux premiers champs de la cle, jamais sa partie aleatoire.
echo "Cle         : $(printf '%s' "$CLE" | cut -d_ -f1-2)_…  (${#CLE} caracteres)"
echo "Environnement : $MILIEU"
echo "API         : $BASE"
echo

TRAVAIL=$(mktemp -d)
trap 'rm -rf "$TRAVAIL"' EXIT
CONF="$TRAVAIL/curl.conf"
umask 077
printf 'header = "Authorization: Bearer %s"\n' "$CLE" > "$CONF"

# Rend le corps sur la sortie standard et le code HTTP sur le descripteur 3.
obtenir() {
  local chemin="$1" sortie code corps
  sortie=$(curl -sS -K "$CONF" --max-time 20 -w '\n%{http_code}' "$BASE/$chemin" 2>&1) || {
    echo "  curl a echoue sur /$chemin (reseau ?)" >&2
    return 1
  }

  code=$(printf '%s' "$sortie" | tail -1)
  corps=$(printf '%s' "$sortie" | sed '$d')

  if [ "$code" != "200" ]; then
    echo "  HTTP $code sur /$chemin — reponse du fournisseur :" >&2
    printf '%s\n' "$corps" | head -30 >&2
    return 1
  fi

  printf '%s' "$corps"
}

# LE POINT /accounts A ETE RETIRE, ET C'EST MOI QUI L'AVAIS MIS. Il rend 401
# avec une cle secrete : ce n'est pas la bonne porte, et son refus ne dit RIEN
# de l'etat du compte. Le laisser faisait croire a un probleme
# d'authentification alors que /transactions, lui, repondait 400 avec un
# message metier — donc authentifie.
#
# Pour connaitre les moyens de paiement actives sur le compte, la page de
# paiement elle-meme fait foi : sa liste d'operateurs EST cette information.

echo "── Transactions (/transactions/search)"
if corps=$(obtenir "transactions/search"); then
  printf '%s' "$corps" | python3 -c '
import json,sys
recherche = sys.argv[1] if len(sys.argv)>1 else ""
brut = sys.stdin.read()
try: d = json.loads(brut)
except Exception:
    print("  reponse non-JSON :"); print("  "+brut[:400]); raise SystemExit

# LA REPONSE EST UN TABLEAU NU. Le point /transactions/search ne lenveloppe
# pas, contrairement a la creation qui rend { "v1/transaction": {...} }.
# Attendre une enveloppe ici revient a lire une liste pleine comme une liste
# vide — ce qua fait la premiere version de ce script.
if isinstance(d, list):
    lot = d
else:
    lot = d.get("v1/transactions") or d.get("transactions") or d.get("data")
    if lot is None:
        print("  cles de la reponse :", ", ".join(d.keys()) or "(aucune)")
        print("  "+json.dumps(d, ensure_ascii=False)[:600])
        raise SystemExit

if not lot:
    print("  la liste est VIDE, et le fournisseur a bien repondu 200.")
    print("  Si une transaction existe, elle appartient a un AUTRE compte.")
    raise SystemExit

print("  %d transaction(s) sur ce compte." % len(lot))

# COMBIEN ONT ABOUTI, TOUS ESSAIS CONFONDUS. Une transaction refusee ne dit
# rien ; vingt-cinq refusees et zero approuvee disent que le parcours na
# JAMAIS marche, ce qui est une information toute differente.
tally = {}
for t in lot:
    st = str(t.get("status"))
    tally[st] = tally.get(st, 0) + 1
print("  statuts     :", ", ".join("%s=%d" % kv for kv in sorted(tally.items())))

def montrer(t):
    print("  ─────────────────────────────────────────")
    print("  id          :", t.get("id"))
    print("  reference   :", t.get("reference"))
    print("  montant     :", t.get("amount"))
    print("  statut      :", t.get("status"))
    print("  mode        :", t.get("mode"))
    print("  erreur      :", t.get("last_error_code"))
    # CE QUE COUTE LENCAISSEMENT, ENFIN LISIBLE : le F qui manquait au modele
    # economique. Commission, part fixe, frais, et ce qui reste transfere.
    print("  commission  :", t.get("commission"), "| fixe:", t.get("fixed_commission"), "| frais:", t.get("fees"))
    print("  transfere   :", t.get("amount_transferred"), "| debite:", t.get("amount_debited"))
    c = t.get("customer") or {}
    tel = c.get("phone_number") or {}
    print("  client      :", c.get("email"), tel.get("number"), tel.get("country"))
    print("  metadonnees :", t.get("custom_metadata"))
    print("  cree le     :", t.get("created_at"))

trouvee = [t for t in lot
           if recherche and (recherche == str(t.get("reference")) or recherche == str(t.get("id")))]

if trouvee:
    for t in trouvee: montrer(t)
else:
    if recherche:
        print("  Aucune ne porte %s. Les plus recentes :" % recherche)
    for t in sorted(lot, key=lambda x: str(x.get("created_at")), reverse=True)[:5]:
        montrer(t)
' "$RECHERCHE"
fi

echo
echo "─────────────────────────────────────────────────────────────────"
echo "Ce qu'il faut regarder :"
echo "  - last_error_code : le motif du refus, chez l operateur"
echo "  - mode            : l operateur reellement sollicite"
echo "  - commission/fees : ce que l encaissement coute REELLEMENT a HBA"
echo "  - liste vide      : la transaction appartient a un autre compte"
echo "  - HTTP 401 ici    : la cle ne correspond pas a cet environnement"
