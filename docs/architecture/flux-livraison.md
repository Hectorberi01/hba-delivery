# Machine à états d'une livraison

La table des transitions vit dans `DeliveryTransitions.cs`. Ce document
l'explique ; en cas d'écart, c'est le code qui a raison.

## Les états

```
                    ┌──────────────────┐
                    │ PENDING_PAYMENT  │  création
                    └────────┬─────────┘
            webhook FedaPay  │  échec / expiration
                 ┌───────────┴───────────┐
                 v                       v
            ┌─────────┐          ┌────────────────┐
            │  PAID   │          │ PAYMENT_FAILED │  terminal
            └────┬────┘          └────────────────┘
                 │ dispatch : première vague
                 v
        ┌──────────────────┐   vagues épuisées   ┌──────────────────┐
        │ SEARCHING_DRIVER ├────────────────────>│ NO_DRIVER_FOUND  │ terminal
        └────────┬─────────┘                     └──────────────────┘
                 │ offre acceptée
                 v
        ┌──────────────────┐ <──── réaffectation forcée (ops)
        │ DRIVER_ASSIGNED  │
        └────────┬─────────┘
                 │ livreur : arrivé
                 v
        ┌──────────────────┐
        │ DRIVER_AT_PICKUP │
        └────────┬─────────┘
                 │ livreur : collecté
                 v
        ┌──────────────────┐
        │    PICKED_UP     │
        └────────┬─────────┘
                 │ livreur : OTP valide
                 v
        ┌──────────────────┐
        │    DELIVERED     │ terminal
        └──────────────────┘

À tout moment avant la collecte : CANCELLED (donneur d'ordre ou ops).
À tout moment après le paiement : FAILED (ops, motif obligatoire).
Depuis DRIVER_ASSIGNED, DRIVER_AT_PICKUP ou PICKED_UP : FAILED (le livreur
affecté, par un incident — motif obligatoire).
```

### L'incident du livreur — tranché le 30 septembre 2026

Depuis `PICKED_UP`, la seule transition ouverte au livreur était `DELIVERED`.
Destinataire absent, adresse fausse, ou code bloqué après cinq essais : il gardait
le colis, restait `ON_MISSION` — donc sans pouvoir ni se mettre hors ligne ni
recevoir la moindre offre — jusqu'à ce qu'un `ops` clôture à sa place. Un cas par
jour immobilisait un livreur pour la journée.

Il déclare donc un incident : la course part en `FAILED` avec un motif, et
l'événement `DeliveryFailed` le libère comme toute fin de course. **Jamais
`DELIVERED`** : marquer une course comme remise reste hors de sa portée sans
l'OTP du destinataire, et c'est la seule chose qui distingue « le colis a été
remis » de « quelqu'un a cliqué ».

**Le motif est un texte libre**, et c'est volontaire : aucune liste d'incidents
n'est tranchée. L'application propose quatre formulations et un champ libre ;
graver la liste dans le contrat et dans la base avant qu'elle soit décidée la
rendrait très difficile à corriger. Voir points-a-trancher.

## PENDING_PAYMENT n'est pas une course, pour le client

Une course non payée existe en base — il faut bien retenir les adresses, le
destinataire et le colis entre la création et le webhook — mais elle n'est
**jamais rendue au client dans la liste de ses courses** : `DeliveryAccess`
retire PENDING_PAYMENT du périmètre `customer`, quel que soit le filtre demandé.
Le back-office, lui, continue de la voir : un paiement abandonné est un fait
commercial.

Le suivi d'une course précise reste lisible par son propriétaire — c'est l'écran
sur lequel il attend la confirmation de son paiement.

**Elle ne survit pas non plus.** `AbandonDesImpayees` (service Delivery) la passe
en PAYMENT_FAILED au bout de **quinze minutes**, soit exactement la durée de vie
d'un devis : au-delà, le prix figé n'engage plus personne. Le devis n'est pas
rendu, parce qu'à cet instant il a expiré de son côté ; le client qui reprend en
demande un nouveau, au prix du moment.

Le balayage ne touche que les courses portant une **intention de paiement**. Une
commande de PARTENAIRE naît dans le même statut et n'en reçoit aucune — son
règlement est un point non tranché — et reste ouverte tant que ce point n'est
pas tranché.

## Qui a le droit de provoquer quoi

| Transition | Acteur autorisé |
|---|---|
| `PENDING_PAYMENT → PAID` | `PaymentProvider` (webhook FedaPay vérifié) — et personne d'autre |
| `PENDING_PAYMENT → PAYMENT_FAILED` | `PaymentProvider`, `Scheduler` |
| `PAID → SEARCHING_DRIVER` | `Dispatch` |
| `SEARCHING_DRIVER → DRIVER_ASSIGNED` | `Dispatch` |
| `SEARCHING_DRIVER → NO_DRIVER_FOUND` | `Dispatch` |
| `DRIVER_ASSIGNED / DRIVER_AT_PICKUP → SEARCHING_DRIVER` | `Admin` (réaffectation) |
| `DRIVER_ASSIGNED → DRIVER_AT_PICKUP → PICKED_UP → DELIVERED` | `Driver` affecté |
| `* → CANCELLED` (avant collecte) | `Customer`, `Merchant`, `Partner`, `Admin`, `Scheduler` |
| `* → FAILED` | `Admin` |
| `DRIVER_ASSIGNED / DRIVER_AT_PICKUP / PICKED_UP → FAILED` | `Driver` affecté (incident) |

Un acteur système — moteur de dispatch, FedaPay, planificateur — apparaît dans
l'audit exactement comme un acteur humain : c'est le rôle de `Actor` dans le
domaine.

## La course B2B ne passe pas par PENDING_PAYMENT

Un commerçant ou un partenaire a un **compte** chez Billing. Delivery le débite
**avant** de créer la course, en synchrone :

```
  Donneur d'ordre       Delivery                Billing
      │  CreateDelivery    │                       │
      ├───────────────────>│  Debit(compte, prix,  │
      │                    │  cle = id de course)  │
      │                    ├──────────────────────>│  verrou de ligne,
      │                    │                       │  solde + plafond ?
      │                    │ <──── mouvement ──────┤  ecriture, solde
      │                    │                       │
      │                    │  course creee EN PAID
      │  <──── 201 ────────┤
```

La course **naît en `PAID`** : elle n'a jamais attendu de paiement, et la faire
naître `PENDING_PAYMENT` pour la faire avancer dans la milliseconde écrirait
dans l'audit un état qui n'a jamais existé.

Le fait de domaine est **`DeliverySettledByAccount`**, distinct de
`DeliveryConfirmed` : l'un dit « FedaPay a encaissé », l'autre « le compte du
commerçant a réglé ». Aucun argent n'a circulé ici. Sur le fil, les deux
deviennent le même message `hba.delivery.v1.DeliveryConfirmed` — Dispatch n'a
besoin que du point d'enlèvement, de la distance et du gain du livreur, et d'où
vient l'argent ne change rien à la façon de chercher un livreur.

**Le refus est immédiat.** Un donneur d'ordre au plafond reçoit
`INSUFFICIENT_BALANCE` et rien n'est créé. C'est la raison d'être de l'appel
synchrone : un `201 Created` suivi d'un échec silencieux serait pire.

**Le débit orphelin est compensé.** Si le débit réussit et que la création
échoue juste après, le gestionnaire rattrape son propre échec et appelle
`ReverseDebit`, qui rend exactement le montant du débit annulé — le montant n'est
pas choisi, il est lu sur l'écriture qu'on annule, et l'appel est rejouable parce
que la clé de l'annulation est dérivée de celle du débit. L'échec de la
compensation ne remplace pas l'échec d'origine : il est journalisé en erreur avec
le compte et la clé, et c'est la faute initiale qui remonte au donneur d'ordre.

**Ce qui reste découvert** : l'arrêt BRUTAL du processus entre le débit et la
compensation. Personne ne compense alors, et aucun des deux services ne peut le
retrouver seul — Billing voit un débit dont il ne sait pas s'il a une course en
face, Delivery n'a aucune trace d'un débit dont la course n'a jamais été écrite.
Il faut un rapprochement périodique ; il n'existe pas, et les options sont au
point 33 des points à trancher.

## Le parcours nominal, bout en bout

1. Le client demande un **devis** à Pricing. Le devis expire, et il retient le
   trajet qu'il a chiffré.
2. Il crée la livraison. Delivery **consomme** le devis et en fige le contenu,
   puis demande une intention de paiement à Payment. La course n'apparaît pas
   encore dans « mes courses », et elle sera abandonnée dans quinze minutes si
   rien ne la confirme. Rejouer la même clé d'idempotence rend la même livraison
   **et la même adresse de paiement** : reprendre un paiement interrompu ne crée
   jamais une seconde course.
3. Le client paie. FedaPay appelle Payment, qui vérifie la signature et publie
   `PaymentSucceeded`.
4. Delivery consomme l'événement, passe en `PAID` et publie
   `DeliveryConfirmed`. **C'est le seul déclencheur de la recherche de livreur.**
5. Dispatch consomme `DeliveryConfirmed`, cherche les livreurs proches auprès de
   Driver et envoie une première vague d'offres.
6. Un livreur accepte. Le verrou Redis désigne un seul gagnant ; Dispatch publie
   `OfferAccepted`. Delivery consomme et passe en `DRIVER_ASSIGNED`. C'est à ce
   moment, et pas avant, que le livreur voit l'adresse exacte de destination.
7. Le livreur signale son arrivée, puis la collecte.
8. À la remise, le destinataire lui **dicte l'OTP** reçu par SMS. Le livreur le
   saisit. Sans code valide, aucune transition vers `DELIVERED`, et **cinq codes
   faux verrouillent la course** : elle passe alors au support.

## Le prix appartient au trajet chiffré — corrigé le 30 septembre 2026

Pricing refuse de consommer un devis pour un autre trajet que celui qu'il a
chiffré. Delivery lui passe les deux points de la livraison en cours de création,
et l'agrégat `Quote` compare : au-delà de **250 m d'écart à l'un des deux bouts**,
le refus est `QUOTE_TRIP_MISMATCH` et le client doit redemander un prix.

**Ce que cela ferme.** Le devis portait ses points depuis toujours, et personne
ne les comparait : la livraison était créée avec les points de la commande. Il
suffisait de deviser 300 m puis de créer la course avec une remise à 20 km. L'ADR
0004 faisait ensuite son travail — le prix figé ne bouge plus —, le paiement était
encaissé au tarif court, et la part du livreur calculée dessus. Le livreur roulait
pour rien et HBA portait l'écart sur chaque course.

**Pourquoi le contrôle est chez Pricing.** Le devis est le seul à savoir ce qu'il
a chiffré. Demander à Delivery de vérifier le prix qu'il vient de consommer
reviendrait à lui demander de se surveiller lui-même.

**La tolérance ne se règle pas.** Elle est une constante du domaine, pas un
paramètre de configuration : 250 m à chaque bout ne déplacent la distance routée
que de 500 m, soit quelques dizaines de francs. Un réglage finirait desserré un
jour de mise en production difficile.

## Le verrou du code de remise — corrigé le 30 septembre 2026

Cinq codes faux verrouillent la remise. Ce n'était pas le cas jusqu'au
30 septembre 2026, et le défaut méritait d'être écrit ici.

L'agrégat incrémentait le compteur de tentatives puis **levait** `INVALID_OTP`.
Or le gestionnaire n'appelle `SaveChanges` qu'après une étape réussie, et le
`Dispatcher` n'ouvre aucune transaction : l'exception remontait, rien n'était
enregistré, et le compteur repartait de zéro à la requête suivante. `OTP_LOCKED`
était du code mort. Un code de six chiffres devenait forçable par le livreur
affecté — qui n'avait donc plus besoin du destinataire pour clore la course, donc
pour déclencher la facturation du donneur d'ordre et le crédit de sa propre part.

**Ce qui a changé.** `ConfirmDelivery` **rend** son issue au lieu de la lever ;
le gestionnaire enregistre, puis traduit le refus en `INVALID_OTP`. La clé
d'idempotence, elle, n'est mémorisée qu'au succès : le rejeu d'un code faux reste
un refus et consomme bien une tentative.

**La leçon vaut plus que la correction.** Le verrou était couvert par un test
vert. Il enchaînait cinq refus sur la même instance en mémoire, où rien n'est
rechargé : il vérifiait une propriété que le système n'avait pas. Le filet est
désormais un test d'intégration sur une vraie base — `make test-delivery-integration`
—, où chaque tentative passe par le gestionnaire et un contexte neuf.

## Pourquoi les entrées asynchrones sont regroupées par topic

Le dossier `Messaging/` du service Delivery contient un consommateur par
**topic**, pas par type d'événement : `PaymentEventsConsumer` et
`DispatchEventsConsumer`. La clé de partition étant l'identifiant de livraison,
un seul consommateur par topic préserve l'ordre des événements d'une même
course ; plusieurs consommateurs sur le même topic le perdraient.
