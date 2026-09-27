# Démarrer en local

Les dépendances tournent dans Docker, les services .NET depuis l'IDE ou
`dotnet run`. Rien d'autre n'est nécessaire.

## DEUX PILES, QU'IL NE FAUT JAMAIS MÉLANGER

C'est l'erreur la plus facile à commettre, et elle se manifeste par un message
trompeur : `Failed to resolve 'postgres'` ou `Failed to resolve 'kafka:9094'`.
Le conteneur démarre, l'application écoute, mais elle ne voit aucune dépendance
— parce qu'elle a été placée sur un réseau où il n'y a personne.

**Tous les conteneurs partagent un réseau unique, `hba-internal`.** Il est créé
hors compose par `make net`, et déclaré `external` partout — les deux piles s'y
rattachent au lieu d'en créer chacune un. `make up`, `make infra-up` et
`make prod-service` en dépendent, donc tu n'as jamais à le créer à la main.

| | Développement | Conteneurs |
|---|---|---|
| Infrastructure | `make up` (`deploy/docker-compose.yml`) | `make infra-up` (`deploy/compose.infra.yml`) |
| Réseau Docker | `hba-internal` | `hba-internal` |
| Les services .NET | depuis l'IDE, sur `localhost` | `make prod-service D=…` ou `make prod-up` |
| Adresses vues par le service | `localhost:5432`, `localhost:9092` | `postgres:5432`, `kafka:9094` |
| `.env` par service | inutile | obligatoire |

Depuis l'unification, un service conteneurisé résout `postgres` et `kafka`
quelle que soit la pile démarrée. Les deux exposent en revanche les mêmes ports
sur l'hôte : **elles ne peuvent toujours pas tourner en même temps**, et passer
de l'une à l'autre demande un `make down` d'abord.

La pile de développement est celle décrite dans la suite de ce document. Pour
la pile conteneurisée, voir `deploy/README.md`.

## 1. Les dépendances

```bash
make up
docker compose -f deploy/docker-compose.yml ps
```

Attendre que `postgres` soit `healthy`. `postgres` crée au premier démarrage
les huit bases via `deploy/postgres/init-databases.sh`, et `kafka-init` crée
les sujets. Ces deux-là ne s'exécutent **qu'une fois**, à la création des
volumes : si les bases manquent, c'est que le volume `pgdata` date d'avant le
script — `make down && docker volume rm hba-delivery-dev_pgdata` le règle.

## 2. Le schéma

**Seulement si tu lances les services depuis l'IDE** : le schéma se crée seul au
démarrage, `Database:AutoMigrate` vaut `true` en développement.

**Si tu lances les services en conteneur**, non. Leur `ASPNETCORE_ENVIRONMENT`
vaut `Production`, où `AutoMigrate` vaut `false` — un service ne modifie pas le
schéma de sa base au démarrage. C'est délibéré, et ça veut dire que tu dois
appliquer les migrations toi-même :

```bash
make migrate
make identity-key    # cle de signature des jetons : sans elle, aucune connexion
```

Sans ça, le service démarre, écoute, puis échoue en boucle sur
`relation "identity.outbox_messages" does not exist` : l'Outbox interroge une
table qui n'a jamais été créée.

## 3. Les services

Pour voir une connexion fonctionner de bout en bout, quatre processus
suffisent :

| Projet | HTTP | gRPC |
|---|---|---|
| `Hba.Identity.Api` | 5001 | 6001 |
| `Hba.Directory.Api` | 5008 | 6008 |
| `Hba.Notification.Api` | 5007 | 6007 |
| `Hba.Gateway` | 5100 | — |

Les autres, pour mémoire : Pricing 5002/6002, Delivery 5003/6003,
Dispatch 5004/6004, Driver 5005/6005, Payment 5006/6006, et les passerelles
Les quatre surfaces — client, livreur, portail, partenaires — passent par ce
seul port, distinguees par leur prefixe d'URL.

## Pourquoi deux ports par service

UN SERVICE gRPC EN CLAIR DOIT ECOUTER EN HTTP/2 EXCLUSIF. Sans TLS, il n'y a
pas d'ALPN, donc pas de négociation : Kestrel configuré en `Http1AndHttp2`
retombe sur HTTP/1.1 et tous les appels gRPC échouent. La documentation
Microsoft est explicite là-dessus.

Mais Identity sert aussi `/.well-known/openid-configuration` et
`/.well-known/jwks.json`, que le middleware JWT de chaque service va lire en
HTTP/1.1. Un point d'écoute unique en HTTP/2 casserait la découverte des clés.

D'où deux points d'écoute par service : `Http` en HTTP/1.1 pour la découverte,
la santé et la racine, `Grpc` en HTTP/2 pour le reste. `Jwt:Authority` vise le
premier, `Services:*` le second.

Conséquence pratique : `applicationUrl` a disparu des `launchSettings.json`.
`ASPNETCORE_URLS` et `ASPNETCORE_HTTP_PORTS` ont priorité sur
`Kestrel:Endpoints` et écraseraient cette configuration.

## 4. Vérifier

**Le code `000000` ne vaut QU'EN DÉVELOPPEMENT**, c'est-à-dire quand Identity
tourne depuis l'IDE. Il vient de `Otp:FixedCodeForDevelopment`, présent dans
`appsettings.Development.json` et absent d'`appsettings.json`.

**En conteneur, `ASPNETCORE_ENVIRONMENT` vaut `Production` : le code est tiré au
hasard et part réellement** par le canal configuré. Avec `SMS_PROVIDER=ovh`,
chaque demande consomme un crédit OVHcloud. Le délai entre deux demandes pour un
même numéro passe aussi de 5 à 60 secondes.

```bash
curl -s localhost:5100/api/client/v1/auth/otp/request \
  -H 'Content-Type: application/json' \
  -d '{"phone":"+22997000001","deviceId":"poste-hector"}'
```

Réponse : un `challengeId`. Puis :

```bash
curl -s localhost:5100/api/client/v1/auth/otp/verify \
  -H 'Content-Type: application/json' \
  -d '{"challengeId":"LE_CHALLENGE","code":"000000","deviceId":"poste-hector","displayName":"Hector"}'
```

Réponse : `accessToken` et `refreshToken`. Enfin :

```bash
curl -s localhost:5100/api/client/v1/me -H "Authorization: Bearer LE_JETON"
```

Ce que ces trois appels prouvent, mis bout à bout : Identity écrit en base,
publie dans son Outbox, Kafka transporte, Directory crée le profil du client,
Notification consigne l'envoi. C'est toute la mécanique Outbox / Inbox qui
tourne.

Pour le constater :

```bash
psql postgresql://hba:hba@localhost:5432/hba_directory -c 'select display_name, phone from customers;'
psql postgresql://hba:hba@localhost:5432/hba_notification -c 'select channel, template_id, status from sent_notifications;'
```

Le code de connexion apparaît aussi dans les journaux de Notification :
`Sms:Provider` vaut `log` en développement, et ce fournisseur refuse de
s'activer ailleurs.

## Ce qui ne fonctionne pas encore

**Créer une livraison échoue**, et c'est attendu : `CreateDeliveryHandler`
appelle Pricing pour consommer un devis, et Pricing est encore une coquille qui
répond `UNIMPLEMENTED`. Même chose pour le paiement, le dispatch et le livreur.

Le parcours client complet attend les étapes 4 à 6 du chemin vers la
production.

## Le mot de passe administrateur

`Bootstrap:AdminPassword` vaut `changez-ce-mot-de-passe` en développement, et
le compte est créé au démarrage d'Identity. C'est commode en local, et ce n'est
pas une valeur qui doit sortir d'ici.
