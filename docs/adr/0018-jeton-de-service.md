# 0018 — Un service s'authentifie auprès d'un autre avec un jeton émis par Identity

Statut : **acceptée** — septembre 2026
Prolonge l'[ADR 0007](0007-autorisation-cote-service.md).

## Contexte

L'ADR 0007 pose que l'autorisation se vérifie côté service. En pratique,
chaque service est en `[Authorize]` et `TokenForwardingInterceptor` reporte sur
l'appel sortant le JWT de l'utilisateur, lu dans le `HttpContext` entrant.

Cela suppose une requête entrante. Or deux appels n'en ont aucune :

- `Dispatch` → `Driver.FindAvailableNearby`, lancé par le planificateur de
  vagues (`DispatchScheduler`, un `BackgroundService`) ;
- `Delivery` → `Driver.GetDriverPublicProfile`, lancé par le consommateur de
  `hba.dispatch.events.v1` au reçu de `OfferAccepted`.

Dans les deux cas `HttpContext` est nul, aucun en-tête n'est posé, et
`DriverGrpcService` répond `Unauthenticated`. Conséquences, dans l'ordre où
elles se produisent : aucune vague ne s'ouvre, donc aucune offre n'est jamais
envoyée ; et si une offre l'était, la course resterait en `SEARCHING_DRIVER`
alors que le livreur voit sa mission acceptée dans son application. La boucle
paiement → dispatch → affectation ne pouvait pas se fermer.

Le défaut est passé inaperçu jusqu'ici parce que tous les appels gRPC
existants — Delivery vers Pricing et Payment, la gateway vers tout le monde —
partent d'une requête HTTP et disposent donc d'un jeton à reporter.

## Décision

**Identity émet des jetons de service**, par `client_credentials`, sur
`IssueServiceToken`. Comme `IssuePartnerToken`, l'appel est anonyme : le secret
client est l'authentification.

**Le jeton de service ne porte aucun rôle.** Son sujet vaut
`service:<client_id>`, sa liste `roles` est vide. C'est la garantie centrale :
le référentiel acteurs ne connaît pas de rôle « service », parce qu'un service
n'est pas un acteur. Un tel jeton passe l'authentification et échoue sur toute
règle qui demande *qui* appelle — il ne peut donc ni prendre une course, ni
lire un dossier livreur complet, ni agir au nom de quiconque. Les deux méthodes
qu'il ouvre ne font, elles, aucune vérification par appelant.

**Les services autorisés viennent de la configuration de l'hôte**, pas d'une
table. Un partenaire B2B est une entreprise : il naît, change de nom, se fait
couper, et cela se gère en base. Un service est un morceau du déploiement, au
même titre que sa chaîne de connexion : ajouter un service, c'est ajouter une
variable d'environnement et déployer.

**Un jeton utilisateur passe toujours avant le jeton de service.** Si l'appel
descend d'une requête HTTP porteuse d'un jeton, le fournisseur ne pose rien et
l'intercepteur reporte celui de l'utilisateur. Autrement, le jeton de service —
sans rôle — contournerait les règles au lieu de les satisfaire.

**Le service refuse de démarrer sans son secret.** Un secret vide ne se voit
pas : le service démarrerait, tournerait, et n'échouerait qu'au premier
événement Kafka, c'est-à-dire en production, sur une vraie course.

## Conséquences

- `Hba.BuildingBlocks.Grpc` référence désormais `Hba.Contracts`. Identity est
  l'autorité d'authentification de la plateforme ; son contrat de jeton est
  aussi fondamental ici que le format du JWT lui-même. L'alternative était de
  recopier le fournisseur dans chaque service qui appelle depuis un
  consommateur.
- Le fournisseur ouvre son propre canal gRPC, sans intercepteur : passer par un
  client enregistré avec `AddHbaGrpcClient` ferait appeler le fournisseur par
  l'intercepteur de jeton pour obtenir un jeton.
- Le jeton dure autant qu'un jeton utilisateur (15 minutes) et est renouvelé
  deux minutes avant terme. Il n'est pas révocable : suspendre un service se
  fait en changeant son secret et en redéployant, avec au plus 15 minutes de
  latence.
- Le secret doit être **identique** dans le `.env` d'Identity
  (`SERVICE_SECRET_<SERVICE>`) et dans celui du service appelant. Un écart se
  voit au démarrage du premier appel, dans les journaux du service appelant.

## Alternatives écartées

**Signer le jeton dans le service appelant**, avec la clé de signature
d'Identity. Moins de plomberie, mais un deuxième émetteur de jetons dans le
système, et la clé privée d'Identity recopiée dans chaque service : une fuite
ailleurs qu'à Identity permettrait alors d'émettre n'importe quel jeton, y
compris administrateur.

**Sortir ces méthodes de `[Authorize]`** et compter sur l'isolation du réseau
`hba-internal`. Immédiat, et c'est exactement ce que l'ADR 0007 refuse :
l'autorisation redeviendrait une propriété du réseau. Le jour où un service est
exposé par erreur, ou joignable depuis un conteneur compromis, la recherche de
livreurs et les profils publics le sont avec lui.
