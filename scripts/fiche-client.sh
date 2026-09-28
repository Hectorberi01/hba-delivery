#!/usr/bin/env bash
#
# OU S'EST ARRETEE LA CHAINE QUI CREE UNE FICHE CLIENT.
#
# LE SYMPTOME EST MUET. « GET /me » repond 404, l'ecran de profil dit que la
# fiche n'existe pas, et rien n'indique pourquoi. Or trois choses doivent
# s'enchainer, chacune pouvant tomber sans bruit :
#
#   1. Identity cree le compte et POSE un evenement dans son outbox ;
#   2. un publicateur envoie cette ligne sur le topic Kafka ;
#   3. Directory la consomme et cree la fiche.
#
# Ce script regarde les trois, dans l'ordre, et dit lequel a lache. Sans lui on
# cherche dans le mauvais service : le 404 vient de Directory, mais la cause est
# presque toujours en amont.
#
#   ./scripts/fiche-client.sh +22966000001                          # par numero
#   ./scripts/fiche-client.sh 01a0e4d9-f401-7894-a8d8-ba6af1ba7c7c  # par identifiant
#   ./scripts/fiche-client.sh                                       # les cinq derniers
#
# L'IDENTIFIANT EST ACCEPTE PARCE QUE C'EST CE QUE L'ECRAN MONTRE. Le message
# d'erreur de l'application dit « Client introuvable : <uuid> » ; obliger a
# retrouver le numero correspondant avant de pouvoir chercher ferait perdre
# l'information qu'on vient de recevoir.

set -euo pipefail

racine="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose="$racine/deploy/docker-compose.yml"
telephone="${1:-}"

if ! command -v docker >/dev/null 2>&1; then
  echo "docker introuvable." >&2
  exit 1
fi

# LE PSQL EST CELUI DU CONTENEUR, pas celui du Mac : il est toujours de la bonne
# version, et il n'y a rien a installer.
psql() {
  local base="$1"; shift
  docker compose -f "$compose" exec -T postgres \
    psql -U "${POSTGRES_USER:-hba}" -d "$base" -v ON_ERROR_STOP=1 "$@"
}

if ! docker compose -f "$compose" ps --status running postgres >/dev/null 2>&1; then
  echo "La pile de developpement ne tourne pas :  make up" >&2
  exit 1
fi

if [ -z "$telephone" ]; then
  echo "═══ Les cinq derniers comptes portant le role client"
  psql hba_identity -c "
    select \"Id\", phone, roles, \"DisplayName\", \"CreatedAt\"
    from identity.accounts
    where roles like '%customer%'
    order by \"CreatedAt\" desc
    limit 5;"
  echo
  echo "Relancez avec un numero :  ./scripts/fiche-client.sh +22966000001"
  exit 0
fi

echo "═══ 1. Le compte, dans Identity"

# UN UUID EST UN IDENTIFIANT, LE RESTE EST UN NUMERO. On ne demande pas a
# l'appelant de le preciser : la forme suffit a decider, et se tromper ne
# coute qu'une recherche vide.
if [[ "$telephone" =~ ^[0-9a-fA-F-]{36}$ ]]; then
  compte="$(psql hba_identity -tAc "
    select \"Id\" from identity.accounts where \"Id\" = '$telephone' limit 1;")"
else
  compte="$(psql hba_identity -tAc "
    select \"Id\" from identity.accounts where phone = '$telephone' limit 1;")"
fi
compte="$(echo "$compte" | tr -d '[:space:]')"

if [ -z "$compte" ]; then
  echo "  Aucun compte ne correspond a « $telephone »."
  echo "  La chaine n'a jamais commence : c'est l'inscription qui n'a pas eu lieu."
  exit 0
fi

psql hba_identity -c "
  select \"Id\", phone, roles, \"DisplayName\", \"Status\", \"CreatedAt\"
  from identity.accounts where \"Id\" = '$compte';"

numero="$(psql hba_identity -tAc "
  select phone from identity.accounts where \"Id\" = '$compte';" | tr -d '[:space:]')"

echo
echo "═══ 2. L'evenement, dans l'outbox d'Identity"
# « PublishedAt » NUL VEUT DIRE QUE LA LIGNE ATTEND ENCORE. Avec des tentatives
# et une derniere erreur, c'est le publicateur qui echoue ; sans ligne du tout,
# c'est l'evenement qui n'a jamais ete pose.
psql hba_identity -c "
  select \"EventType\", \"OccurredAt\", \"PublishedAt\", \"Attempts\", \"LastError\"
  from identity.outbox_messages
  where \"PartitionKey\" = '$compte'
  order by \"OccurredAt\";"

# LA DATE DE PUBLICATION SERT DE FENETRE. L'inbox de Directory ne garde pas
# l'identifiant du compte : ses colonnes sont l'identifiant de l'evenement,
# son type et ses dates. Compter « des AccountRegistered » ne dit donc rien —
# il peut y en avoir dix, d'autres comptes, et pas celui-ci. On encadre donc
# la recherche par l'heure a laquelle CELUI-CI est parti.
# LE FORMAT EST DEMANDE A POSTGRES, PAS RECOUPE ICI. Un « timestamptz » se lit
# « 2026-09-27 21:51:24.643422+00 » : il contient une espace, et le nettoyage
# applique apres chaque lecture psql la supprimerait, produisant une date que
# Postgres refuse ensuite. On le fait donc ecrire en ISO, avec un T : une
# valeur sans espace ne peut pas etre cassee par un nettoyage. « OF » n'est
# defini que sur un timestamptz : on ne le convertit donc pas avant, sinon
# l'offset n'aurait plus de source.
publie="$(psql hba_identity -tAc "
  select to_char(\"PublishedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS.USOF')
  from identity.outbox_messages
  where \"PartitionKey\" = '$compte'
    and \"EventType\" like '%AccountRegistered%'
    and \"PublishedAt\" is not null
  order by \"PublishedAt\" limit 1;" | tr -d '[:space:]')"

echo "═══ 3. L'evenement, vu par Directory"
# L'INBOX EST LE MAILLON QUE J'AVAIS OUBLIE, ET C'EST LE PLUS PARLANT.
#
# Une ligne « ProcessedAt » renseignee alors que la fiche n'existe pas veut
# dire que Directory a bien recu l'evenement et l'a marque comme traite —
# autrement dit qu'il ne le rejouera JAMAIS. Aucune ligne du tout veut dire
# qu'il ne l'a pas vu : service arrete, groupe de consommateurs decale, ou
# topic absent. Les deux situations se ressemblent depuis l'application et
# n'appellent pas la meme reparation.
if [ -n "$publie" ]; then
  echo "  (evenement publie a $publie ; fenetre de -1 a +10 minutes)"
  psql hba_directory -c "
    select \"EventType\", \"ReceivedAt\", \"ProcessedAt\", \"Attempts\", \"LastError\"
    from directory.inbox_messages
    where \"EventType\" like '%AccountRegistered%'
      and \"ReceivedAt\" between timestamptz '$publie' - interval '1 minute'
                            and timestamptz '$publie' + interval '10 minutes'
    order by \"ReceivedAt\";"
else
  echo "  (l'evenement n'a pas de date de publication : on montre les dernieres lignes)"
  psql hba_directory -c "
    select \"EventType\", \"ReceivedAt\", \"ProcessedAt\", \"Attempts\", \"LastError\"
    from directory.inbox_messages
    order by \"ReceivedAt\" desc
    limit 10;"
fi

echo "═══ 4. La fiche, dans Directory"
# L'IDENTIFIANT EST LE MEME DES DEUX COTES, par contrat : « Identique au
# subject_id du compte Identity : un compte, un profil ».
#
# LES COLONNES SONT EN PASCALCASE POUR « Id », en snake_case pour le reste :
# c'est ce que produit la configuration EF de ce service, et une requete sans
# guillemets echoue sur « column id does not exist ».
psql hba_directory -c "
  select \"Id\", display_name, phone, email
  from directory.customers where \"Id\" = '$compte';"

echo "═══ 5. Une AUTRE fiche porte-t-elle deja ce telephone ?"
# « IX_customers_phone » EST UNIQUE, ET C'EST LE PIEGE LE PLUS SILENCIEUX DE LA
# CHAINE. Si une fiche existe deja avec ce numero sous un AUTRE identifiant, la
# creation echoue sur une violation d'unicite — pas sur une regle metier. Le
# consommateur Kafka enregistre alors une erreur dans son inbox et n'insiste
# plus, et la route de rattrapage rend « Erreur interne du service ».
#
# Les deux symptomes se ressemblent a s'y meprendre : dans les deux cas la
# fiche du compte n'existe pas. Mais la cause n'est pas l'evenement perdu, et la
# reparation n'est pas la meme — il faut decider ce qu'on fait de la fiche
# orpheline avant de pouvoir creer celle-ci.
if [ -n "$numero" ]; then
  psql hba_directory -c "
    select \"Id\", display_name, phone, \"CreatedAt\"
    from directory.customers
    where phone = '$numero' and \"Id\" <> '$compte';"

  # CE QUE PORTE LA FICHE ORPHELINE DECIDE DE SON SORT, ET PAS SON NOM.
  # « Test » ressemble a un residu, mais un compte encore vivant, des adresses
  # enregistrees ou des livraisons passees ne se supprimeront pas de la meme
  # facon. Les trois questions se posent dans trois bases differentes : les
  # poser a la main, c'est trois commandes et une chance sur trois d'en oublier
  # une avant de supprimer.
  autre="$(psql hba_directory -tAc "
    select \"Id\" from directory.customers
    where phone = '$numero' and \"Id\" <> '$compte' limit 1;" | tr -d '[:space:]')"

  if [ -n "$autre" ]; then
    echo
    echo "  Ce que porte la fiche $autre :"

    echo "   — son compte, dans Identity :"
    psql hba_identity -c "
      select \"Id\", phone, roles, \"Status\", \"CreatedAt\"
      from identity.accounts where \"Id\" = '$autre';"

    echo "   — ses adresses favorites :"
    psql hba_directory -c "
      select count(*) as adresses
      from directory.customer_favorite_addresses
      where \"CustomerId\" = '$autre';"

    echo "   — ses livraisons :"
    psql hba_delivery -c "
      select count(*) as livraisons, min(\"CreatedAt\") as premiere,
             max(\"CreatedAt\") as derniere
      from delivery.deliveries where \"CustomerId\" = '$autre';"
  fi
else
  echo "  (le compte n'a pas de telephone : rien a comparer)"
fi

echo "═══ Verdict"
fiche="$(psql hba_directory -tAc "
  select count(*) from directory.customers where \"Id\" = '$compte';" | tr -d '[:space:]')"

if [ "$fiche" = "1" ]; then
  echo "  La fiche existe. Si l'application dit le contraire, le probleme est"
  echo "  ailleurs : jeton, revendication, ou service Directory injoignable."
  exit 0
fi

pose="$(psql hba_identity -tAc "
  select count(*) from identity.outbox_messages
  where \"PartitionKey\" = '$compte'
    and \"EventType\" like '%AccountRegistered%';" | tr -d '[:space:]')"

envoye="$(psql hba_identity -tAc "
  select count(*) from identity.outbox_messages
  where \"PartitionKey\" = '$compte'
    and \"EventType\" like '%AccountRegistered%'
    and \"PublishedAt\" is not null;" | tr -d '[:space:]')"

if [ "$pose" = "0" ]; then
  echo "  L'evenement n'a JAMAIS ete pose dans l'outbox."
  echo "  Le compte a probablement ete cree avant que l'evenement n'existe,"
  echo "  ou par un chemin qui ne le leve pas."
elif [ "$envoye" = "0" ]; then
  echo "  L'evenement est pose mais N'EST PAS PARTI. Regardez la colonne"
  echo "  LastError ci-dessus, et si Kafka tourne."
else
  vu="$(psql hba_directory -tAc "
    select count(*) from directory.inbox_messages
    where \"EventType\" like '%AccountRegistered%'
      and \"ReceivedAt\" between timestamptz '$publie' - interval '1 minute'
                            and timestamptz '$publie' + interval '10 minutes';" \
    | tr -d '[:space:]')"

  squatte="$(psql hba_directory -tAc "
    select count(*) from directory.customers
    where phone = '$numero' and \"Id\" <> '$compte';" | tr -d '[:space:]')"

  if [ "$squatte" != "0" ] && [ -n "$numero" ]; then
    echo "  UNE AUTRE FICHE PORTE DEJA CE TELEPHONE (voir l'etape 5)."
    echo "  L'index unique sur la colonne « phone » interdit donc la creation :"
    echo "  ce n'est pas l'evenement qui s'est perdu, c'est l'insertion qui"
    echo "  echoue, a chaque tentative et par le meme chemin."
    echo "  A decider avant de reparer : ce que devient la fiche orpheline."
    exit 0
  fi

  echo "  L'evenement est PARTI d'Identity, et la fiche n'existe pas."
  if [ "$vu" = "0" ]; then
    echo "  Directory ne l'a JAMAIS RECU : rien dans son inbox a cette heure-la."
    echo "  Le service etait arrete, ou son groupe de consommateurs n'etait pas"
    echo "  encore branche sur le topic quand le message est passe. Kafka ne"
    echo "  rejoue pas pour un groupe qui n'existait pas."
    echo "    docker compose -f src/Services/Directory/docker-compose.yml logs directory"
  else
    echo "  Directory L'A RECU (voir l'inbox ci-dessus) mais la fiche manque."
    echo "  Si ProcessedAt est renseigne, il ne rejouera jamais : le traitement"
    echo "  a ete marque fait sans creer la fiche. Regardez LastError."
  fi
fi
