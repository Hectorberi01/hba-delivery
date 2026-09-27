#!/usr/bin/env bash
#
# L'ANNUAIRE CLIENT, ET SURTOUT SA GRADUATION PAR ROLE.
#
# CE QUE CE SCRIPT VIENT MESURER, et que rien d'autre ne mesure : qu'un
# operateur (ops) ouvre la fiche d'un client SANS voir son adresse e-mail ni
# ses adresses enregistrees, que finance n'ouvre pas l'annuaire du tout, et
# que le cumul facture ne sorte que pour admin. Ces trois regles n'existent
# que dans DirectoryAccess et GetCustomerBillingHandler ; sans ce script,
# rien ne dit qu'elles tiennent.
#
# IL FABRIQUE SES PROPRES COMPTES ops ET finance. Les tester avec le jeton
# d'admin ne prouverait rien : c'est precisement la difference entre les
# roles qui est en jeu.
#
#   ./scripts/clients-annuaire.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
IDENTITY_DIR="src/Services/Identity"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"
MOT_DE_PASSE="HbaDev!2026"

HORODATAGE="$(date +%s)"
SUFFIXE="${HORODATAGE: -6}"
TEL_CLIENT="${TEL_CLIENT:-+2290195$SUFFIXE}"
EMAIL_CLIENT="client$SUFFIXE@exemple.bj"

vert()  { printf '  \033[1;32m✓\033[0m %s\n' "$*"; }
jaune() { printf '  \033[1;33m!\033[0m %s\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }
echouer() { rouge "ECHEC : $*"; exit 1; }

attendu() {
  if [ "$2" = "$3" ]; then
    printf '  \033[1;32m✓\033[0m %-52s %s\n' "$1" "$3"
  else
    printf '  \033[1;31m✗\033[0m %-52s attendu «%s», obtenu «%s»\n' "$1" "$2" "$3" >&2
    exit 1
  fi
}

TEMPO="$(mktemp -d)"
command -v jq >/dev/null || echouer "jq est requis."
command -v docker >/dev/null || echouer "docker est requis."

# LE JOURNAL SE VERIFIE EN BASE, PAS PAR UNE ROUTE. Aucun ecran ne le lit
# encore : c'est le seul moyen de prouver qu'une ligne a bien ete ecrite.
# UN 500 PORTE UNE REFERENCE, ET PERSONNE NE PENSE A LA COMMANDE QUI LA
# RETROUVE au moment ou le script casse. Il la lance donc lui-meme.
journaux() {
  local service="$1" reference="${2:-}"
  rouge ""
  rouge "Journaux de $service :"

  local DC="docker compose -f src/Services/$service/docker-compose.yml"
  local trouve=""

  if [ -n "$reference" ]; then
    trouve="$($DC logs --tail 600 2>&1 | grep -F "$reference" | tail -3 || true)"
    [ -n "$trouve" ] && rouge "(lignes portant la reference $reference)"
  fi

  if [ -z "$trouve" ]; then
    trouve="$($DC logs --tail 600 2>&1 \
      | grep -Ei 'Exception|Unhandled|"@l":"(Error|Fatal)"' | tail -5 || true)"
  fi

  if [ -n "$trouve" ]; then
    printf '%s\n' "$trouve" | cut -c1-1200 | sed 's/^/    /' >&2
  else
    rouge "(rien de cible ; voici les vingt dernieres lignes)"
    $DC logs --tail 20 2>&1 | sed 's/^/    /' >&2
  fi
}

sql() {
  docker compose -f deploy/docker-compose.yml exec -T postgres \
    psql -tAq -U "${POSTGRES_USER:-hba}" -d "$1" -c "$2" 2>/dev/null | tr -d '[:space:]'
}

# --------------------------------------------------------- Images a jour --

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  etape "0. Construction (SKIP_BUILD=1 pour sauter)"
  for service in Identity Directory Delivery; do
    printf '  %s…\n' "$service"
    docker compose -f "src/Services/$service/docker-compose.yml" build >/dev/null \
      || echouer "la construction de $service a echoue."
  done
  printf '  Gateway…\n'
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build >/dev/null \
    || echouer "la construction de la gateway a echoue."

  for service in Directory Delivery; do
    docker compose -f "src/Services/$service/docker-compose.yml" up -d --force-recreate >/dev/null
  done
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml up -d --force-recreate >/dev/null
  bleu "Attente du redemarrage…"
  sleep 10
fi

restaurer() {
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" \
    up -d --force-recreate identity >/dev/null 2>&1 || true
  rm -rf "$TEMPO"
}
trap restaurer EXIT

etape "0 bis. Identity en code OTP fixe"
docker compose -f "$IDENTITY_DIR/docker-compose.yml" \
  -f "$IDENTITY_DIR/compose.dev-otp.yml" up -d --force-recreate identity >/dev/null
sleep 10

# ------------------------------------------------------------- 1. Routes --

etape "1. Les routes de l'annuaire existent-elles ?"
# Sans jeton, une route qui existe rend 401 ; une route absente rend 404.
for chemin in "/api/admin/v1/customers" "/api/admin/v1/customers/x" "/api/admin/v1/customers/x/billing"; do
  code="$(curl -sS -o /dev/null -w '%{http_code}' "$GATEWAY$chemin")"
  [ "$code" != "404" ] || echouer "$chemin est absente : image de passerelle perimee."
  attendu "$chemin" "401" "$code"
done

# ------------------------------------------------------------ 2. Comptes --

connexion() {
  curl -sS -X POST "$GATEWAY/api/web/v1/auth/login" -H 'Content-Type: application/json' \
    -d "{\"login\":\"$1\",\"password\":\"$2\",\"deviceId\":\"clients\"}" | jq -r '.accessToken // empty'
}

etape "2. Administrateur"
ADMIN="$(connexion "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
[ -n "$ADMIN" ] || echouer "Identity refuse $ADMIN_EMAIL."
vert "Connecte."

creer_back_office() {
  local email="$1" nom="$2" role="$3" code
  code="$(curl -sS -o "$TEMPO/compte.json" -w '%{http_code}' \
    -X POST "$GATEWAY/api/admin/v1/accounts/back-office" \
    -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"displayName\":\"$nom\",\"roles\":[\"$role\"],\"initialPassword\":\"$MOT_DE_PASSE\"}")"
  case "$code" in
    200|201) ;;
    *) rouge "Creation du compte $role refusee ($code) :"; cat "$TEMPO/compte.json" >&2; exit 1 ;;
  esac
}

etape "3. Un compte ops et un compte finance"
EMAIL_OPS="ops$SUFFIXE@hba.local"
EMAIL_FIN="finance$SUFFIXE@hba.local"
creer_back_office "$EMAIL_OPS" "Ops Test" "ops"
creer_back_office "$EMAIL_FIN" "Finance Test" "finance"

OPS="$(connexion "$EMAIL_OPS" "$MOT_DE_PASSE")"
FINANCE="$(connexion "$EMAIL_FIN" "$MOT_DE_PASSE")"
[ -n "$OPS" ] || echouer "connexion ops impossible."
[ -n "$FINANCE" ] || echouer "connexion finance impossible."
vert "ops et finance connectes."

# ------------------------------------------------------------ 4. Client --

etape "4. Un client, avec e-mail et adresse enregistree"
DEFI="$(curl -sS -X POST "$GATEWAY/api/client/v1/auth/otp/request" -H 'Content-Type: application/json' \
  -d "{\"phone\":\"$TEL_CLIENT\",\"deviceId\":\"clients\"}" | jq -r '.challengeId // empty')"
[ -n "$DEFI" ] || echouer "pas de defi OTP client."

REPONSE="$(curl -sS -X POST "$GATEWAY/api/client/v1/auth/otp/verify" -H 'Content-Type: application/json' \
  -d "{\"challengeId\":\"$DEFI\",\"code\":\"000000\",\"deviceId\":\"clients\",\"displayName\":\"Client Annuaire\"}")"
CLIENT="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
ID_CLIENT="$(echo "$REPONSE" | jq -r '.principal.subjectId // empty')"
[ -n "$CLIENT" ] || echouer "pas de jeton client — $REPONSE"
[ -n "$ID_CLIENT" ] || echouer "le jeton ne porte pas d'identifiant — $REPONSE"

bleu "Attente de la creation du profil par Directory (Kafka)…"
sleep 8

curl -sS -o /dev/null -X PUT "$GATEWAY/api/client/v1/me" -H "Authorization: Bearer $CLIENT" \
  -H 'Content-Type: application/json' \
  -d "{\"displayName\":\"Client Annuaire\",\"email\":\"$EMAIL_CLIENT\"}"

CODE="$(curl -sS -o "$TEMPO/adresse.json" -w '%{http_code}' \
  -X POST "$GATEWAY/api/client/v1/addresses" -H "Authorization: Bearer $CLIENT" \
  -H 'Content-Type: application/json' \
  -d '{"label":"Maison","latitude":6.3703,"longitude":2.3912,"landmark":"Carre 442, face a la pharmacie","phone":"'"$TEL_CLIENT"'","contactName":"Client Annuaire","notes":null,"setAsDefault":true}')"
if [ "$CODE" != "200" ]; then
  rouge "Ajout d'adresse refuse ($CODE) :"
  cat "$TEMPO/adresse.json" >&2
  journaux Directory "$(jq -r '.message // ""' "$TEMPO/adresse.json" | grep -oE '[0-9a-f]{32}' || true)"
  exit 1
fi
vert "Client $ID_CLIENT"

# --------------------------------------------------- 5. L'annuaire, admin --

annuaire() { curl -sS "$GATEWAY/api/admin/v1/customers?query=$1" -H "Authorization: Bearer $2"; }

etape "5. L'annuaire trouve le client, et masque son numero"
FRAGMENT="${TEL_CLIENT: -6}"
LISTE="$(annuaire "$FRAGMENT" "$ADMIN")"
LIGNE="$(echo "$LISTE" | jq -c '[.customers[] | select(.customerId=="'"$ID_CLIENT"'")][0] // empty')"
[ -n "$LIGNE" ] || { rouge "Le client n'est pas dans l'annuaire :"; echo "$LISTE" >&2; exit 1; }

MASQUE="$(echo "$LIGNE" | jq -r '.phoneMasked')"

# ON COMPTE LES CHIFFRES, ON NE FILTRE PAS SUR LE CARACTERE DE MASQUE. Le
# point median est multioctet : une regexp qui le contient depend de la
# locale du poste, et le script passerait ou echouerait selon LANG. Quatre
# chiffres et pas un de plus, c'est la regle ; le reste est decoratif.
attendu "Quatre chiffres visibles, pas davantage" "4" \
  "$(printf '%s' "$MASQUE" | tr -cd '0-9' | wc -c | tr -d ' ')"
attendu "Les quatre derniers chiffres correspondent" "${TEL_CLIENT: -4}" \
  "$(printf '%s' "$MASQUE" | tr -cd '0-9')"
attendu "Le numero complet n'est PAS dans la liste" "false" \
  "$(echo "$LISTE" | grep -qF "$TEL_CLIENT" && echo true || echo false)"

# ------------------------------------------------------- 6. La fiche --

fiche() { curl -sS "$GATEWAY/api/admin/v1/customers/$ID_CLIENT" -H "Authorization: Bearer $1"; }

etape "6. La fiche vue par l'administration"
F="$(fiche "$ADMIN")"
attendu "Telephone complet"            "$TEL_CLIENT" "$(echo "$F" | jq -r '.phone')"
attendu "E-mail visible"               "$EMAIL_CLIENT" "$(echo "$F" | jq -r '.email')"
attendu "E-mail non masque"            "false" "$(echo "$F" | jq -r '.emailHidden')"
attendu "Adresses non masquees"        "false" "$(echo "$F" | jq -r '.addressesHidden')"
attendu "Une adresse enregistree"      "1" "$(echo "$F" | jq '.favoriteAddresses | length')"

etape "7. La meme fiche vue par ops"
F="$(fiche "$OPS")"
attendu "Telephone complet, comme pour admin" "$TEL_CLIENT" "$(echo "$F" | jq -r '.phone')"
attendu "E-mail masque"                "true"  "$(echo "$F" | jq -r '.emailHidden')"
attendu "E-mail absent du corps"       "null"  "$(echo "$F" | jq -r '.email')"
attendu "Adresses masquees"            "true"  "$(echo "$F" | jq -r '.addressesHidden')"
attendu "Aucune adresse rendue"        "0"     "$(echo "$F" | jq '.favoriteAddresses | length')"

# LE REPERE ECRIT NE DOIT PAS FUIR AILLEURS DANS LE CORPS. Compter les
# adresses ne suffit pas : une mise en forme maladroite pourrait laisser
# passer le contenu sans la liste.
attendu "Le repere de l'adresse n'apparait nulle part" "false" \
  "$(echo "$F" | grep -qF "pharmacie" && echo true || echo false)"

etape "8. Finance n'ouvre pas l'annuaire"
CODE="$(curl -sS -o "$TEMPO/refus.json" -w '%{http_code}' \
  "$GATEWAY/api/admin/v1/customers" -H "Authorization: Bearer $FINANCE")"
case "$CODE" in
  403) vert "Refus (403)." ;;
  401) vert "Refus (401)." ;;
  *) rouge "Finance a obtenu l'annuaire ($CODE) :"; cat "$TEMPO/refus.json" >&2; exit 1 ;;
esac

# ------------------------------------------------------- 9. Le facture --

etape "9. Le cumul facture"
CODE="$(curl -sS -o "$TEMPO/facture.json" -w '%{http_code}' \
  "$GATEWAY/api/admin/v1/customers/$ID_CLIENT/billing" -H "Authorization: Bearer $ADMIN")"
attendu "Admin obtient le cumul" "200" "$CODE"
attendu "Il porte un nombre de courses" "true" \
  "$(jq -e 'has("deliveredCount")' "$TEMPO/facture.json" >/dev/null && echo true || echo false)"
attendu "Il porte un total en francs" "true" \
  "$(jq -e '.billedTotalXof | type == "number"' "$TEMPO/facture.json" >/dev/null && echo true || echo false)"

CODE="$(curl -sS -o /dev/null -w '%{http_code}' \
  "$GATEWAY/api/admin/v1/customers/$ID_CLIENT/billing" -H "Authorization: Bearer $OPS")"
attendu "Ops se voit refuser le cumul" "403" "$CODE"

# --------------------------------------------------- 10. L'historique --

etape "10. L'historique filtre par client"
COURSES="$(curl -sS "$GATEWAY/api/admin/v1/deliveries?customerId=$ID_CLIENT&pageSize=20" \
  -H "Authorization: Bearer $ADMIN")"
NOMBRE="$(echo "$COURSES" | jq '[.deliveries // [] | .[]] | length')"

if [ "$NOMBRE" = "0" ]; then
  # ON NE COCHE PAS UNE CASE QUI N'A RIEN MESURE. Ce client vient d'etre
  # cree et n'a commande nulle part : le filtre rend une liste vide, ce qui
  # est correct mais ne prouve rien. Le dire vaut mieux qu'un vert trompeur.
  jaune "Aucune course pour ce client : le filtre n'a rien eu a ecarter."
  jaune "Pour l'eprouver vraiment, lancez « make etape4 » puis relancez ce script."
else
  attendu "Toutes les courses rendues sont les siennes" "$NOMBRE" \
    "$(echo "$COURSES" | jq '[.deliveries[] | select(.customerId=="'"$ID_CLIENT"'")] | length')"
fi

# ------------------------------------- 11. Le journal des lectures --

etape "11. Les lectures laissent une trace, les refus non"

LUES="$(sql hba_directory "SELECT count(*) FROM directory.personal_data_reads
  WHERE subject_id = '$ID_CLIENT' AND kind = 1;")"
if [ -z "$LUES" ]; then
  rouge "La table directory.personal_data_reads est introuvable."
  rouge "La migration est-elle generee ET appliquee ?"
  rouge "  make migrations NAME=PersonalDataReads && make migrate"
  exit 1
fi

# TROIS OUVERTURES DE FICHE ONT EU LIEU : admin (etape 6), ops (etape 7), et
# celle qui precede le cumul n'en est pas une. On n'exige pas un compte exact
# — un rejeu du script ajouterait des lignes — mais au moins deux, et les deux
# lecteurs distincts.
[ "$LUES" -ge 2 ] || echouer "seules $LUES lectures de fiche consignees, au moins 2 attendues."
vert "$LUES ouvertures de fiche consignees."

LECTEURS="$(sql hba_directory "SELECT count(DISTINCT reader_id) FROM directory.personal_data_reads
  WHERE subject_id = '$ID_CLIENT' AND kind = 1;")"
attendu "Deux lecteurs distincts (admin et ops)" "2" "$LECTEURS"

ROLES="$(sql hba_directory "SELECT count(*) FROM directory.personal_data_reads
  WHERE subject_id = '$ID_CLIENT' AND reader_roles = 'ops';")"
attendu "Le role d'ops est fige dans la ligne" "true" "$([ "$ROLES" -ge 1 ] && echo true || echo false)"

# LE CUMUL EST CONSIGNE DANS DELIVERY, PAS DANS DIRECTORY. C'est la
# consequence visible de « une base par service » : si cette ligne etait dans
# hba_directory, c'est que le journal aurait traverse une frontiere qu'il ne
# doit pas traverser.
CUMULS="$(sql hba_delivery "SELECT count(*) FROM delivery.personal_data_reads
  WHERE subject_id = '$ID_CLIENT' AND kind = 2;")"
[ -n "$CUMULS" ] || echouer "delivery.personal_data_reads est introuvable : migration non appliquee ?"
attendu "Lecture du cumul consignee dans Delivery" "true" "$([ "$CUMULS" -ge 1 ] && echo true || echo false)"
attendu "Et rien du cumul dans Directory" "0" \
  "$(sql hba_directory "SELECT count(*) FROM directory.personal_data_reads WHERE kind = 2;")"

# LE REFUS DE FINANCE (etape 8) NE DOIT AVOIR RIEN LAISSE. Une ligne pour une
# lecture qui n'a rien montre ferait croire, six mois plus tard, que finance a
# consulte ce client.
ID_FINANCE="$(sql hba_directory "SELECT count(*) FROM directory.personal_data_reads
  WHERE reader_roles LIKE '%finance%';")"
attendu "Le refus de finance n'a rien consigne" "0" "$ID_FINANCE"

printf '\n\033[1;32mL annuaire client tient, et la graduation par role avec lui.\033[0m\n'
