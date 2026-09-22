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
À tout moment après le paiement : FAILED (ops uniquement, motif obligatoire).
```

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

Un acteur système — moteur de dispatch, FedaPay, planificateur — apparaît dans
l'audit exactement comme un acteur humain : c'est le rôle de `Actor` dans le
domaine.

## Le parcours nominal, bout en bout

1. Le client demande un **devis** à Pricing. Le devis expire.
2. Il crée la livraison. Delivery **consomme** le devis et en fige le contenu,
   puis demande une intention de paiement à Payment.
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
   saisit. Sans code valide, aucune transition vers `DELIVERED`.

## Pourquoi les entrées asynchrones sont regroupées par topic

Le dossier `Messaging/` du service Delivery contient un consommateur par
**topic**, pas par type d'événement : `PaymentEventsConsumer` et
`DispatchEventsConsumer`. La clé de partition étant l'identifiant de livraison,
un seul consommateur par topic préserve l'ordre des événements d'une même
course ; plusieurs consommateurs sur le même topic le perdraient.
