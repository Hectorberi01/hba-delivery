#!/usr/bin/env bash
#
# LE DOSSIER DU LIVREUR, DE BOUT EN BOUT.
#
# Inscription -> depot des cinq pieces -> vehicule -> soumission -> REJET ->
# correction -> resoumission -> validation.
#
# LE REJET N'EST PAS UN ORNEMENT DU SCENARIO, c'est ce qu'il vient verifier.
# La regle « apres un refus, le livreur corrige et resoumet » a ete decidee
# dans l'ADR 0021 et n'existe nulle part ailleurs que dans l'agregat : sans
# ce script, rien ne dit qu'elle tient.
#
#   ./scripts/etape7-dossier-livreur.sh
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

GATEWAY="${GATEWAY:-http://localhost:5100}"
IDENTITY_DIR="src/Services/Identity"

ADMIN_EMAIL="${BOOTSTRAP_ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${BOOTSTRAP_ADMIN_PASSWORD:-HbaDev!2026}"

HORODATAGE="$(date +%s)"
TEL_LIVREUR="${TEL_LIVREUR:-+2290192${HORODATAGE: -6}}"

bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[1;33m%s\033[0m\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }
etape() { printf '\n\033[1;34m── %s\033[0m\n' "$*"; }

echouer() { rouge "ECHEC : $*"; exit 1; }

TEMPO="$(mktemp -d)"
nettoyer() { rm -rf "$TEMPO"; }
trap nettoyer EXIT

# ----------------------------------------------------------- Verifications --

attendu() {
  local quoi="$1" attendu="$2" obtenu="$3"
  if [ "$attendu" = "$obtenu" ]; then
    printf '  \033[1;32m✓\033[0m %-44s %s\n' "$quoi" "$obtenu"
  else
    printf '  \033[1;31m✗\033[0m %-44s attendu «%s», obtenu «%s»\n' \
      "$quoi" "$attendu" "$obtenu" >&2
    exit 1
  fi
}

command -v jq >/dev/null || echouer "jq est requis (brew install jq)."
command -v docker >/dev/null || echouer "docker est requis."

curl -fsS --max-time 5 "$GATEWAY/health" >/dev/null \
  || echouer "la gateway ne repond pas sur $GATEWAY/health."

# ------------------------------------------------------ Images a jour --

# LE PIEGE QUE J'AI DEJA NOMME QUATRE FOIS DANS CE DEPOT, et que ce script
# n'evitait pas : « up -d » sans « --build » relance l'image precedente. Tout
# ce que ce scenario exerce est du code neuf ; contre une image d'hier, les
# routes n'existent pas et le script raconte autre chose que la vraie cause.
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  etape "0. Construction des images (SKIP_BUILD=1 pour sauter)"
  for service in Identity Driver; do
    printf '  %s…\n' "$service"
    docker compose -f "src/Services/$service/docker-compose.yml" build >/dev/null \
      || echouer "la construction de $service a echoue. Relancez-la sans -q :
  docker compose -f src/Services/$service/docker-compose.yml build"
  done
  printf '  Gateway…\n'
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build >/dev/null \
    || echouer "la construction de la gateway a echoue."

  docker compose -f src/Services/Driver/docker-compose.yml up -d --force-recreate >/dev/null
  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml up -d --force-recreate >/dev/null
  bleu "Attente du redemarrage…"
  sleep 10
fi

# ------------------------------------------------- Surcouche OTP de developpement --

restaurer() {
  docker compose -f "$IDENTITY_DIR/docker-compose.yml" \
    up -d --force-recreate identity >/dev/null 2>&1 || true
  nettoyer
}
trap restaurer EXIT

etape "0. Identity en code OTP fixe"
docker compose -f "$IDENTITY_DIR/docker-compose.yml" \
  -f "$IDENTITY_DIR/compose.dev-otp.yml" up -d --force-recreate identity >/dev/null
sleep 10

# ------------------------------------------------------ Routes attendues --

# UNE ROUTE ABSENTE REND 404, ET 404 RESSEMBLE A UN REFUS METIER. C'est ce
# qui a rendu ce script menteur a son premier passage : la soumission d'un
# dossier vide a ete « refusee (404) » et le script a applaudi, alors que la
# route n'existait pas encore. On verifie donc AVANT, et on nomme ce qui
# manque.
etape "1. Les routes du dossier existent-elles ?"

route_presente() {
  local methode="$1" chemin="$2"
  local code
  code="$(curl -sS -o /dev/null -w '%{http_code}' -X "$methode" "$GATEWAY$chemin")"
  # Sans jeton, une route qui existe rend 401. Une route absente rend 404.
  [ "$code" != "404" ]
}

MANQUANTES=()
route_presente GET  /api/driver/v1/application        || MANQUANTES+=("GET /api/driver/v1/application")
route_presente POST /api/driver/v1/documents          || MANQUANTES+=("POST /api/driver/v1/documents")
route_presente POST /api/driver/v1/application/submit || MANQUANTES+=("POST /api/driver/v1/application/submit")
route_presente PUT  /api/driver/v1/vehicle            || MANQUANTES+=("PUT /api/driver/v1/vehicle")

if [ "${#MANQUANTES[@]}" -gt 0 ]; then
  rouge "La passerelle ne connait pas ces routes :"
  printf '    %s\n' "${MANQUANTES[@]}" >&2
  rouge ""
  rouge "L'image de la passerelle est perimee. Reconstruisez-la :"
  rouge "  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml build"
  rouge "  docker compose -f src/Gateways/Hba.Gateway/docker-compose.yml up -d --force-recreate"
  exit 1
fi
vert "Les quatre routes repondent."

# ------------------------------------------------------- Une image de test --

# UNE VRAIE IMAGE, PAS UN FICHIER TEXTE RENOMME. Le serveur se fie au type
# MIME annonce, mais Garage stocke ce qu'on lui donne et la console affichera
# ces vignettes : un octet de texte ne prouverait pas que la chaine porte des
# images.
#
# PNG 1x1 minimal, en base64. Soixante-dix octets, et c'est une image valide.
IMAGE="$TEMPO/piece.png"
base64 --decode > "$IMAGE" <<'B64'
iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==
B64
[ -s "$IMAGE" ] || echouer "l'image de test n'a pas ete produite."

# ------------------------------------------------------------- 1. Comptes --

etape "2. Administrateur"
REPONSE="$(curl -sS -X POST "$GATEWAY/api/web/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"login\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"deviceId\":\"etape7\"}")"
ADMIN="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
[ -n "$ADMIN" ] || echouer "Identity refuse $ADMIN_EMAIL — $REPONSE"
vert "Connecte."

# ------------------------------------------------- Le service Driver repond-il ? --

# UN 503 NE SE LIT PAS COMME UN REFUS METIER, et c'est ce qui a fait ecrire
# « Refus inattendu (503) » a l'etape 4 de la premiere execution : le scenario
# etait deja lance depuis trois etapes quand le vrai probleme etait que Driver
# ne demarrait pas du tout.
#
# LE CONTROLE DES ROUTES NE PEUT PAS L'ATTRAPER : sans jeton, la passerelle
# rend 401 avant d'appeler quoi que ce soit. Il faut donc un appel
# authentifie, et il ne peut venir qu'apres la connexion de l'administrateur.
etape "2 bis. Le service Driver est-il joignable ?"

CODE="$(curl -sS -o "$TEMPO/sonde.json" -w '%{http_code}' \
  "$GATEWAY/api/admin/v1/drivers?pageSize=1" -H "Authorization: Bearer $ADMIN")"

if [ "$CODE" = "503" ] || [ "$CODE" = "504" ]; then
  rouge "La passerelle ne joint pas le service Driver ($CODE)."
  rouge ""
  rouge "LA CAUSE LA PLUS FREQUENTE : les cles du stockage objet sont vides."
  rouge "Driver REFUSE DE DEMARRER sans elles, volontairement — il accepterait"
  rouge "sinon des pieces d'identite et les perdrait une par une."
  rouge ""

  if grep -qE '^OBJECTSTORE_(ACCESS|SECRET)_KEY=$' src/Services/Driver/.env 2>/dev/null; then
    rouge "CONFIRME : OBJECTSTORE_ACCESS_KEY et/ou OBJECTSTORE_SECRET_KEY sont vides"
    rouge "dans src/Services/Driver/.env."
    rouge ""
    rouge "  make garage-init          # cree la cle et affiche le secret UNE SEULE FOIS"
    rouge "  # puis reportez les deux valeurs dans src/Services/Driver/.env"
    rouge "  docker compose -f src/Services/Driver/docker-compose.yml up -d --force-recreate"
  else
    rouge "Les cles sont renseignees. Journaux de Driver :"
    docker compose -f src/Services/Driver/docker-compose.yml logs --tail 30 driver 2>&1 \
      | sed 's/^/    /' >&2 || true
  fi

  exit 1
fi

# LE SCHEMA EST-IL A JOUR ? Une migration generee mais jamais appliquee
# donne « column ... does not exist » — un 500 cote HTTP, et un consommateur
# Kafka qui reessaie trois fois puis abandonne en silence. La generation
# (« make migrations ») et l'application (« make migrate ») sont DEUX
# commandes ; on oublie la seconde, parce que la premiere a l'air d'avoir
# fait le travail.
if [ "$CODE" = "500" ]; then
  rouge "L'annuaire des livreurs repond 500."
  rouge ""

  MANQUE="$(docker compose -f src/Services/Driver/docker-compose.yml logs --tail 200 driver 2>&1 \
    | grep -oE 'column [a-z0-9_.]+ does not exist' | tail -1 || true)"

  if [ -n "$MANQUE" ]; then
    rouge "CONFIRME dans les journaux de Driver : $MANQUE"
    rouge ""
    rouge "La migration est generee mais pas appliquee. En developpement :"
    rouge "  make migrate"
  else
    rouge "Journaux de Driver :"
    docker compose -f src/Services/Driver/docker-compose.yml logs --tail 30 driver 2>&1 \
      | sed 's/^/    /' >&2 || true
  fi

  exit 1
fi

if [ "$CODE" != "200" ]; then
  rouge "Reponse inattendue de l'annuaire des livreurs ($CODE) :"
  cat "$TEMPO/sonde.json" >&2
  exit 1
fi
vert "Driver repond, et son schema est a jour."

etape "3. Livreur ($TEL_LIVREUR)"
DEFI="$(curl -sS -X POST "$GATEWAY/api/driver/v1/auth/otp/request" \
  -H 'Content-Type: application/json' \
  -d "{\"phone\":\"$TEL_LIVREUR\",\"deviceId\":\"etape7\"}" | jq -r '.challengeId // empty')"
[ -n "$DEFI" ] || echouer "pas de defi OTP."

REPONSE="$(curl -sS -X POST "$GATEWAY/api/driver/v1/auth/otp/verify" \
  -H 'Content-Type: application/json' \
  -d "{\"challengeId\":\"$DEFI\",\"code\":\"000000\",\"deviceId\":\"etape7\",\"displayName\":\"Dossier Etape7\"}")"
LIVREUR="$(echo "$REPONSE" | jq -r '.accessToken // empty')"
DRIVER_ID="$(echo "$REPONSE" | jq -r '.principal.driverId // empty')"
[ -n "$LIVREUR" ] || echouer "pas de jeton livreur — $REPONSE"
[ -n "$DRIVER_ID" ] || echouer "le jeton ne porte pas driver_id — $REPONSE"
vert "Livreur $DRIVER_ID"

bleu "Attente de la creation du profil par le service Driver (Kafka)…"
sleep 8

# ------------------------------------------------------------- Raccourcis --

dossier() { curl -sS "$GATEWAY/api/driver/v1/application" -H "Authorization: Bearer $LIVREUR"; }

# LE CODE HTTP PASSE PAR UN FICHIER, PAS PAR UNE VARIABLE. Il partait dans
# $DERNIER_CODE ; « reponse="$(deposer …)" » execute deposer dans un
# SOUS-SHELL, dont les affectations meurent avec lui. Le code revenait donc
# toujours vide, et l'echec s'affichait « refuse () » — la panne exacte que
# cette variable devait empecher, deux ans de bonnes intentions plus tard.
# Un fichier survit au sous-shell ; une variable, non.
deposer() {
  curl -sS -o "$TEMPO/depot.json" -w '%{http_code}' \
    -X POST "$GATEWAY/api/driver/v1/documents?type=$1" \
    -H "Authorization: Bearer $LIVREUR" \
    -F "fichier=@$IMAGE;type=image/png" > "$TEMPO/depot.code"
  cat "$TEMPO/depot.json"
}

code_du_depot() { cat "$TEMPO/depot.code" 2>/dev/null || echo "???"; }

# UN 409 SUR UN DEPOT NE SE LIT PAS DEPUIS LE CLIENT. Le corps porte un code
# metier et une phrase pour le livreur ; ce qui manque, c'est QUELLE
# instruction la base a refusee. DriverDbContext le journalise depuis
# septembre 2026 — encore faut-il aller le chercher, et personne ne pense a
# la bonne commande au moment ou le script echoue. Il la lance lui-meme.
journaux_du_depot() {
  rouge ""
  rouge "Ce que le service Driver a journalise :"

  local DC="docker compose -f src/Services/Driver/docker-compose.yml"
  local trouve=0

  local perdues
  perdues="$($DC logs --tail 400 driver 2>&1 | grep -F "Ecriture concurrente perdue" | tail -5 || true)"
  if [ -n "$perdues" ]; then
    trouve=1
    printf '%s\n' "$perdues" | sed 's/^/    /' >&2
  fi

  # ET L'EXCEPTION BRUTE, EN SECOND CANAL. Si la ligne ci-dessus manque, ce
  # n'est pas que la base va bien : c'est peut-etre que l'image deployee est
  # anterieure a cette journalisation. La pile, elle, est la depuis toujours.
  local pile
  pile="$($DC logs --tail 400 driver 2>&1 \
    | grep -Ei 'DbUpdate|Concurrency|PostgresException|23505|42703' | tail -6 || true)"
  if [ -n "$pile" ]; then
    trouve=1
    rouge ""
    rouge "Exceptions vues par Driver :"
    printf '%s\n' "$pile" | cut -c1-600 | sed 's/^/    /' >&2
  fi

  local sql
  sql="$($DC logs --tail 400 driver 2>&1 \
    | grep -Ei 'INSERT INTO|UPDATE .*SET|DELETE FROM' | tail -12 || true)"
  if [ -n "$sql" ]; then
    trouve=1
    rouge ""
    rouge "Dernieres instructions envoyees a Postgres :"
    printf '%s\n' "$sql" | sed 's/^/    /' >&2
  else
    rouge ""
    rouge "Le SQL n'est pas journalise. Relancez avec :"
    rouge "  EF_SQL_LEVEL=Information EF_SQL_PARAMETERS=true make dossier"
  fi

  if [ "$trouve" -eq 0 ]; then
    rouge ""
    rouge "Rien de cible dans les journaux — voici les trente dernieres lignes :"
    $DC logs --tail 30 driver 2>&1 | sed 's/^/    /' >&2
  fi
}

# depot_exige <type> — echoue bruyamment, en disant le code et le corps.
depot_exige() {
  local reponse code
  reponse="$(deposer "$1")"
  code="$(code_du_depot)"

  if [ "$code" != "200" ]; then
    rouge "Depot de $1 refuse ($code)."
    [ -n "$reponse" ] && printf '%s\n' "$reponse" | sed 's/^/    /' >&2
    case "$code" in
      404) rouge "Route absente : l'image de la passerelle est perimee." ;;
      502|503) rouge "La passerelle n'a pas joint le service Driver sur le port 8080." ;;
    esac

    # LES JOURNAUX POUR TOUT ECHEC, PAS SEULEMENT POUR CEUX QU'ON A PREVUS.
    # La branche n'etait posee que sur 409 ; un code inattendu — ou vide,
    # comme ici — ressortait sans rien montrer.
    journaux_du_depot
    exit 1
  fi
}

# ----------------------------------------------- 3. Un dossier vide se refuse --

etape "4. Soumission d'un dossier vide"

CODE="$(curl -sS -o "$TEMPO/vide.json" -w '%{http_code}' \
  -X POST "$GATEWAY/api/driver/v1/application/submit" \
  -H "Authorization: Bearer $LIVREUR")"

# « PAS 200 » NE SUFFIT PAS COMME ASSERTION, et c'est la lecon de la premiere
# execution : elle a rendu 404 — route absente — et le script a annonce que
# la regle metier tenait. Un test qui passe pour la mauvaise raison est pire
# qu'un test absent, parce qu'on cesse de le soupconner.
#
# Un refus METIER rend 400 ou 409. 404 veut dire « route ou profil
# introuvable », 401 « jeton invalide » : ni l'un ni l'autre ne dit quoi que
# ce soit de « complet, ou rien ».
case "$CODE" in
  400|409)
    vert "Refusee ($CODE) — c'est bien la regle « complet, ou rien »."
    echo "  $(jq -r '.message // .code // .' < "$TEMPO/vide.json" 2>/dev/null | head -1)"
    ;;
  200)
    echouer "un dossier vide a ete accepte. La regle « complet, ou rien » ne tient pas."
    ;;
  404)
    rouge "404 : le profil du livreur n'existe pas encore."
    rouge "Le service Driver le cree depuis l'evenement AccountRegistered."
    rouge "Verifiez qu'il consomme bien le topic identity :"
    rouge "  docker compose -f src/Services/Driver/docker-compose.yml logs --tail 50 driver"
    exit 1
    ;;
  *)
    rouge "Refus inattendu ($CODE). Reponse :"
    cat "$TEMPO/vide.json" >&2
    exit 1
    ;;
esac

# ------------------------------------------------------- 4. Les cinq pieces --

etape "5. Depot des cinq pieces"

for TYPE in NATIONAL_ID DRIVING_LICENCE VEHICLE_REGISTRATION IDENTITY_PHOTO VEHICLE_PHOTO; do
  depot_exige "$TYPE"
  printf '  deposee : %s\n' "$TYPE"
done

attendu "Pieces au dossier" "5" "$(dossier | jq '.documents | length')"
attendu "Pieces manquantes" "0" "$(dossier | jq '.missingDocuments | length')"

# LE VEHICULE MANQUE ENCORE : « aucune piece manquante » ne veut pas dire
# « pret a soumettre », et c'est precisement le piege que can_submit evite.
attendu "Pret a soumettre (vehicule absent)" "false" "$(dossier | jq -r '.canSubmit')"

# ---------------------------------------------------------- 5. Le vehicule --

etape "6. Declaration du vehicule"

REPONSE="$(curl -sS -X PUT "$GATEWAY/api/driver/v1/vehicle" \
  -H "Authorization: Bearer $LIVREUR" -H 'Content-Type: application/json' \
  -d '{"type":"MOTORCYCLE","plate":"AB 1234 RB"}')"
echo "$REPONSE" | jq -e '.id' >/dev/null 2>&1 || echouer "vehicule refuse — $REPONSE"

attendu "Immatriculation" "AB 1234 RB" "$(dossier | jq -r '.vehicle.plate')"
attendu "Pret a soumettre" "true" "$(dossier | jq -r '.canSubmit')"

# -------------------------------------------------------- 6. La soumission --

etape "7. Soumission"

REPONSE="$(curl -sS -X POST "$GATEWAY/api/driver/v1/application/submit" \
  -H "Authorization: Bearer $LIVREUR")"
echo "$REPONSE" | jq -e '.id' >/dev/null 2>&1 || echouer "soumission refusee — $REPONSE"

attendu "Statut" "PendingVerification" "$(dossier | jq -r '.verificationStatus')"
[ "$(dossier | jq -r '.submittedAt')" != "null" ] \
  || echouer "la date de soumission n'a pas ete posee."

# ------------------------------------------------------------- 7. Le rejet --

etape "8. Rejet par l'administration"

REPONSE="$(curl -sS -X POST "$GATEWAY/api/admin/v1/drivers/$DRIVER_ID/kyc" \
  -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d '{"approved":false,"reason":"La carte grise est illisible."}')"
echo "$REPONSE" | jq -e '.id' >/dev/null 2>&1 || echouer "rejet refuse — $REPONSE"

attendu "Statut" "Rejected" "$(dossier | jq -r '.verificationStatus')"
attendu "Motif rendu au livreur" "La carte grise est illisible." \
  "$(dossier | jq -r '.statusReason')"

# CE QUI SE JOUE ICI : un dossier rejete doit rester MODIFIABLE. S'il se
# figeait, la seule issue serait un second compte pour la meme personne, avec
# la meme CNI.
attendu "Rejete mais encore soumettable" "true" "$(dossier | jq -r '.canSubmit')"

# -------------------------------------------------------- 8. La correction --

etape "9. Correction et resoumission"

depot_exige VEHICLE_REGISTRATION

attendu "Toujours cinq pieces (remplacement, pas ajout)" "5" \
  "$(dossier | jq '.documents | length')"

REPONSE="$(curl -sS -X POST "$GATEWAY/api/driver/v1/application/submit" \
  -H "Authorization: Bearer $LIVREUR")"
echo "$REPONSE" | jq -e '.id' >/dev/null 2>&1 || echouer "resoumission refusee — $REPONSE"

attendu "Statut apres resoumission" "PendingVerification" \
  "$(dossier | jq -r '.verificationStatus')"
attendu "Motif efface" "" "$(dossier | jq -r '.statusReason')"

# --------------------------------------------------------- 9. La validation --

etape "10. Validation"

REPONSE="$(curl -sS -X POST "$GATEWAY/api/admin/v1/drivers/$DRIVER_ID/kyc" \
  -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d '{"approved":true,"reason":"Dossier conforme."}')"
echo "$REPONSE" | jq -e '.id' >/dev/null 2>&1 || echouer "validation refusee — $REPONSE"

attendu "Statut" "Verified" "$(dossier | jq -r '.verificationStatus')"

# UN DOSSIER VALIDE SE FIGE. Laisser remplacer une CNI apres validation
# reviendrait a valider une personne et a en laisser travailler une autre.
#
# CE CONTROLE PASSAIT POUR LA MAUVAISE RAISON. Il lisait $DERNIER_CODE, que
# le sous-shell de « deposer » n'a jamais renseigne : la comparaison portait
# sur une chaine vide, « different de 200 » etait donc toujours vrai, et le
# script annonçait que le gel tenait sans l'avoir mesure. Il lit maintenant
# le code depuis le fichier — et un 409 y est exige, pas seulement « pas 200 ».
deposer NATIONAL_ID >/dev/null
attendu "Remplacement refuse sur dossier valide" "409" "$(code_du_depot)"

# ------------------------------------------------- 10. Les URL signees vivent --

etape "11. Les pieces sont reellement lisibles"

URL="$(dossier | jq -r '.documents[0].readUrl')"
if [ -z "$URL" ] || [ "$URL" = "null" ]; then
  echouer "aucune URL signee dans le dossier."
fi

# LE VRAI TEST DU STOCKAGE. Tout le reste passe par la base ; cette ligne est
# la seule qui prouve que l'octet depose est ressorti de Garage.
OCTETS="$(curl -sS -o "$TEMPO/relu.png" -w '%{size_download}' "$URL")"
[ "$OCTETS" -gt 0 ] || echouer "l'URL signee ne rend rien. Garage est-il initialise ?"
# UNE IMAGE QUI DIFFERE FAIT ECHOUER LE SCRIPT. Elle rendait un avertissement
# jaune, et le scenario se terminait sur « tout tient » : l'assertion la plus
# importante de tout ce fichier — l'octet depose est ressorti de Garage —
# etait la seule a ne pas pouvoir echouer. Un « && … || … » n'est pas un
# si-alors-sinon, et ici la difference n'etait pas theorique.
if cmp -s "$IMAGE" "$TEMPO/relu.png"; then
  vert "L'image relue est identique a celle deposee ($OCTETS octets)."
else
  echouer "l'image relue fait $OCTETS octets et differe de celle deposee.
  Le depot et la relecture ne portent pas le meme objet : cle mal formee,
  reecriture par un autre depot, ou troncature a l'ecriture."
fi

etape "Verdict"
vert "Le dossier tient de bout en bout, rejet et reprise compris."
