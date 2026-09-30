#!/usr/bin/env bash
#
# GENERE OU FAIT TOURNER LES TROIS SECRETS INTERNES DE GARAGE.
#
# Ils vivaient en clair dans deploy/garage/garage.toml, fichier suivi par git
# dans un depot PUBLIC : le jeton d'administration du stockage objet — donc de
# toutes les pieces d'identite — etait lisible par n'importe qui. Corrige le
# 30 septembre 2026. Ils arrivent desormais par variables d'environnement,
# depuis deploy/.env qui n'est pas versionne.
#
# CE SCRIPT N'AFFICHE JAMAIS UNE VALEUR. Il imprime une empreinte courte, de
# quoi verifier que deux machines portent bien le meme secret sans jamais le
# faire transiter par un terminal, un journal ou un presse-papier.
#
#   ./scripts/garage-secrets.sh              # fait tourner les trois secrets
#   ./scripts/garage-secrets.sh --si-absent  # ne remplit que ce qui manque
#
# Apres une rotation, il faut recreer le conteneur : Garage lit ses secrets au
# demarrage.
#
set -euo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RACINE"

ENV_FICHIER="deploy/.env"
EXEMPLE="deploy/.env.example"
CLES="GARAGE_RPC_SECRET GARAGE_ADMIN_TOKEN GARAGE_METRICS_TOKEN"

SI_ABSENT=0
[ "${1:-}" = "--si-absent" ] && SI_ABSENT=1

bleu()  { printf '\033[1;34m%s\033[0m\n' "$*"; }
vert()  { printf '\033[1;32m%s\033[0m\n' "$*"; }
jaune() { printf '\033[1;33m%s\033[0m\n' "$*"; }
rouge() { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }

command -v openssl >/dev/null || { rouge "openssl est requis."; exit 1; }

if [ ! -f "$ENV_FICHIER" ]; then
  [ -f "$EXEMPLE" ] || { rouge "$EXEMPLE est absent."; exit 1; }
  cp "$EXEMPLE" "$ENV_FICHIER"
  jaune "$ENV_FICHIER cree depuis le gabarit."
fi

# LE FICHIER N'EST LISIBLE QUE PAR SON PROPRIETAIRE. Il porte desormais les
# secrets du stockage : 644 les donnerait a tout compte de la machine.
chmod 600 "$ENV_FICHIER" 2>/dev/null || true

# HEXADECIMAL POUR LES TROIS, ET CE N'EST PAS UN CAPRICE. Le base64 contient
# « = », « + » et « / » ; Docker Compose interpole ces valeurs, et une valeur
# non alphanumerique dans un .env finit par demander des guillemets que l'on
# oublie un jour. L'hexadecimal a la meme force pour deux fois la longueur, et
# aucune surprise de citation. Garage impose 64 caracteres hex pour rpc_secret ;
# les deux jetons acceptent n'importe quelle chaine.
valeur_neuve() { openssl rand -hex 32; }

# Empreinte courte : elle identifie un secret sans rien en reveler.
empreinte() {
  printf '%s' "$1" | openssl dgst -sha256 -binary | od -An -tx1 | tr -d ' \n' | cut -c1-10
}

lire_valeur() {
  # La premiere occurrence seulement, et tout ce qui suit le premier « = » :
  # une valeur peut contenir un « = » sans que la ligne soit pour autant
  # malformee.
  grep -m1 -E "^$1=" "$ENV_FICHIER" 2>/dev/null | cut -d= -f2- || true
}

# ON ECRIT DANS LE FICHIER EXISTANT, ON NE LE REMPLACE PAS, et ce choix a une
# raison concrete : « mv tmp .env » doit DELIER la cible. Cela echoue des que le
# repertoire interdit la suppression — un volume monte en lecture-ecriture mais
# sans droit d'unlink, ce qui arrive plus souvent qu'on ne croit. Le contenu est
# donc prepare a part, puis verse dans le fichier par troncature.
#
# LA CONTREPARTIE EST UNE FENETRE : une interruption pendant le versement
# laisserait un .env incomplet. D'ou la sauvegarde prise juste avant, et la
# restauration si la relecture ne retrouve pas ce qu'on vient d'ecrire.
poser_valeur() {
  local cle="$1" valeur="$2" tmp sauvegarde
  tmp="$(mktemp)"
  sauvegarde="$(mktemp)"
  chmod 600 "$tmp" "$sauvegarde"

  cat "$ENV_FICHIER" > "$sauvegarde"

  if grep -qE "^$cle=" "$ENV_FICHIER"; then
    # LA VALEUR NE PASSE PAS PAR LE MOTIF DE SED. Un secret contenant « & » ou
    # « | » serait reecrit de travers ; awk recoit la valeur en variable, donc
    # litteralement, quoi qu'elle contienne.
    awk -v cle="$cle" -v val="$valeur" \
      'BEGIN{FS=OFS="="} $1==cle && !fait {print cle "=" val; fait=1; next} {print}' \
      "$ENV_FICHIER" > "$tmp"
  else
    cat "$ENV_FICHIER" > "$tmp"
    printf '%s=%s\n' "$cle" "$valeur" >> "$tmp"
  fi

  cat "$tmp" > "$ENV_FICHIER"

  if [ "$(lire_valeur "$cle")" != "$valeur" ]; then
    cat "$sauvegarde" > "$ENV_FICHIER"
    rm -f "$tmp" "$sauvegarde" 2>/dev/null || true
    rouge "Ecriture de $cle impossible ; $ENV_FICHIER a ete restaure."
    return 1
  fi

  rm -f "$tmp" "$sauvegarde" 2>/dev/null || true
  chmod 600 "$ENV_FICHIER" 2>/dev/null || true
}

bleu "Secrets internes de Garage — $ENV_FICHIER"
echo

TOUCHES=0

for CLE in $CLES; do
  ACTUELLE="$(lire_valeur "$CLE")"

  if [ "$SI_ABSENT" = "1" ] && [ -n "$ACTUELLE" ]; then
    vert "  $CLE deja pose (empreinte $(empreinte "$ACTUELLE"))"
    continue
  fi

  NEUVE="$(valeur_neuve)"
  poser_valeur "$CLE" "$NEUVE"

  RELUE="$(lire_valeur "$CLE")"
  [ "$RELUE" = "$NEUVE" ] || { rouge "L'ecriture de $CLE a echoue."; exit 1; }

  vert "  $CLE pose (empreinte $(empreinte "$NEUVE"))"
  TOUCHES=$((TOUCHES + 1))
done

echo

if [ "$TOUCHES" = "0" ]; then
  vert "Rien a faire."
  exit 0
fi

jaune "Garage lit ses secrets AU DEMARRAGE. Recreez le conteneur :"
jaune "  docker compose -f deploy/docker-compose.yml up -d --force-recreate garage"
jaune "  # ou, pour l'infrastructure partagee :"
jaune "  docker compose -f deploy/compose.infra.yml up -d --force-recreate garage"
echo
bleu "Ni la disposition, ni le seau, ni les cles S3 ne sont touches : ces trois"
bleu "secrets n'authentifient que le protocole entre noeuds et l'API d'admin."
