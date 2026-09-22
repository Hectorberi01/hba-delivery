# 0002 — gRPC pour le synchrone, Kafka pour l'asynchrone

Statut : acceptée — septembre 2026

## Contexte

Deux besoins distincts. « Donne-moi ce devis maintenant, j'attends la réponse »
n'est pas « le paiement a réussi, que ceux que ça concerne réagissent ».

## Décision

- **gRPC** pour les appels où l'appelant attend une réponse et où l'échec doit
  remonter immédiatement.
- **Kafka** pour les faits accomplis, avec une enveloppe commune
  (`EventEnvelope`) et l'identifiant de l'agrégat comme clé de partition.
- Les contrats des deux vivent dans `contracts/`, en protobuf, et sont la
  **source de vérité** des deux côtés. Une PR peut modifier un `.proto` et ses
  deux côtés ; `buf` détecte les changements cassants en CI.

## Conséquences

- Un seul langage de schéma pour le synchrone et l'asynchrone, donc un seul
  endroit où regarder.
- Les applications Flutter et Next.js ne parlent pas gRPC : les BFF traduisent
  en REST/JSON. C'est du travail de traduction en plus, assumé.
- La clé de partition impose sa contrainte : l'ordre n'est garanti que **par
  agrégat**. Aucune logique ne doit supposer un ordre global.
