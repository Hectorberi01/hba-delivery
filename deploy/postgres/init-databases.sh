#!/bin/bash
# Une base par service. Aucun service ne lit la base d'un autre, même en lecture.
set -euo pipefail

for db in hba_identity hba_directory hba_delivery hba_pricing hba_dispatch hba_driver hba_payment hba_notification; do
  echo "Création de $db"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-SQL
    CREATE DATABASE $db;
SQL
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$db" <<-SQL
    CREATE EXTENSION IF NOT EXISTS postgis;
SQL
done
