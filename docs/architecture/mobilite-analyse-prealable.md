# Passage de HBA Delivery à une plateforme de mobilité — analyse préalable

Rapport de phase 1. Rédigé le 1er octobre 2026. **Aucun code n'a été modifié.**

> **Les six questions du chapitre 0 sont tranchées depuis le 1er octobre 2026.**
> Les décisions font foi dans [points-a-trancher.md](points-a-trancher.md),
> point 35 ; ce chapitre garde l'analyse qui y a conduit.
>
> En bref : `customer` vaut passager (pas de rôle `passenger`) · un topic par
> service, `hba.ride.events.v1` · pas de `causation_id` · `MoneyXof`, XOF seul,
> pas de champ `Currency` · prix ferme encaissé à la commande · `Result` dans
> Ride uniquement, traduit en refus à sa frontière.

---

## 0. Contradictions à trancher AVANT toute ligne de code

Le référentiel des acteurs impose : *« Si une demande contredit une règle
ci-dessus, signale la contradiction avant de produire quoi que ce soit. »*
Six points le font.

### 0.1 « Passenger » est un acteur inventé — À TRANCHER

Le référentiel dit : *« N'invente pas d'acteur »* et *« Emploie exactement ces
noms d'acteurs et de rôles (`customer`, `driver`, `merchant_owner`,
`merchant_staff`, `partner`, `admin`, `ops`, `support`, `finance`, `service`)
dans le code, les claims JWT et la documentation. »*

`HbaRoles` (BuildingBlocks.Security) porte exactement ces dix rôles. La demande
introduit `PASSENGER`, ainsi que `PassengerId` sur l'agrégat et une section
« PASSENGER » dans les permissions.

Trois options, et il faut choisir explicitement :

| Option | Conséquence |
|---|---|
| **A — Réutiliser `customer`** | Aucun changement de JWT, d'`HbaRoles`, ni de référentiel. Le même compte envoie un colis et prend une course. `Ride.PassengerId` devient `CustomerId`. C'est cohérent avec « Ne pas dupliquer les services existants ». |
| **B — Ajouter `passenger`** | Nouveau rôle dans `HbaRoles`, dans Identity, dans les claims, dans la matrice de visibilité. Justifié seulement si un passager doit être un compte distinct d'un client colis — ce que rien n'indique aujourd'hui. |
| **C — Garder `customer` et n'ajouter que le mot « passager » à l'interface** | Le vocabulaire visible change, le modèle non. |

**Recommandation : A.** Le référentiel devra être amendé AVANT le code pour
dire qu'un `customer` peut être expéditeur de colis ou passager — c'est la
procédure suivie le 30 septembre pour le type de véhicule.

### 0.2 Nommage des topics Kafka — la convention existante gagne

La demande liste `ride.requested`, `dispatch.failed`, `payment.captured` : un
topic par type d'événement. Le dépôt utilise **un flux par service** :

```
hba.identity.events.v1   hba.delivery.events.v1   hba.dispatch.events.v1
hba.driver.events.v1     hba.payment.events.v1    hba.pricing.events.v1
hba.billing.events.v1    hba.directory.events.v1  hba.notification.commands.v1
```

Le type précis voyage dans `EventEnvelope.event_type`
(`hba.delivery.v1.DriverAssigned`). La demande dit elle-même : *« NE PAS imposer
une nouvelle organisation si une convention existe déjà. »*

**Décidé par cette règle : `hba.ride.events.v1`, un seul topic**, avec les neuf
types d'événements dans l'enveloppe. Passer à un topic par événement
multiplierait par six le nombre de topics et casserait les consommateurs
existants.

### 0.3 `causation_id` n'existe pas dans l'enveloppe

`EventEnvelope` porte `event_id`, `event_type`, `schema_version`,
`aggregate_type`, `aggregate_id`, `occurred_at`, `trace_id`, `correlation_id`,
`partner_id`, `actor_type`, `actor_id`, `producer`. **Pas de `causation_id`.**

L'ajouter est un changement de contrat commun à tous les services. Protobuf le
permet sans casse (nouveau champ 13), mais il faut le décider : aujourd'hui le
chaînage se fait par `correlation_id` seul.

### 0.4 Result/Error contre exceptions — divergence assumée à confirmer

La demande impose : *« Utiliser Result/Error plutôt que des exceptions pour le
flow métier normal. »*

Le dépôt fait **exactement l'inverse, partout et volontairement** :
`DomainException` avec un code, traduite en frontière par
`ExceptionInterceptor` (gRPC) et `TraduireLesRefus` (HTTP). Chaque service a son
fichier `*ErrorCodes.cs`. Basculer en `Result<T>` est une réécriture de dix
services, sans rapport avec l'ajout de Ride.

**Recommandation : garder les exceptions de domaine**, et ne pas introduire
`Result` dans Ride seul — deux conventions de gestion d'erreur dans le même
dépôt coûtent plus que celle qu'on remplace.

### 0.5 Montants : `long` XOF contre `decimal` multi-devise

L'ADR 0006 fixe les montants en **entiers XOF** (`MoneyXof`, `long`) : le franc
CFA n'a pas de subdivision. La demande impose `decimal` ou `Money` — le dépôt
est déjà conforme à l'esprit (jamais de `double`), mais le champ `Currency` sur
`Ride` suppose plusieurs devises, ce que l'ADR 0006 ne couvre pas.

**À trancher : le multi-devise est-il réel à court terme ?** Si non, `Ride`
porte `MoneyXof` comme le reste et ne garde pas de champ `Currency`.

### 0.6 Payment : `Authorized` / `Captured` ne sont pas implémentables aujourd'hui

C'est le point le plus lourd, et il ne vient pas d'une préférence de style.

Le flux demandé est : *Estimate → **Payment Authorization** → Dispatch → … →
Final Price → **Capture***.

Or :

- `PaymentStatus` vaut `Pending, Succeeded, Failed, Expired, Refunded,
  PartiallyRefunded`. **Il n'y a ni `Authorized` ni `Captured`.**
- Le seul fournisseur branché est **FedaPay** (ADR 0017). FedaPay est un
  agrégateur de paiement mobile : il encaisse, il ne pré-autorise pas.
- **FedaPay n'a pas d'API de remboursement** — vérifié dans sa documentation le
  30 septembre 2026, et déjà inscrit dans le code du client : les
  remboursements se font à la main depuis le tableau de bord marchand, et
  uniquement sur MTN Mobile Money.

Autrement dit : l'autorisation-puis-capture n'existe pas chez le fournisseur, et
la compensation « différence prix estimé / prix final » n'a pas non plus de
chemin automatique.

Trois options :

| Option | Description |
|---|---|
| **A — Prix ferme à la commande** | On encaisse le prix estimé, comme pour un colis aujourd'hui. Le prix final est le prix estimé. Aucune capture, aucune compensation. Implémentable **maintenant**. |
| **B — Encaissement à l'arrivée** | La course part sans paiement ; on encaisse le prix final à `Completed`. Expose au non-paiement, ce qui pour une course de personnes est un risque réel. |
| **C — Vraie autorisation** | Demande un fournisseur qui la propose (carte, ou un agrégateur qui l'offre). C'est un changement de fournisseur, pas une évolution de Ride. |

**Recommandation : A pour le MVP**, avec la note que le passage à C est une
décision commerciale, pas technique.

---

## A. État actuel

### A.1 Structure réelle (à respecter, elle diffère de celle proposée)

```
src/Services/<Service>/
├── Hba.<Service>.Api/             (ASP.NET Core + gRPC + consumers Kafka)
├── Hba.<Service>.Application/
├── Hba.<Service>.Domain/
├── Hba.<Service>.Infrastructure/
├── docker-compose.yml             (un par service, PAS dans deploy/)
└── tests/
    ├── Hba.<Service>.Domain.Tests/
    ├── Hba.<Service>.Application.Tests/   (quand il y en a)
    └── Hba.<Service>.Integration.Tests/   (quand il y en a)
```

La structure `Ride/src/Ride.Api/` + `tests/Ride.UnitTests/` proposée dans la
demande **n'est pas celle du dépôt**. On garde `Hba.Ride.Api`,
`Hba.Ride.Domain.Tests`, etc.

Chaque service a son propre `docker-compose.yml` et son `.env`. Le Makefile
pilote l'ensemble (`make relancer`, `make rebuild D=...`), avec
`SERVICE_DIRS` à compléter pour tout nouveau service.

### A.2 BuildingBlocks disponibles

| Bloc | Contenu utile |
|---|---|
| `Domain` | `AggregateRoot`, `Entity`, `ValueObject`, `Actor`, `DomainException`, `IDomainEvent` |
| `Application` | `Dispatcher` (CQRS maison : `SendAsync` pour `ICommand<T>`, **`QueryAsync` pour `IQuery<T>`**), `ICallerContext`, `IClock`, `IIdempotencyStore`, `IUnitOfWork` |
| `Messaging` | `IOutbox`, `OutboxDispatcher`, `OutboxMessage`, `InboxMessage`, `IKafkaProducer`, `KafkaConsumerBase`, `KafkaTopics` |
| `Persistence` | `EfOutbox`, `EfInboxStore`, `IdempotencyRecord`, `DatabaseMigrator`, `ChaineDeConnexion` |
| `Security` | `HbaRoles`, `HbaPolicies`, `HbaClaims`, `HbaRateLimiting` |
| `Grpc` | `ExceptionInterceptor` |
| `Observability` | OpenTelemetry déjà câblé, `service.name` dans les logs |

**Tout ce qu'il faut pour un nouveau service existe.** Un service neuf ne
réécrit ni outbox, ni inbox, ni idempotence, ni sécurité.

### A.3 Observabilité

OpenTelemetry est en place (`Hba.BuildingBlocks.Observability`), les logs sont
structurés en JSON avec `service.name`, `@tr`/`@sp` (trace et span) apparaissent
dans les journaux. Collector OTLP, Prometheus, Loki, Tempo et Grafana sont dans
`deploy/docker-compose.yml`. **Les métriques métier de la demande
(`ride_requests_total`, `dispatch_duration_seconds`…) n'existent pas.**

---

## B. Composants réutilisables tels quels

1. **Tout `BuildingBlocks`** (voir A.2).
2. **`EventEnvelope`** — sauf `causation_id` (§0.3).
3. **Outbox/Inbox** — ADR 0003, éprouvé sur huit services.
4. **`IPaymentProvider`** — l'abstraction demandée **existe déjà**
   (`Hba.Payment.Application/Common/Interfaces/IPaymentProvider.cs`). Ajouter
   MTN/Moov/Celtis est une implémentation, pas une refonte.
5. **`MoneyXof`** — value object entier, ADR 0006.
6. **`RedisAcceptanceLock`** — le verrou contre la double acceptation demandé au
   §5 **existe déjà** dans Dispatch (`StringSetAsync(..., When.NotExists)`).
7. **`RedisDriverLocationStore`** — Redis GEO, fraîcheur des positions,
   `GeoAdd`/recherche, `MeasureAsync`. Voir §C.2 : c'est le futur service
   Location, déjà écrit.
8. **`DispatchAggregate`** — vagues, offres, expiration, acceptation,
   épuisement. Bien plus avancé que ce que suppose la demande.
9. **Passerelle unique** (ADR 0016) avec `Endpoints/{Client,Driver,Partner,Public,Relais,Web}`.
10. **Conteneur OSRM** — déjà dans `deploy/docker-compose.yml`, **mais aucun
    code ne s'en sert**.

---

## C. Composants à modifier

### C.1 Driver — capabilities

Existant : `DriverAggregate`, `Vehicle` (Type, Plate, CapacityGrams),
`VehicleType {Motorcycle, Car, Van, Bicycle, Tricycle}`, `PiecesRequisesPour`,
`EstDeclare`, `SansPlaque`.

Manquant : `DriverCapability`, la table des combinaisons interdites, et
`PassengerCapacity`/`Brand`/`Model`/`Color` sur `Vehicle`.

**Driver n'a aucun projet de tests.** C'est la première phase du plan et la
moins couverte du dépôt : `PiecesRequisesPour` et `EstDeclare`, écrits hier, ne
sont testés nulle part.

### C.2 Location — le service existe déjà, mais dans Driver

C'est la découverte la plus importante de cette analyse.

`Hba.Driver.Infrastructure/Services/Locations/RedisDriverLocationStore.cs`
implémente déjà : `UpsertAsync`, `RemoveAsync`, `SearchAsync` (Redis GEO),
`ListFreshAsync`, `MeasureAsync`, et une **fenêtre de fraîcheur**
(`DRIVER_POSITION_FRESHNESS_SECONDS`, déjà dans le `.env` de Driver). Aucune
écriture SQL par point GPS.

La demande sépare Location de Driver. Deux options :

| Option | Conséquence |
|---|---|
| **A — Extraire un service Location** | Conforme à la demande. Coût : nouveau service, nouveau `.env`, nouveau compose, et surtout **Dispatch et Driver doivent changer d'interlocuteur**. Le gain immédiat est faible : la charge GPS ne pèse pas sur Postgres aujourd'hui. |
| **B — Garder les positions dans Driver pour l'instant** | Zéro migration. Le jour où la charge le justifie, l'extraction est mécanique : le store est déjà derrière une interface. |

**Recommandation : B pour le MVP mobilité, A quand la charge l'exigera.** La
raison invoquée par la demande — « PostgreSQL ne doit pas recevoir chaque update
GPS » — est **déjà satisfaite**.

### C.2 bis — TRANCHÉ LE 1er OCTOBRE 2026 : option A, contre cette recommandation

**L'option A a été retenue, et la recommandation ci-dessus est conservée telle
quelle** : elle dit ce qu'on a pesé, et la décision l'emporte sans l'effacer. Ce
qui a fait pencher la balance n'est pas la charge — le constat « Postgres ne reçoit
déjà aucun point GPS » reste exact — mais la **frontière de données** : Driver
portait à la fois le fichier des personnes (nom, téléphone, pièces d'identité) et
la trace de l'endroit où elles se tiennent. Les séparer donne un service qui tient
des positions **sans savoir à qui elles appartiennent**.

Ce que la décision a coûté, et qu'il faut connaître avant de la discuter :

| Conséquence | Détail |
|---|---|
| **Le croisement passe chez l'appelant** | `FindAvailableNearby` faisait tout en un appel : Redis GEO pour la proximité, Postgres pour l'éligibilité. Les deux moitiés sont maintenant deux services, et c'est `DriverFinder` (Dispatch), `ClientNearbyEndpoints` et `BackOfficeEndpoints` (passerelle) qui recollent. **Deux appels gRPC là où il y en avait un**, sur le chemin de la première offre. |
| **La sur-interrogation a déménagé** | Elle vivait dans le handler de Driver (facteur 5). Elle est maintenant chez chaque appelant, et **chacun a la sienne** : 5 pour le dispatch, qui filtre aussi sur le véhicule et l'habilitation ; 3 pour la carte client, qui ne filtre que l'état. |
| **`DRIVER_POSITION_STALE` a changé de bouche** | Driver ne sait plus dire « son téléphone est éteint ». Ce motif naît désormais dans `DriverFinder`, seul endroit du dépôt qui le prononce. L'ordre de précédence est préservé : le motif de Driver gagne, parce qu'un livreur suspendu qui laisse tourner son téléphone a une position parfaitement fraîche. |
| **Effacer un point ne porte plus de décision** | C'est le gain inattendu. `GoOffline`, `SuspendDriver` et le refus de KYC effaçaient la position, et l'oublier laissait quelqu'un rentré chez lui recevoir des offres. Aujourd'hui la vague croise toujours la base : un point qui traîne ne produit plus aucune offre par lui-même. L'oubli est devenu de l'**hygiène d'affichage**, et c'est pour cela qu'il peut vivre à la passerelle. |
| **Driver n'a plus aucune dépendance à Redis** | Plus un `using StackExchange.Redis`, plus le paquet dans le `.csproj`, plus de `ConnectionStrings:Redis`. C'est vérifiable d'un coup d'œil, et si l'un revient c'est que les positions rentrent par la fenêtre. |

**Trois éléments de la demande n'ont PAS été implémentés, par décision explicite :**

- **L'historique de déplacement** (« éventuellement » dans la demande). C'est le
  seul élément qui demande un stockage durable, donc une base, une rétention et
  une règle d'accès — et la trace GPS d'un travailleur est une donnée personnelle.
  Rien n'est stocké au-delà de la dernière position connue. **Point à trancher à
  part**, quand un besoin précis le demandera : litige sur une course, contrôle
  d'un détour.
- **La diffusion temps réel.** Elle appartient à la phase 8, qui décidera si le hub
  SignalR vit dans Location ou dans une passerelle temps réel dédiée. Les lecteurs
  sondent en attendant.
- **La présence, pour sa moitié métier.** `GoOnline` / `GoOffline` et
  `OperationalStatus` restent dans Driver. Location ne connaît que la fraîcheur.

**Ce que la décision a apporté au passage, et qui n'était pas demandé :** le cap,
la vitesse et la précision GPS sont enfin transportés et conservés. Les deux
premiers existaient dans `UpdateLocationRequest` **depuis l'origine** et étaient
jetés à la frontière gRPC — ni la passerelle ni l'application ne les remplissaient.
C'est pour cela qu'aucune carte n'a jamais pu orienter une flèche. Même famille de
défaut que `package_weight_grams`, trouvée le même jour.

### C.3 Routing — abstraction présente, fournisseur absent

`Hba.Pricing.Application/Common/Interfaces/IRouteEngine.cs` existe, avec une
seule implémentation : `HaversineRouteEngine` — distance **à vol d'oiseau**.

Conséquence directe, à dire clairement : **les prix affichés aujourd'hui sont
calculés sur une distance à vol d'oiseau**, pas sur un trajet routier. Les
1650 F / 8,2 km d'hier soir sont une approximation. Brancher OSRM changera les
prix.

Il manque aussi la **matrice** (`CalculateMatrixAsync`), qui est ce dont
Dispatch a besoin pour comparer les ETA de plusieurs chauffeurs.

### C.4 Dispatch — découplage du type de course

`DispatchAggregate` porte `DeliveryId` en dur, ainsi que `PickupLandmark`,
`TripDistanceMeters`, `DriverEarningXof`. Il faut passer à
`(ServiceType, ReferenceId)`. C'est une migration de schéma et un changement de
contrat gRPC/Kafka.

La sélection se fait aujourd'hui sur la **distance Redis GEO**, sans ETA
routier : l'étape 5 de l'algorithme demandé est la vraie nouveauté.

### C.5 Pricing — stratégies et grilles

`Tariff` porte déjà `ZoneCode`, `VehicleType`, `BaseFare`, `PerKilometer`,
`PerMinute`, `MinimumFare`, `SurgeBasisPoints`, `DriverShareBasisPoints`,
`ValidFrom/ValidUntil`. **La formule demandée est déjà celle du domaine.** Il
manque : `ServiceType`, la plage horaire, et `IPricingStrategy`.

**Blocage connu, signalé le 30 septembre et toujours ouvert :** Pricing
n'expose **aucune route** pour créer ou modifier une grille.
`InitialTariffSeeder` en sème exactement **une**, depuis la configuration. Une
course à vélo, à tricycle ou en voiture échoue donc sur `NO_TARIFF`. Ce point
bloque la phase 6 entière et doit être traité avant, pas pendant.

Second blocage : `ClientDeliveryEndpoints` fixe `VehicleType.Motorcycle` **en
dur** pour le devis client.

### C.6 Payment — référence générique

`PaymentIntent.DeliveryId` est obligatoire (`MissingDeliveryId`). Il faut
`(ReferenceType, ReferenceId)`. Migration + contrat. Voir §0.6 pour
l'autorisation/capture.

---

## D. Nouveaux composants

| Composant | Nature | Remarque |
|---|---|---|
| `Hba.Ride.*` (4 projets + tests) | Neuf | Le cœur de la demande |
| `Hba.Routing.*` | Neuf | Fournisseur OSRM à écrire ; conteneur déjà là |
| `Hba.Location.*` | **Optionnel** | Voir §C.2 |
| `contracts/proto/hba/ride/v1/` | Neuf | `ride_service.proto`, `ride_events.proto` |
| `DriverCapability` + table de compatibilité | Neuf | Dans Driver |
| Temps réel (SignalR) | **Entièrement neuf** | Voir ci-dessous |
| Métriques métier | Neuf | Aucune des dix métriques demandées n'existe |

**Le temps réel n'existe nulle part.** Aucun `Hub`, aucune référence SignalR
dans la solution. L'application livreur **sonde toutes les 5 secondes**, et son
propre commentaire dit : *« tant qu'elle n'existe pas, sonder reste le moyen le
plus simple »*. C'est le plus gros morceau neuf de tout ce chantier, et le plus
sous-estimé dans la demande : il touche la passerelle, les deux applications
Flutter, et l'authentification des connexions persistantes.

---

## E. Dépendances entre services

```
Ride ──(gRPC)──> Pricing          estimation
     ──(gRPC)──> Payment          intention de paiement
     ──(Kafka)─> hba.ride.events.v1
                      │
                      ▼
                  Dispatch ──(gRPC)──> Driver    (disponibilité, véhicule, capability)
                           ──(gRPC)──> Routing   (matrice ETA)
                           ──(Kafka)─> hba.dispatch.events.v1
                                            │
                                            ▼
                                          Ride   (affectation)

Routing ──> OSRM (HTTP)        aucun service métier ne connaît OSRM
Pricing ──> Routing            remplace HaversineRouteEngine
```

**Aucun cycle.** Ride ne parle jamais à Driver ni à Location : c'est Dispatch
qui le fait, conformément à la demande.

Point de vigilance : Pricing dépendra de Routing, et Dispatch aussi. Routing
doit donc rester sans dépendance métier — un pur calculateur.

---

## F. Contrats et événements nécessaires

Nouveau `contracts/proto/hba/ride/v1/ride_events.proto`, neuf types dans le
topic `hba.ride.events.v1` :

`RideRequested`, `RideDriverSearchStarted`, `RideDriverAssigned`,
`RideDriverArriving`, `RideDriverArrived`, `RideStarted`, `RideCompleted`,
`RideCancelled`, `RideNoDriverAvailable`.

Modifications de contrats existants :

- `hba/common/v1/envelope.proto` : `causation_id = 13` (si §0.3 est retenu).
- `hba/common/v1/enums.proto` : `ServiceType {DELIVERY, RIDE}`,
  `DriverCapability`.
- `hba/dispatch/v1/*` : `delivery_id` → `service_type` + `reference_id`.
- `hba/payment/v1/*` : idem.
- `hba/driver/v1/driver_service.proto` : capabilities dans `Driver` et filtre
  dans `FindAvailableNearby`.
- `hba/pricing/v1/pricing_service.proto` : `service_type` dans le devis.

Les numéros de champs ne se réutilisent ni ne se réordonnent — règle déjà
inscrite dans `enums.proto`.

---

## G. Migrations de base nécessaires

| Base | Migration |
|---|---|
| `hba_ride` | **Création complète** (+ entrée dans `deploy/postgres/init-databases.sh`, + `apply-migrations.sh`) |
| `hba_driver` | `driver_capabilities` ; `vehicles` : `Brand`, `Model`, `Color`, `PassengerCapacity` |
| `hba_dispatch` | `DeliveryId` → `ServiceType` + `ReferenceId` (reprise des lignes existantes) |
| `hba_payment` | `DeliveryId` → `ReferenceType` + `ReferenceId` (idem) |
| `hba_pricing` | `tariffs` : `ServiceType`, plage horaire ; **et une route d'administration des grilles** |

Les deux migrations de renommage portent des données de production : elles
doivent copier avant de supprimer, comme l'a fait `20260930151422_ProofMedia`
avec son garde-fou.

---

## H. Plan d'implémentation

L'ordre proposé dans la demande est bon, à trois corrections près.

**Phase 0 — Décisions (aucun code).** Trancher §0.1 à §0.6. Amender le
référentiel des acteurs et `points-a-trancher.md` AVANT le code, comme pour le
type de véhicule le 30 septembre.

**Phase 0 bis — Débloquer Pricing.** Écrire la route d'administration des
grilles tarifaires et retirer le `VehicleType.Motorcycle` en dur. Sans cela,
toute course non-moto échoue, et la phase 6 travaillerait sur du sable.

**Phase 1 — Driver : projet de tests d'abord, puis capabilities.** La demande
place Driver en premier ; j'ajoute que son projet de tests doit précéder la
modification, pas la suivre. `PiecesRequisesPour` et `EstDeclare` sont écrits et
non couverts.

**Phase 2 — Routing.** Extraire `IRouteEngine` de Pricing, écrire
`OsrmRouteProvider`, ajouter la matrice, brancher le cache Redis. À la fin,
Pricing calcule sur des distances routières — **et les prix changent**.

**Phase 3 — Ride Domain + Application.** Machine à états, value objects,
commandes, requêtes, événements, outbox. Entièrement testable sans
infrastructure : c'est la phase la plus sûre.

**Phase 4 — Pricing : stratégies.** `IPricingStrategy`, `ServiceType`, plages
horaires.

**Phase 5 — Dispatch : découplage.** `(ServiceType, ReferenceId)`, filtre par
capability, ETA routier à l'étape 5.

**Phase 6 — Payment.** Selon la décision §0.6.

**Phase 7 — Kafka de bout en bout.** Topic `hba.ride.events.v1`, consumers,
inbox.

**Phase 8 — Temps réel.** Le plus gros morceau neuf. À ne pas commencer avant
que le reste fonctionne en sondage.

**Phase 9 — Location.** FAITE le 1er octobre 2026 : l'option A de §C.2 a été
retenue, contre la recommandation. Voir §C.2 bis pour ce qu'elle a coûté, et pour les
trois éléments de la demande volontairement laissés de côté (historique, temps réel,
présence métier).

**Phase 10 — Tests d'intégration bout en bout** et métriques métier.

---

## Ce que je recommande de faire en premier

La phase 0 est une conversation, pas du code. Les six décisions du chapitre 0
changent la forme de presque tout ce qui suit — en particulier §0.1 (acteur) et
§0.6 (paiement), qui décident respectivement du modèle de `Ride` et de son
cycle de vie complet.

La phase 0 bis, elle, est du code, et elle est indépendante : la route
d'administration des grilles tarifaires manque déjà aujourd'hui, pour les
colis, avant toute question de mobilité.
