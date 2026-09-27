#!/usr/bin/env bash
#
# ETAPE 7 — VERIFIER QUE LES CHIFFRES DU TABLEAU DE BORD SONT JUSTES.
#
# Le tableau de bord traverse six couches : SQL, lecteur, handler, gRPC,
# passerelle, JSON. Chacune peut decaler un chiffre sans rien casser — une
# borne inclusive de trop, un fuseau qui derive, un statut oublie dans une
# somme. Rien n'echoue : le nombre est simplement faux, et personne ne le
# sait.
#
# Ce script compte deux fois. Une fois par la chaine complete, en appelant
# /api/admin/v1/kpi comme le navigateur. Une fois directement dans Postgres,
# avec un SQL ecrit a part. Puis il compare.
#
# LA FENETRE N'EST PAS RECALCULEE ICI : elle est lue dans la reponse, telle
# que la passerelle l'a appliquee. Recalculer les dates de son cote
# reviendrait a verifier un calcul avec le meme calcul.
#
#   ./scripts/etape7-verifier-kpi.sh
#   RANGE=7d ./scripts/etape7-verifier-kpi.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
RANGE="${RANGE:-30d}"
PG="${PG_CONTENEUR:-hba-delivery-dev-postgres-1}"
PG_USER="${POSTGRES_USER:-hba}"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"

bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[1;33m%s\033[0m\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }

echouer() { rouge "ECHEC : $*"; exit 1; }

ECARTS=0
COMPARAISONS=0
NON_NULS=0

# comparer <libelle> <attendu-sql> <obtenu-api>
comparer() {
  local libelle="$1" sql="${2:-}" api="${3:-}"

  sql="${sql:-0}"; api="${api:-0}"
  sql="${sql//[[:space:]]/}"; api="${api//[[:space:]]/}"

  COMPARAISONS=$((COMPARAISONS + 1))

  # Ni la base ni l'API ne doivent rendre autre chose qu'un nombre. Traiter
  # un « ERREUR-SQL » ou un « null » comme un zero rendrait le script muet
  # exactement la ou il sert.
  if ! [[ "$sql" =~ ^-?[0-9]+$ ]] || ! [[ "$api" =~ ^-?[0-9]+$ ]]; then
    printf '  \033[1;31m✗\033[0m %-38s base=%s  api=%s  (valeur non numerique)\n' \
      "$libelle" "$sql" "$api" >&2
    ECARTS=$((ECARTS + 1))
    return 0
  fi

  [ "$sql" != "0" ] && NON_NULS=$((NON_NULS + 1))

  if [ "$sql" = "$api" ]; then
    printf '  \033[1;32m✓\033[0m %-38s %s\n' "$libelle" "$api"
  else
    printf '  \033[1;31m✗\033[0m %-38s base=%s  api=%s\n' "$libelle" "$sql" "$api" >&2
    ECARTS=$((ECARTS + 1))
  fi
}

# sql <base> <requete> — un scalaire, sans en-tete ni alignement.
#
# UNE ERREUR SQL NE DOIT PAS SE LIRE COMME UN ZERO. Un nom de table change,
# une migration pas appliquee, et psql rend une chaine vide : sans ce garde,
# le script comparerait « 0 » contre « 0 » et annoncerait que tout va bien.
sql() {
  local sortie
  if ! sortie="$(docker exec -i "$PG" psql -U "$PG_USER" -d "$1" -At \
                   -v ON_ERROR_STOP=1 -c "$2" 2>&1)"; then
    rouge "ECHEC SQL sur $1 :"
    printf '%s\n' "$sortie" | sed 's/^/    /' >&2
    rouge "Requete : $2"
    printf 'ERREUR-SQL'
    return 0
  fi
  printf '%s' "$sortie" | head -1
}

# --------------------------------------------------------------- Prealables --

command -v jq >/dev/null || echouer "jq est requis (brew install jq)."
command -v docker >/dev/null || echouer "docker est requis."

docker exec "$PG" true >/dev/null 2>&1 \
  || echouer "le conteneur Postgres « $PG » est introuvable. Lancez « make up », ou passez PG_CONTENEUR=…"

curl -fsS --max-time 5 "$GATEWAY/health" >/dev/null \
  || echouer "la gateway ne repond pas sur $GATEWAY/health. Lancez « make up » puis « make services-up »."

# ------------------------------------------------------------- 1. Le jeton --

etape "1. Connexion de l'administrateur"

REPONSE="$(curl -sS -X POST "$GATEWAY/api/web/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"etape7\"}")"

JETON="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
[ -n "$JETON" ] || echouer "Identity refuse $ADMIN_EMAIL — reponse : $REPONSE
Si le compte existe deja avec un autre mot de passe, relancez Identity avec
Bootstrap__ResetPassword=true, ou passez BOOTSTRAP_ADMIN_PASSWORD=…"

vert "Jeton obtenu."

# ------------------------------------------------------- 2. La chaine complete --

etape "2. Lecture de /api/admin/v1/kpi?range=$RANGE"

KPI="$(curl -sS "$GATEWAY/api/admin/v1/kpi?range=$RANGE" -H "Authorization: Bearer $JETON")"
echo "$KPI" | jq -e . >/dev/null 2>&1 || echouer "reponse inattendue : $KPI"

INDISPONIBLES="$(echo "$KPI" | jq -r '(.unavailable // []) | join(", ")')"
if [ -n "$INDISPONIBLES" ]; then
  jaune "BLOCS INDISPONIBLES : $INDISPONIBLES"
  jaune "Ils ne seront pas verifies. Un bloc absent n'est pas un bloc a zero."
fi

DEBUT="$(echo "$KPI" | jq -r '.window.from')"
FIN="$(echo "$KPI" | jq -r '.window.to')"
[ "$DEBUT" != "null" ] || echouer "la reponse ne porte pas de fenetre appliquee."

bleu "Fenetre appliquee : $DEBUT  ->  $FIN   (bornes : debut inclus, fin exclue)"

# Les bornes partent telles quelles dans le SQL : ce sont des instants absolus,
# et timestamptz les compare correctement quel que soit le fuseau de la session.
B="'$DEBUT'::timestamptz"
F="'$FIN'::timestamptz"

a() { echo "$KPI" | jq -r "$1 // 0"; }
present() { [ "$(echo "$KPI" | jq -r "$1 | type")" != "null" ]; }

# ------------------------------------------------------------- 3. Courses --

if present '.deliveries'; then
  etape "3. Courses — base hba_delivery"

  comparer "Courses creees" \
    "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F")" \
    "$(a '.deliveries.total')"

  comparer "Livrees" \
    "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F AND \"Status\" = 9")" \
    "$(a '.deliveries.delivered')"

  # Closes : les cinq etats terminaux de DeliveryTransitions.Terminal.
  comparer "Closes" \
    "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F AND \"Status\" IN (2,5,9,10,11)")" \
    "$(a '.deliveries.closed')"

  comparer "Ouvertes" \
    "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F AND \"Status\" NOT IN (2,5,9,10,11)")" \
    "$(a '.deliveries.open')"

  comparer "Facture (XOF)" \
    "$(sql hba_delivery "SELECT coalesce(sum(price_total_xof),0) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F")" \
    "$(a '.deliveries.billedXof')"

  comparer "Somme des tranches par statut" \
    "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F")" \
    "$(a '[.deliveries.byStatus[]?.count // 0] | map(tonumber) | add')"

  comparer "Somme de la serie journaliere" \
    "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F")" \
    "$(a '[.deliveries.createdSeries[]?.value // 0] | map(tonumber) | add')"

  # LE SEUL TEST QUI ATTRAPE UNE DERIVE DE FUSEAU. Les totaux sont
  # insensibles au calendrier : c'est le decoupage en journees qui ne l'est
  # pas. Si le service decoupait en UTC au lieu d'Africa/Porto-Novo, les
  # courses d'avant 1 h du matin basculeraient dans la veille — le total
  # resterait juste, et la journee la plus chargee changerait de case.
  ZONE="${HBA_TIMEZONE:-Africa/Porto-Novo}"
  PIC_CLE="$(a '[.deliveries.createdSeries[]? | select((.value // 0 | tonumber) > 0)] | sort_by(.value | tonumber) | last | .key // ""')"

  if [ -n "$PIC_CLE" ] && [ "$PIC_CLE" != "null" ]; then
    comparer "Journee la plus chargee ($PIC_CLE, $ZONE)" \
      "$(sql hba_delivery "SELECT count(*) FROM delivery.deliveries WHERE \"CreatedAt\" >= $B AND \"CreatedAt\" < $F AND to_char(\"CreatedAt\" AT TIME ZONE '$ZONE', 'YYYY-MM-DD') = '$PIC_CLE'")" \
      "$(a "[.deliveries.createdSeries[]? | select(.key == \"$PIC_CLE\")] | first | .value // 0")"
  else
    jaune "  · aucune journee non vide : le decoupage par fuseau n'est pas verifiable."
  fi
fi

# ----------------------------------------------------------- 4. Paiements --

if present '.payments'; then
  etape "4. Paiements — base hba_payment"

  # COHORTE ET RECETTE NE SE COMPTENT PAS SUR LA MEME COLONNE. La cohorte
  # suit created_at : les intentions ouvertes dans la fenetre. La recette
  # suit succeeded_at : l'argent reellement recu pendant la fenetre, y
  # compris sur des intentions ouvertes avant. Les confondre est l'erreur
  # que le contrat nomme, et c'est ici qu'on la verifie.
  comparer "Intentions ouvertes (cohorte)" \
    "$(sql hba_payment "SELECT count(*) FROM payment.payment_intents WHERE created_at >= $B AND created_at < $F")" \
    "$(a '.payments.createdCount')"

  comparer "Montant de la cohorte (XOF)" \
    "$(sql hba_payment "SELECT coalesce(sum(amount),0) FROM payment.payment_intents WHERE created_at >= $B AND created_at < $F")" \
    "$(a '.payments.createdAmountXof')"

  comparer "Paiements encaisses (recette)" \
    "$(sql hba_payment "SELECT count(*) FROM payment.payment_intents WHERE succeeded_at >= $B AND succeeded_at < $F")" \
    "$(a '.payments.collectedCount')"

  comparer "Recette encaissee (XOF)" \
    "$(sql hba_payment "SELECT coalesce(sum(amount),0) FROM payment.payment_intents WHERE succeeded_at >= $B AND succeeded_at < $F")" \
    "$(a '.payments.collectedXof')"

  comparer "Somme de la serie d'encaissement" \
    "$(sql hba_payment "SELECT coalesce(sum(amount),0) FROM payment.payment_intents WHERE succeeded_at >= $B AND succeeded_at < $F")" \
    "$(a '[.payments.collectedSeries[]?.value // 0] | map(tonumber) | add')"

  comparer "Somme des tranches par statut" \
    "$(sql hba_payment "SELECT count(*) FROM payment.payment_intents WHERE created_at >= $B AND created_at < $F")" \
    "$(a '[.payments.createdByStatus[]?.count // 0] | map(tonumber) | add')"
fi

# ------------------------------------------------------------ 5. Dispatch --

if present '.dispatch'; then
  etape "5. Dispatch — base hba_dispatch"

  # RECHERCHES ET OFFRES NE PARTAGENT PAS LEUR DATE : la recherche est datee
  # de created_at, l'offre de sent_at. Une recherche ouverte a 23 h 50 peut
  # envoyer ses offres apres minuit.
  comparer "Recherches ouvertes" \
    "$(sql hba_dispatch "SELECT count(*) FROM dispatch.dispatches WHERE created_at >= $B AND created_at < $F")" \
    "$(a '.dispatch.dispatches')"

  comparer "Somme des tranches de recherche" \
    "$(sql hba_dispatch "SELECT count(*) FROM dispatch.dispatches WHERE created_at >= $B AND created_at < $F")" \
    "$(a '[.dispatch.assigned, .dispatch.exhausted, .dispatch.cancelled, .dispatch.stillSearching] | map(. // 0 | tonumber) | add')"

  comparer "Offres envoyees" \
    "$(sql hba_dispatch "SELECT count(*) FROM dispatch.offers WHERE sent_at >= $B AND sent_at < $F")" \
    "$(a '.dispatch.offersSent')"

  comparer "Somme des tranches d'offre" \
    "$(sql hba_dispatch "SELECT count(*) FROM dispatch.offers WHERE sent_at >= $B AND sent_at < $F")" \
    "$(a '[.dispatch.offersByStatus[]?.count // 0] | map(tonumber) | add')"

  comparer "Somme des vagues" \
    "$(sql hba_dispatch "SELECT count(*) FROM dispatch.offers WHERE sent_at >= $B AND sent_at < $F")" \
    "$(a '[.dispatch.acceptedByWave[]?.count // 0] | map(tonumber) | add')"
fi

# ------------------------------------------------------------ 6. Livreurs --

if present '.drivers'; then
  etape "6. Livreurs — base hba_driver"

  # LES EFFECTIFS N'ONT PAS DE FENETRE, LES FLUX EN ONT UNE. Compter les
  # inscrits « sur la periode » donnerait un annuaire qui retrecit quand on
  # raccourcit la fenetre — un livreur inscrit l'an dernier travaille
  # toujours aujourd'hui.
  comparer "Inscrits (hors fenetre)" \
    "$(sql hba_driver "SELECT count(*) FROM driver.drivers")" \
    "$(a '.drivers.total')"

  comparer "Somme des tranches de verification" \
    "$(sql hba_driver "SELECT count(*) FROM driver.drivers")" \
    "$(a '[.drivers.byVerification[]?.count // 0] | map(tonumber) | add')"

  comparer "Somme des tranches operationnelles" \
    "$(sql hba_driver "SELECT count(*) FROM driver.drivers")" \
    "$(a '[.drivers.byOperational[]?.count // 0] | map(tonumber) | add')"

  comparer "Inscriptions dans la fenetre" \
    "$(sql hba_driver "SELECT count(*) FROM driver.drivers WHERE registered_at >= $B AND registered_at < $F")" \
    "$(a '.drivers.registeredInWindow')"

  comparer "Validations dans la fenetre" \
    "$(sql hba_driver "SELECT count(*) FROM driver.drivers WHERE verified_at >= $B AND verified_at < $F")" \
    "$(a '.drivers.verifiedInWindow')"
fi

# ------------------------------------------------------------- 7. Verdict --

etape "7. Verdict"

printf '  %s comparaisons, dont %s sur un chiffre non nul.\n' "$COMPARAISONS" "$NON_NULS"

if [ "$ECARTS" -gt 0 ]; then
  rouge ""
  rouge "$ECARTS ECART(S). Le tableau de bord affiche autre chose que la base."
  rouge "Regardez d'abord les bornes : la fenetre prend >= debut et < fin."
  exit 1
fi

if [ "$NON_NULS" -eq 0 ]; then
  jaune ""
  jaune "TOUT CONCORDE, ET CELA NE PROUVE RIEN : chaque chiffre compare vaut zero."
  jaune "Sur une base vide, une requete fausse rend zero elle aussi."
  jaune "Lancez « make etape4 » quelques fois, puis relancez ce script."
  exit 2
fi

vert ""
vert "Les $COMPARAISONS chiffres du tableau de bord correspondent a la base."
if [ -n "$INDISPONIBLES" ]; then
  jaune "Non verifie, service absent : $INDISPONIBLES"
fi
