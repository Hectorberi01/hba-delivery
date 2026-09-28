#!/usr/bin/env bash
#
# CE QUE CHAQUE SERVICE A VRAIMENT RECU COMME CHAINE DE CONNEXION.
#
# LE .ENV N'EST PAS CE QUI COMPTE : le conteneur, si. Un .env complet et un
# conteneur cree avant sa derniere modification donnent une chaine amputee, et
# la panne s'exprime tres loin de la — « No password has been provided », ou
# une base vide dans un journal EF Core. Ce script compare les deux.
#
# AUCUNE VALEUR N'EST AFFICHEE. La chaine porte le mot de passe de la base :
# on ne montre que les champs PRESENTS, jamais leur contenu. Un diagnostic ne
# doit pas laisser un secret dans un terminal, ni dans une capture d'ecran
# collee dans une conversation.
#
#   ./scripts/chaines-de-connexion.sh
#
set -uo pipefail

racine="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$racine"

vert()  { printf '\033[32m%s\033[0m' "$*"; }
rouge() { printf '\033[31m%s\033[0m' "$*"; }
gris()  { printf '\033[90m%s\033[0m' "$*"; }

command -v docker >/dev/null 2>&1 || { echo "docker introuvable." >&2; exit 1; }

printf '%-12s %-26s %s\n' "SERVICE" "VARIABLE" "CHAMPS"
printf '%-12s %-26s %s\n' "-------" "--------" "------"

manque_total=0

for f in src/Services/*/docker-compose.yml; do
  dossier="$(dirname "$f")"
  nom="$(basename "$dossier" | tr '[:upper:]' '[:lower:]')"

  if ! docker compose -f "$f" ps --status running "$nom" 2>/dev/null | grep -q "$nom"; then
    printf '%-12s ' "$nom"; gris "arrete"; echo
    continue
  fi

  # TOUTES LES VARIABLES, PAS LA PREMIERE. Dispatch et Driver en ont deux —
  # Postgres et Redis — et n'en lire qu'une donnait un verdict faux : la chaine
  # Redis n'a ni Host= ni Database=, donc le script annoncait cinq champs
  # manquants sur un service parfaitement configure. Un instrument qui se
  # trompe envoie chercher la panne au mauvais endroit, ce qui coute plus cher
  # que pas d'instrument du tout.
  brut="$(docker compose -f "$f" exec -T "$nom" sh -c 'printenv | grep "^ConnectionStrings__"' 2>/dev/null | tr -d '\r')"

  if [ -z "$brut" ]; then
    printf '%-12s %-26s ' "$nom" "(aucune)"; rouge "AUCUNE variable ConnectionStrings__"; echo
    manque_total=$((manque_total + 1))
    continue
  fi

  while IFS= read -r ligne; do
    [ -n "$ligne" ] || continue
    cle="${ligne%%=*}"
    valeur="${ligne#*=}"
    court="${cle#ConnectionStrings__}"

    # SEULES LES CHAINES POSTGRES SE JUGENT SUR CES CINQ CHAMPS. Redis s'ecrit
    # « hote:port,password=… » : lui appliquer la meme grille n'a aucun sens.
    case "$court" in
      *Db)
        presents=""; absents=""
        for champ in Host Port Database Username Password; do
          if printf '%s' "$valeur" | grep -qiE "(^|;)[[:space:]]*$champ[[:space:]]*=[[:space:]]*[^;[:space:]]"; then
            presents="$presents $champ"
          else
            absents="$absents $champ"
          fi
        done

        printf '%-12s %-26s ' "$nom" "$court"
        if [ -z "$absents" ]; then
          vert "complete"
        else
          rouge "MANQUE :$absents"
          manque_total=$((manque_total + 1))
        fi
        echo
        ;;
      *)
        printf '%-12s %-26s ' "$nom" "$court"
        gris "(pas une chaine Postgres, non verifiee)"
        echo
        ;;
    esac
  done <<< "$brut"
done

# ------------------------------------------------ Ce qui prime sur le .env ---
#
# L'ENVIRONNEMENT DU SHELL L'EMPORTE SUR LE .ENV, ET PERSONNE NE S'EN SOUVIENT.
# Docker Compose resout ${VAR} d'abord dans les variables exportees du terminal,
# et seulement ensuite dans le .env du service. Une variable exportee une fois —
# souvent par une ligne collee ou le « ; » de la chaine n'etait pas protege par
# des guillemets, ce qui la coupe net — s'impose alors a toutes les commandes
# lancees depuis ce terminal, et le .env, pourtant correct, n'est jamais lu.
#
# C'est invisible : rien ne le signale, ni Compose, ni le service.
echo
exportees="$(env | grep -E '^[A-Z]+DB_CONNECTION=' || true)"

if [ -n "$exportees" ]; then
  rouge "ATTENTION : des chaines de connexion sont EXPORTEES dans ce terminal."
  echo
  echo "$exportees" | sed -E 's/=.*/= (valeur masquee)/'
  echo
  echo "  Elles PRIMENT sur le .env des services, pour toute commande docker"
  echo "  compose lancee depuis ce terminal. Si l'une est tronquee — un « ; »"
  echo "  non protege par des guillemets suffit —, le conteneur la recoit telle"
  echo "  quelle et le .env n'est jamais consulte."
  echo
  echo "  Pour les retirer, dans CE terminal :"
  echo "$exportees" | sed -E 's/^([A-Z_]+)=.*/    unset \1/'
  echo
  echo "  Puis recreez les services concernes :  make rebuild D=src/Services/<Nom>"
  echo
fi

if [ "$manque_total" = "0" ]; then
  echo "Toutes les chaines Postgres des services demarres sont completes."
else
  echo "$manque_total chaine(s) incomplete(s)."
  echo "Si le .env du service est correct, le conteneur tourne sur un"
  echo "environnement perime — le .env n'est lu qu'a la CREATION du conteneur."
  echo
  echo "  make rebuild D=src/Services/<Nom>"
fi
