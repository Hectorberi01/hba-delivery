# 0017 — FedaPay comme agrégateur de paiement, en page hébergée

Statut : **acceptée** — septembre 2026
Tranche le point 12 de [points-a-trancher.md](../architecture/points-a-trancher.md).

## Contexte

Le point 12 posait le choix du fournisseur : un agrégateur, ou une intégration
MTN MoMo directe. Le service Payment était une coquille — `PaymentGrpcService`
sans aucune méthode — et `CreateDeliveryHandler` appelait `CreateIntentAsync`
dès que le donneur d'ordre est un client. Aucune course n'était donc payable,
et aucune ne pouvait entrer en dispatch.

Le contrat `payment_service.proto` nommait déjà FedaPay dans ses commentaires,
et `ActorKind.PaymentProvider` porte la mention « FedaPay, via webhook
vérifié ». La décision formalise ce que le code supposait.

## Décision

**L'agrégateur est FedaPay** (option A du point 12).

**Le parcours est la page de paiement hébergée**, en deux appels : création
d'une transaction, puis génération de son jeton, qui rend l'adresse où envoyer
le payeur.

L'alternative — pousser directement une demande sur le téléphone — oblige à
désigner l'opérateur (MTN, Moov, Celtiis) transaction par transaction. Ni
l'application cliente ni le client lui-même ne fournissent cette information de
façon fiable. La page hébergée couvre les trois opérateurs et la carte avec une
seule intégration.

**Le webhook signé est la seule source de vérité**, et il ne porte qu'une
référence, jamais un verdict. Le service relit l'état de la transaction par
l'API du fournisseur avant de conclure. La signature prouve l'émetteur, pas la
fraîcheur du contenu : deux notifications peuvent se croiser, et le fournisseur
recommande lui-même la relecture.

Le retour du client sur l'écran « merci » n'est branché sur rien. Un navigateur
fermé ne prouve aucun paiement.

## Ce que la décision fixe dans le code

| Point | Choix |
|---|---|
| Frontière | `IPaymentProvider` dans la couche Application. Aucun autre service ne connaît le fournisseur. |
| Montants | Entiers de francs CFA, transmis tels quels — FedaPay attend le même entier (ADR 0006). |
| Signature | HMAC-SHA-256 sur `<horodatage>.<corps brut>`, en-tête `t=…,s=…`, tolérance 300 s. Format repris des bibliothèques officielles : il n'est pas publié dans la documentation. |
| Idempotence | Le rejeu d'une notification ne change aucun état et ne republie aucun événement. Le fournisseur rejoue jusqu'à neuf fois. |
| Concurrence | Jeton `xmin` sur l'intention : deux notifications simultanées ne peuvent pas toutes deux encaisser. |
| Ordre des faits | Un échec peut devenir un succès — le client réessaie sur la même page. L'inverse est refusé : un paiement encaissé ne redevient jamais en échec. |
| Démarrage | Hors développement, une clé absente empêche le démarrage. Un rapport au démarrage nomme le compte visé, bac à sable ou production. |

## Ce que la décision ne tranche pas

- **Le remboursement.** `RefundPayment` renvoie `UNIMPLEMENTED`. Qui déclenche,
  sous quel délai, avec ou sans frais, et comment la part du livreur est
  reprise quand la course a déjà été faite : rien de cela n'est décrit par le
  référentiel acteurs. Le remboursement automatique sur `NO_DRIVER_FOUND` que
  mentionne le contrat attend donc cette décision — et le service Dispatch.
- **Le règlement du commerçant donneur d'ordre** (point 1) et **celui du
  partenaire** (point 2). Ils restent ouverts, et l'ADR 0013 reste *proposée*.
  Le constat technique qui les bloque est confirmé par cette intégration :
  chez FedaPay comme chez MTN MoMo, le payeur valide sur son téléphone avec son
  code PIN. Il n'existe ni prélèvement serveur, ni mandat pré-autorisé — un
  partenaire, qui est un système, n'a personne devant un téléphone.

## Conséquences

- **Une commission par transaction**, celle de l'agrégateur. C'est le prix de
  la couverture multi-opérateurs et d'une seule intégration à tenir.
- **Un secret de plus à gérer** : la clé secrète et le secret de webhook sont
  distincts, et les deux existent en bac à sable et en production.
- **Le webhook doit être joignable depuis l'extérieur.** Il est publié par le
  gateway sur `/webhooks/fedapay` et relayé vers le port HTTP du service.
- **Changer d'agrégateur reste une implémentation à écrire**, pas une
  modification du domaine. C'est exactement ce que `IPaymentProvider` achète.
- **Une transaction peut rester orpheline** si l'enregistrement échoue après
  l'appel sortant. C'est le moindre mal : l'ordre inverse laisserait un client
  devant un écran sans adresse de paiement, alors qu'une transaction orpheline
  n'est jamais payée.
