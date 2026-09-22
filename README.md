# HBA Delivery

Domaine logistique commun à **HBA Express** (marketplace), **HBA Food**
(restauration) et, à terme, à des partenaires externes. Il exécute la livraison
physique ; il ne gère pas la commande commerciale.

Marché : Bénin, pilote à Cotonou, zone UEMOA. Monnaie XOF, montants entiers.

## Démarrer

```bash
# Dépendances locales : Postgres + PostGIS, Redis, Kafka, MinIO, observabilité
docker compose -f deploy/docker-compose.yml up -d

# Compiler
dotnet build HbaDelivery.sln

# Migrations : elles ne sont pas versionnées d'avance, une commande les produit
make migrations

# Tests du domaine (rapides, sans infrastructure)
make test-domain

# Règles de dépendance entre couches
make test-arch

# Parcours d'inscription de bout en bout (Postgres et Redis via Testcontainers)
make test-e2e
```

`make` sans argument liste toutes les cibles.

Prérequis : SDK .NET 9 et Docker. Sur macOS :
`brew install --cask dotnet-sdk` et OrbStack ou Docker Desktop.

Les migrations ne sont pas versionnées : un fichier de migration va de pair
avec un instantané du modèle, et les écrire à la main revient à mentir à EF Core
sur ce qu'il croit savoir de la base. `make migrations` les produit pour les
quatre services. Ensuite, `Database:AutoMigrate` les applique au démarrage —
vrai en développement, à votre main en production.

## Organisation

```
contracts/     source de vérité des échanges (gRPC + Kafka), en protobuf
src/
  BuildingBlocks/  briques communes : domaine, application, messagerie, gRPC,
                   observabilité, sécurité
  Gateways/        trois BFF REST + la Partner API publique
  Services/        Identity, Delivery, Pricing, Dispatch, Driver, Payment,
                   Notification
apps/          client Flutter, livreur Flutter, web Next.js (à échafauder)
tests/         règles d'architecture, parcours de bout en bout
deploy/        compose dev et prod, Traefik, topics Kafka, observabilité
docs/          architecture, référentiel des acteurs, ADR
```

## État

**Delivery est implémenté** : agrégat, machine à états avec sa table de
transitions par acteur, objets-valeurs, EF Core, Outbox et Inbox, entrée gRPC,
consommateurs Kafka, tests de domaine.

**Les six autres services sont des coquilles** qui compilent et démarrent :
quatre projets chacun, hôte gRPC dont chaque méthode renvoie `UNIMPLEMENTED`.
Leur domaine reste à écrire.

Les **BFF** exposent une surface REST réelle vers Delivery, Pricing, Dispatch et
Driver ; ils appelleront des méthodes non implémentées tant que les services
correspondants sont des coquilles.

## À lire avant de coder

1. [`docs/architecture/referentiel-acteurs.md`](docs/architecture/referentiel-acteurs.md)
   — la **référence unique** du projet. Tout en découle ; rien ne s'y ajoute
   sans décision.
2. [`docs/architecture/README.md`](docs/architecture/README.md) — la vue
   d'ensemble et les trois règles qui ne se négocient pas.
3. [`docs/architecture/points-a-trancher.md`](docs/architecture/points-a-trancher.md)
   — ce qui n'est **pas** implémenté, et pourquoi.
4. [`docs/adr/`](docs/adr/) — les décisions, une par fichier.

## Les trois règles qui ne se négocient pas

1. **L'autorisation se vérifie côté service.** Le BFF propage le jeton ; il ne
   décide rien.
2. **Le prix est figé à la confirmation.** Un changement de grille ne touche
   jamais une course déjà confirmée.
3. **Seul le livreur livre**, et seulement avec un OTP valide. Un administrateur
   clôt en `FAILED` ou `CANCELLED`, jamais en `DELIVERED`.

## Conventions

- Rôles, partout et à l'identique : `customer`, `driver`, `merchant_owner`,
  `merchant_staff`, `partner`, `admin`, `ops`, `support`, `finance`.
- Ne jamais confondre **Client** et **Destinataire**, ni **Partenaire**
  (un système) et **Commerçant** (une entreprise).
- Topics : `hba.<service>.events.v1`, clé de partition = identifiant de
  l'agrégat.
- Namespaces : `Hba.<Service>.<Couche>`.
