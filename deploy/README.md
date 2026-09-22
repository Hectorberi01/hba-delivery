# Déploiement

## Développement local

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Ce qui démarre : PostgreSQL + PostGIS (une base par service, créées au premier
lancement), Redis, Kafka en mode KRaft avec ses topics, Kafka UI sur
http://localhost:8081, MinIO sur http://localhost:9001, et la pile
d'observabilité — Grafana sur http://localhost:3000.

Les services .NET tournent depuis l'IDE : `appsettings.Development.json` pointe
déjà sur `localhost`.

### OSRM

Le routage est derrière le profil `routing` parce qu'il demande un fichier de
données à télécharger et à pré-traiter une fois :

```bash
mkdir -p deploy/.data/osrm && cd deploy/.data/osrm
curl -O https://download.geofabrik.de/africa/benin-latest.osm.pbf
docker run -t -v "$PWD:/data" ghcr.io/project-osrm/osrm-backend \
  osrm-extract -p /opt/car.lua /data/benin-latest.osm.pbf
docker run -t -v "$PWD:/data" ghcr.io/project-osrm/osrm-backend \
  osrm-partition /data/benin-latest.osrm
docker run -t -v "$PWD:/data" ghcr.io/project-osrm/osrm-backend \
  osrm-customize /data/benin-latest.osrm
cd - && docker compose -f deploy/docker-compose.yml --profile routing up -d osrm
```

## Production

```bash
cp deploy/.env.example deploy/.env   # puis remplir les secrets
docker compose -f deploy/docker-compose.prod.yml up -d --build
```

Seuls les trois BFF et la Partner API sont sur le réseau public. Le réseau
`hba-internal` est marqué `internal: true` : les services gRPC ne sont pas
joignables depuis l'extérieur, quelle que soit la configuration de Traefik.

## Ce que ce dossier ne fait pas encore

- Pas de sauvegarde automatisée de PostgreSQL ni de MinIO.
- Kafka tourne sur un seul nœud, avec un facteur de réplication de 1 : acceptable
  pour le pilote de Cotonou, pas pour un trafic réel.
- Pas de registre de schémas : les contrats protobuf sont validés au build par
  `buf`, pas à l'exécution.
