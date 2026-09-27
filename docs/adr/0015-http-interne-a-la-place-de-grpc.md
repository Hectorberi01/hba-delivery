# 0015 — HTTP+JSON pour les appels internes, à la place de gRPC

Statut : **rejetée** — septembre 2026
Ne remplace rien : [0002](0002-grpc-et-kafka.md) reste en vigueur.

> **Pourquoi cet ADR est conservé bien que rejeté.** La question reviendra, et
> l'analyse qui a conduit au refus vaut mieux qu'un fichier supprimé.
>
> Le motif du changement était la difficulté à déboguer un appel interne. Trois
> faits ont fait pencher la balance dans l'autre sens :
>
> 1. **Protobuf reste de toute façon**, pour les 8 contrats d'événements Kafka.
>    Retirer gRPC ne supprime ni `Grpc.Tools`, ni `protoc`, ni la rustine
>    Alpine. Le chantier n'enlevait aucun coût d'outillage — il ajoutait un
>    second langage de schéma.
> 2. **Le typage de bout en bout protège une équipe d'une personne.** Un champ
>    renommé casse la compilation de l'appelant ; en JSON il casse en
>    production, à Cotonou, sans revue pour le rattraper.
> 3. **Le problème réel coûte deux lignes** : la réflexion gRPC rend `grpcurl`
>    utilisable sans descripteur. Une heure contre plusieurs jours.
>
> Si la question se repose — par exemple pour ouvrir ces appels à un tiers —, la
> voie à instruire en premier est le transcodage JSON, qui donne des routes REST
> à partir des mêmes `.proto` sans perdre le typage.

## Contexte

L'ADR 0002 a retenu gRPC pour le synchrone et Kafka pour l'asynchrone, avec
protobuf comme source de vérité des deux côtés. Le raisonnement tient toujours
sur le fond. Ce qui l'a mis en défaut, c'est l'exploitation.

**Un appel gRPC ne se rejoue pas à la main.** Quand une livraison échoue à
obtenir son devis, on ne peut pas ouvrir un terminal et refaire l'appel. Il faut
un client généré, ou `grpcurl` plus un descripteur. Les journaux montrent un
statut numérique et une charge utile binaire. Le temps passé à reproduire un
appel dépasse celui passé à corriger la cause.

C'est un coût qui se paie à chaque incident, sur un service qui démarre à
Cotonou avec une équipe réduite et un accès distant aux machines.

## Décision

**Les appels synchrones entre services passent en HTTP+JSON**, sur des routes
préfixées `/internal/v1/`, servies sur le port HTTP existant.

**Kafka et protobuf ne changent pas.** Les huit fichiers `*_events.proto` et
`EventEnvelope` restent la source de vérité de l'asynchrone. `Grpc.Tools` reste
donc dans la chaîne de compilation : ce chantier ne retire ni protobuf, ni
`protoc`.

Les contrats du synchrone deviennent des enregistrements C# dans `contracts/`,
sérialisés en JSON par `System.Text.Json`.

## Ce qui doit survivre au changement

gRPC ne transportait pas que des messages. Quatre comportements transverses
étaient portés par des intercepteurs, et les perdre serait une régression
silencieuse — le genre qui ne se voit qu'en production :

| Intercepteur gRPC | Équivalent HTTP |
|---|---|
| `ExceptionInterceptor` | Intergiciel qui traduit les exceptions de domaine en statut + code métier |
| `TokenForwardingInterceptor` | `DelegatingHandler` qui reporte le JWT |
| `CorrelationInterceptor` | `DelegatingHandler` qui propage `hba-correlation-id` |
| `DeadlineInterceptor` | Délai d'expiration de 5 s sur le `HttpClient` |

**Le report du jeton est le plus critique.** L'ADR 0007 exige que toute
autorisation soit vérifiée côté service. Un service qui ne reçoit plus le jeton
de l'appelant ne peut plus rien vérifier — et échouerait ouvert ou fermé selon
le code, sans que personne ne s'en aperçoive avant longtemps.

## Correspondance des erreurs

| Exception | gRPC (avant) | HTTP (après) |
|---|---|---|
| `NotFoundException` | `NotFound` | 404 |
| `ForbiddenException` | `PermissionDenied` | 403 |
| `InvalidStateTransitionException` | `FailedPrecondition` | 409 |
| `DomainException` code `VALIDATION_FAILED` | `InvalidArgument` | 400 |
| `DomainException` (autre) | `FailedPrecondition` | 422 |

Le code métier stable (`hba-error-code` dans les trailers gRPC) passe dans le
corps de la réponse d'erreur, sous `code`. Les BFF continuent de le remonter aux
applications sans interpréter un message en français.

## Conséquences

- **Un appel interne se rejoue avec `curl`.** C'est tout l'objet du chantier.
- **Un port de moins par service** : le point d'entrée Kestrel `Grpc` en HTTP/2
  disparaît, et avec lui la contrainte h2c qui interdisait `Http1AndHttp2` en
  clair.
- **Deux langages de schéma au lieu d'un**, ce que l'ADR 0002 avait justement
  voulu éviter. C'est le prix assumé. La frontière est nette et facile à
  retenir : protobuf pour ce qui transite sur Kafka, C# et JSON pour ce qui
  s'appelle en direct.
- **Perte du typage fort de bout en bout** sur le synchrone. Un champ renommé
  d'un côté ne casse plus la compilation de l'autre. À compenser par des tests
  de contrat.
- Les BFF traduisaient déjà gRPC en REST/JSON : cette couche de traduction
  s'allège au lieu de disparaître.
