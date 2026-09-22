# Décisions d'architecture

Une décision par fichier, numérotée, jamais réécrite : une décision qu'on
remplace reçoit un nouveau numéro et l'ancienne passe en « Remplacée par ».

Format : contexte, décision, conséquences — y compris celles qui dérangent.

| N° | Décision | Statut |
|---|---|---|
| [0001](0001-microservices.md) | Microservices plutôt que monolithe modulaire | Acceptée |
| [0002](0002-grpc-et-kafka.md) | gRPC pour le synchrone, Kafka pour l'asynchrone | Acceptée |
| [0003](0003-outbox-inbox.md) | Outbox à l'émission, Inbox à la réception | Acceptée |
| [0004](0004-prix-fige.md) | Le prix est recopié dans la livraison, pas référencé | Acceptée |
| [0005](0005-otp-preuve-de-livraison.md) | L'OTP du destinataire est la preuve de livraison | Acceptée |
| [0006](0006-montants-entiers-xof.md) | Montants en entiers de francs CFA | Acceptée |
| [0007](0007-autorisation-cote-service.md) | Autorisation vérifiée côté service, jamais au BFF seul | Acceptée |
| [0008](0008-un-hote-par-service.md) | Un seul déployable par service, gRPC et Kafka dans le même hôte | Acceptée |
| [0009](0009-un-seul-service-directory.md) | Un seul service Directory, pas deux | Acceptée |
| [0010](0010-jetons-rs256-et-rotation.md) | Jetons RS256, rafraîchissement à usage unique | Acceptée |
| [0011](0011-otp-dans-redis.md) | Les codes SMS vivent dans Redis, pas en base | Acceptée |
| [0012](0012-routes-publiques-et-enumeration.md) | Une route publique ne doit rien dire de plus que nécessaire | Acceptée |
| [0013](0013-compte-de-facturation-donneurs-dordre.md) | Un compte de facturation par donneur d'ordre, prépayé ou postpayé | **Proposée** |
| [0014](0014-canal-otp-du-pilote.md) | Le pilote envoie les codes par email, sauf celui du destinataire | **Proposée** |
