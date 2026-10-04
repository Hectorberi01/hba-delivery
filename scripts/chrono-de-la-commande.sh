#!/usr/bin/env bash
#
# OU PASSE LE TEMPS ENTRE « JE PAIE » ET « MA COMMANDE EST LA » ?
#
# « L'application met du temps » recouvre quatre attentes qui s'additionnent, et
# dont DEUX NE SONT PAS DE NOUS :
#
#   1. created_at → succeeded_at   Le client sur la page du fournisseur : il
#                                  choisit son operateur, tape son code, attend
#                                  la confirmation de l'operateur, puis le
#                                  webhook signe nous parvient par le tunnel.
#                                  C'est presque toujours le plus gros terme, et
#                                  aucune ligne de notre code ne le raccourcit.
#   2. OccurredAt → PublishedAt    Notre boite d'envoi. Le cycle tourne toutes
#                                  les secondes : au-dela de deux ou trois
#                                  secondes, quelque chose cloche — un broker
#                                  injoignable, ou un message en attente
#                                  exponentielle (jusqu'a 512 s au neuvieme
#                                  essai).
#   3. PublishedAt → ReceivedAt    Kafka et le reveil du consommateur. Un
#                                  rebalancement de groupe apres redemarrage
#                                  s'y voit.
#   4. ReceivedAt → ProcessedAt    Le traitement chez nous.
#
# Et un cinquieme, invisible en base : L'APPLICATION NE SAIT QUE CE QU'ELLE
# REDEMANDE. L'accueil sonde toutes les cinq secondes ; meme avec une chaine
# parfaite, le client peut donc attendre cinq secondes de plus que la verite.
#
# CE SCRIPT NE DEVINE PAS, IL DATE. Chaque maillon porte ses horodatages en
# base ; il ne fait que les mettre en colonne.
set -euo pipefail

REF="${1:-}"
RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFRA="$RACINE/deploy/docker-compose.yml"
UTILISATEUR="${POSTGRES_USER:-hba}"

command -v docker >/dev/null || { echo "docker est introuvable." >&2; exit 1; }

# PAR LE CONTENEUR, PAS PAR LE MAC. psql n'est pas installe partout, et l'image
# Postgres en porte un qui parle exactement la bonne version.
sql() {
  local base="$1" requete="$2"
  docker compose -f "$INFRA" exec -T postgres \
    psql -U "$UTILISATEUR" -d "$base" -c "$requete" 2>&1
}

echo "══ Les files d'envoi, tous services ══════════════════════════════"
echo "UNE FILE QUI NE SE VIDE PAS EST LA PANNE, pas une lenteur. « en_retard »"
echo "compte ce qui attend depuis plus de dix secondes ; « reessais » ce qui a"
echo "deja echoue au moins une fois et patiente en attente exponentielle."
echo
printf '  %-14s %9s %9s %9s  %s\n' service en_attente en_retard reessais plus_ancien
for base in hba_identity hba_directory hba_delivery hba_pricing hba_dispatch \
            hba_driver hba_payment hba_notification hba_media hba_billing \
            hba_ride hba_rating; do
  schema="${base#hba_}"
  ligne="$(docker compose -f "$INFRA" exec -T postgres psql -U "$UTILISATEUR" -d "$base" \
    -qAtF'|' -c "
      SELECT count(*),
             count(*) FILTER (WHERE \"OccurredAt\" < now() - interval '10 seconds'),
             count(*) FILTER (WHERE \"Attempts\" > 0),
             coalesce(to_char(min(\"OccurredAt\"), 'HH24:MI:SS'), '-')
      FROM $schema.outbox_messages
      WHERE \"PublishedAt\" IS NULL;" 2>/dev/null || true)"

  if [[ -z "$ligne" ]]; then
    printf '  %-14s %9s\n' "$schema" "illisible"
    continue
  fi
  IFS='|' read -r total retard reessais ancien <<< "$ligne"
  printf '  %-14s %9s %9s %9s  %s\n' "$schema" "$total" "$retard" "$reessais" "$ancien"
done

if [[ -z "$REF" ]]; then
  echo
  echo "Pour dater une commande precise :"
  echo "  $(basename "$0") <identifiant de course ou de livraison>"
  exit 0
fi

echo
echo "══ L'intention de paiement ═══════════════════════════════════════"
echo "« attente_du_client » EST LE TERME QUI N'EST PAS DE NOUS : page du"
echo "fournisseur, code de l'operateur, puis webhook par le tunnel."
sql hba_payment "
  SELECT purpose, status, amount,
         to_char(created_at,   'HH24:MI:SS') AS creee,
         to_char(succeeded_at, 'HH24:MI:SS') AS encaissee,
         succeeded_at - created_at           AS attente_du_client
  FROM payment.payment_intents
  WHERE reference_id = '$REF'
  ORDER BY created_at;"

echo "══ La boite d'envoi de Payment, autour de cette intention ════════"
sql hba_payment "
  SELECT \"EventType\",
         to_char(\"OccurredAt\",  'HH24:MI:SS') AS posee,
         to_char(\"PublishedAt\", 'HH24:MI:SS') AS publiee,
         \"PublishedAt\" - \"OccurredAt\"        AS delai,
         \"Attempts\" AS essais,
         left(coalesce(\"LastError\", ''), 60)   AS derniere_erreur
  FROM payment.outbox_messages
  WHERE \"PartitionKey\" = '$REF'
     OR \"OccurredAt\" >= (SELECT min(created_at) FROM payment.payment_intents
                          WHERE reference_id = '$REF')
  ORDER BY \"OccurredAt\"
  LIMIT 20;"

for base in hba_ride hba_delivery; do
  schema="${base#hba_}"
  table="${schema}s"

  trouve="$(docker compose -f "$INFRA" exec -T postgres psql -U "$UTILISATEUR" -d "$base" \
    -qAtc "SELECT 1 FROM $schema.$table WHERE id = '$REF';" 2>/dev/null || true)"
  [[ "$trouve" != "1" ]] && continue

  echo "══ La mission, chez $schema ══════════════════════════════════════"
  sql "$base" "
    SELECT status,
           to_char(requested_at,       'HH24:MI:SS') AS demandee,
           to_char(driver_assigned_at, 'HH24:MI:SS') AS affectee,
           driver_assigned_at - requested_at         AS recherche
    FROM $schema.$table WHERE id = '$REF';"

  echo "══ Ce que $schema a recu de Kafka, sur la periode ════════════════"
  echo "RIEN ICI AVEC UNE INTENTION ENCAISSEE VEUT DIRE QUE L'EVENEMENT N'EST"
  echo "JAMAIS ARRIVE : regarder la file d'envoi de Payment, pas ce service."
  sql "$base" "
    SELECT \"EventType\",
           to_char(\"ReceivedAt\",  'HH24:MI:SS') AS recu,
           to_char(\"ProcessedAt\", 'HH24:MI:SS') AS traite,
           \"ProcessedAt\" - \"ReceivedAt\"        AS delai,
           \"Attempts\" AS essais
    FROM $schema.inbox_messages
    ORDER BY \"ReceivedAt\" DESC
    LIMIT 10;"
done

echo "══ Et les cinq secondes qui restent ══════════════════════════════"
echo "L'accueil de l'application cliente sonde toutes les CINQ SECONDES"
echo "(home_screen.dart). Une chaine parfaite se voit donc jusqu'a cinq"
echo "secondes apres coup. C'est le seul terme qui se reduit sans toucher a"
echo "l'infrastructure — et le reduire fait payer des donnees au client, sur"
echo "un reseau ou elles se rechargent."
