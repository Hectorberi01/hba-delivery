# 0008 — Un seul déployable par service

Statut : acceptée — septembre 2026

## Contexte

Un service a deux entrées : gRPC, synchrone, et Kafka, asynchrone. Les séparer
en deux processus — une API et un worker — est un schéma courant et permet de
dimensionner chacun séparément.

## Décision

Un seul hôte par service. Dans `Hba.<Service>.Api`, le dossier `Grpc/` porte
l'entrée synchrone et le dossier `Messaging/` les consommateurs Kafka, démarrés
comme services hébergés. Un seul Dockerfile, une seule image, un seul
déploiement.

## Conséquences

- Sept images à construire et à déployer au lieu de quatorze. Pour une équipe
  qui n'est pas encore une équipe, cela compte plus que le dimensionnement fin.
- Les consommateurs partagent le pool de connexions et la mémoire de l'API. Un
  consommateur qui s'emballe dégrade les appels gRPC du même service.
- La séparation reste possible plus tard sans toucher au domaine : les
  consommateurs ne dépendent que de `IDispatcher`. Le jour où Dispatch ou
  Notification le justifiera, le découpage sera mécanique.
