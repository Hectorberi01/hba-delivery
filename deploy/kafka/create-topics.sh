#!/bin/sh
# Topics du système. Compaction désactivée : ce sont des flux d'événements, pas
# des états. Rétention 7 jours en développement.
set -eu

BOOTSTRAP="${KAFKA_BOOTSTRAP:-kafka:9094}"
# Image apache/kafka : les scripts ne sont pas dans le PATH.
KT="${KAFKA_TOPICS_SH:-/opt/kafka/bin/kafka-topics.sh}"
PARTITIONS="${PARTITIONS:-6}"
REPLICATION="${REPLICATION:-1}"
RETENTION_MS="${RETENTION_MS:-604800000}"

# LE TOPIC DES COURSES MANQUAIT DEPUIS LA NAISSANCE DU SERVICE RIDE, et le
# defaut etait entierement silencieux. auto.create.topics.enable vaut « false »
# sur le broker — c'est voulu, pour qu'un topic ait toujours le bon nombre de
# partitions et la bonne retention — donc Ride publiait dans le vide : son
# expediteur d'outbox reessayait sans fin, aucun gain de chauffeur n'etait
# credite, aucune notification n'etait envoyee, et rien ne le disait ailleurs
# que dans ses propres journaux.
#
# TROUVE LE 1er OCTOBRE 2026, en preparant le redemarrage de la phase 8 : le
# service de diffusion s'abonne a ce topic, et c'est en verifiant qu'il existait
# qu'on a vu qu'il n'existait pas.
TOPICS="
hba.identity.events.v1
hba.directory.events.v1
hba.delivery.events.v1
hba.dispatch.events.v1
hba.driver.events.v1
hba.ride.events.v1
hba.payment.events.v1
hba.pricing.events.v1
hba.billing.events.v1
hba.notification.commands.v1
hba.platform.order.events.v1
"

for topic in $TOPICS; do
  echo "Topic $topic"
  "$KT" --bootstrap-server "$BOOTSTRAP" \
    --create --if-not-exists \
    --topic "$topic" \
    --partitions "$PARTITIONS" \
    --replication-factor "$REPLICATION" \
    --config retention.ms="$RETENTION_MS" \
    --config cleanup.policy=delete
done

echo "Topics en place :"
"$KT" --bootstrap-server "$BOOTSTRAP" --list
