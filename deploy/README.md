# Déploiement

## Développement local

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Ce qui démarre : PostgreSQL + PostGIS (une base par service, créées au premier
lancement), Redis, Kafka en mode KRaft avec ses topics, Kafka UI sur
http://localhost:8081, et la pile
d'observabilité — Grafana sur http://localhost:3000.

Les services .NET tournent depuis l'IDE : `appsettings.Development.json` pointe
déjà sur `localhost`.

## Chaque service se déploie seul

Chaque service porte son `docker-compose.yml` et son `.env.example`, à côté de
son code. Chacun est un **projet compose distinct** — `hba-identity`,
`hba-delivery`, `hba-gateway` — ce qui veut dire qu'un `docker compose down`
dans un dossier n'arrête que ce service.

```bash
make infra-up                                   # une fois : réseaux, base, broker
cd src/Services/Identity && cp .env.example .env && docker compose up -d

make prod-up                                    # infrastructure puis les douze
make prod-service D=src/Services/Identity       # un seul
make lint-compose                               # valide chaque fichier isolément
```

Sur un serveur qui n'a pas les sources :

```bash
docker compose pull && docker compose up -d --no-build
```

### Ce qui rend l'indépendance réelle

**Aucune référence croisée.** Pas d'`extends`, pas d'`include`, pas de
`depends_on` vers une base ou un broker que le service ne possède pas. Le prix
est six lignes d'environnement répétées douze fois ; le gain est qu'aucun
fichier ne peut casser les onze autres.

**Les réseaux sont externes.** `deploy/compose.infra.yml` crée `hba-internal` et
`hba-internal` une fois, hors compose (`make net`) ; toutes les piles et tous
les services le déclarent `external` et s'y rattachent. **Un seul réseau, pour
tous les conteneurs** — c'est ce qui permet à un service conteneurisé de
résoudre `postgres` et `kafka` quelle que soit la pile d'infrastructure
démarrée.
C'est le seul ordre imposé : l'infrastructure d'abord.

**Une image, pas un contexte de build.** Chaque compose référence
`${HBA_REGISTRY}/hba-<service>:${HBA_TAG}`. La section `build` reste pour le
poste de développement et la CI ; un serveur qui n'a pas l'arborescence tire
l'image et ne construit rien.

**Le `.env` n'est pas facultatif.** Les variables sensibles — chaînes de
connexion, `JWT_ISSUER`, `OTP_PEPPER`, `DATA_PROTECTION_KEY`, mots de passe —
sont déclarées sous la forme requise `${VAR:?message}`. Compose **refuse** de
démarrer si l'une manque, avant de créer le moindre conteneur, et nomme la
variable. C'est délibéré : un `.env` oublié doit arrêter le déploiement, pas
faire partir un service avec une chaîne de connexion vide ou une base ouverte
aux identifiants de développement.

`make lint-compose` reste utilisable sans secrets : il charge `deploy/lint.env`,
qui ne contient que des valeurs factices et ne doit jamais servir à démarrer
quoi que ce soit.

**Le `.env` est le seul couplage à l'environnement.** Adresse de la base, du
broker, du collecteur : rien n'est figé dans le compose.

### Ce qui reste partagé, et doit le rester

Postgres, Redis, Kafka, Traefik et le collecteur vivent dans
`deploy/compose.infra.yml`, projet `hba-infra`. Huit brokers Kafka séparés ne
s'échangent aucun événement, et huit bases Postgres séparées ne sont pas huit
schémas d'un même système. L'indépendance porte sur les services, jamais sur ce
qu'ils partagent.

### Un défaut corrigé au passage

La chaîne de connexion n'était passée nulle part en production : les services
retombaient sur `appsettings.json`, qui porte `Username=hba;Password=hba`.
Chaque compose exige désormais `<SERVICE>DB_CONNECTION`, **sans valeur par
défaut** — un démarrage qui échoue vaut mieux qu'une base ouverte avec un mot
de passe de développement.


réseaux, répétée dans chaque fichier.

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
./scripts/garage-secrets.sh --si-absent   # les trois secrets de Garage
docker compose -f deploy/docker-compose.prod.yml up -d --build
```

Seuls les trois BFF et la Partner API sont sur le réseau public. Le réseau
`hba-internal` est marqué `internal: true` : les services gRPC ne sont pas
joignables depuis l'extérieur, quelle que soit la configuration de Traefik.

## Ce que ce dossier ne fait pas encore

- Pas de sauvegarde automatisée de PostgreSQL.
- Pas de stockage objet : le moteur reste à choisir (point 14 de
  `docs/architecture/points-a-trancher.md`).
- Kafka tourne sur un seul nœud, avec un facteur de réplication de 1 : acceptable
  pour le pilote de Cotonou, pas pour un trafic réel.
- Pas de registre de schémas : les contrats protobuf sont validés au build par
  `buf`, pas à l'exécution.
