# 0007 — L'autorisation se vérifie côté service, jamais au BFF seul

Statut : acceptée — septembre 2026

## Contexte

Il aurait été tentant de laisser les BFF décider : ils connaissent l'utilisateur,
ils sont en façade, ils pourraient injecter un en-tête `X-User-Id` que les
services croiraient sur parole.

Le problème est que les services gRPC sont joignables depuis le réseau interne.
Un BFF compromis, ou simplement un appel mal écrit d'un autre service, suffirait
alors à lire les livraisons de n'importe qui.

## Décision

Le BFF **propage le JWT de l'utilisateur final** dans la métadonnée
`authorization` (`TokenForwardingInterceptor`). Le service le revalide via les
JWKS d'Identity et reconstruit lui-même l'identité de l'appelant
(`ICallerContext`). Aucune requête protobuf ne transporte l'identité de
l'appelant.

Le périmètre et la visibilité sont appliqués dans la couche Application du
service — `DeliveryAccess` et `DeliveryViewMapper` — et nulle part ailleurs.

## Conséquences

- Les BFF ne filtrent rien. Ils traduisent. S'ils filtraient aussi, il y aurait
  deux sources de vérité pour la même règle, et la moins fiable serait
  contournable.
- Chaque service paie la validation du jeton. Le coût est faible : les JWKS sont
  mises en cache par le middleware.
- Reste à traiter : les appels de service à service **sans** utilisateur final —
  un planificateur, par exemple. Ils demanderont un jeton de service, émis par
  Identity, avec des scopes propres. Ce n'est pas encore écrit.
