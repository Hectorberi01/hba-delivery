#!/bin/bash
# Topics du système. Compaction désactivée : ce sont des flux d'événements, pas
# des états. Rétention 7 jours en développement.
set -euo pipefail

BOOTSTRAP="${KAFKA_BOOTSTRAP:-kafka:9094}"
PARTITIONS="${PARTITIONS:-6}"
REPLICATION="${REPLICATION:-1}"
RETENTION_MS="${RETENTION_MS:-604800000}"

TOPICS=(
  "hba.identity.events.v1"
  "hba.directory.events.v1"
  "hba.delivery.events.v1"
  "hba.dispatch.events.v1"
  "hba.driver.events.v1"
  "hba.payment.events.v1"
  "hba.pricing.events.v1"
  "hba.notification.commands.v1"
  "hba.platform.order.events.v1"
)

for topic in "${TOPICS[@]}"; do
  echo "Topic $topic"
  kafka-topics.sh --bootstrap-server "$BOOTSTRAP" \
    --create --if-not-exists \
    --topic "$topic" \
    --partitions "$PARTITIONS" \
    --replication-factor "$REPLICATION" \
    --config retention.ms="$RETENTION_MS" \
    --config cleanup.policy=delete
done

echo "Topics en place :"
kafka-topics.sh --bootstrap-server "$BOOTSTRAP" --list
