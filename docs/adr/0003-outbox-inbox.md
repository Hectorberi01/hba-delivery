# 0003 — Outbox à l'émission, Inbox à la réception

Statut : acceptée — septembre 2026

## Contexte

Écrire en base puis publier sur Kafka, ce sont deux opérations. Entre les deux,
le processus peut mourir : la livraison est passée en `PAID` et personne n'est
prévenu. L'inverse est pire : l'événement part, la transaction échoue.

## Décision

- **Outbox.** Les messages sont insérés dans la table `outbox_messages` **dans
  la même transaction** que le changement métier. Un service de fond les publie
  ensuite, avec un backoff exponentiel. La lecture se fait en
  `FOR UPDATE SKIP LOCKED` : plusieurs instances peuvent vider la file sans se
  marcher dessus.
- **Inbox.** Chaque consommateur enregistre l'`EventId` traité dans
  `inbox_messages`, dont c'est la clé primaire. C'est la base qui arbitre les
  courses, pas une lecture préalable.

## Conséquences

- La livraison est « au moins une fois ». Les consommateurs doivent rester
  idempotents malgré l'Inbox : c'est pour cela que `ConfirmPayment` et
  `AssignDriver` sortent silencieusement quand l'état est déjà atteint.
- Le passage par la base ajoute une latence de l'ordre de la seconde entre le
  fait et sa publication. Acceptable pour ce domaine : rien n'y est temps réel
  au sens strict, pas même la position des livreurs, qui passe par Redis.
- Les deux tables vivent dans la base de chaque service ; les interfaces sont
  communes, les implémentations sont liées au DbContext local.
