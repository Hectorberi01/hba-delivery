# La facturation des donneurs d'ordre

Conception du service `Billing`, décidé par l'ADR 0013. Ce document décrit le
modèle et les flux ; en cas d'écart avec le code, c'est le code qui a raison.

Rien de ceci n'est implémenté. L'ADR 0013 est en attente de décision.

## Portée

Billing détient les comptes de facturation des donneurs d'ordre professionnels
(`merchant_owner`, `partner`), leurs mouvements, leur solde et leurs factures.
Il ne connaît ni les courses, ni les livreurs, ni les clients particuliers.

Ce qu'il ne fait pas : encaisser (c'est Payment), tarifer (c'est Pricing),
rémunérer un livreur (hors périmètre), détenir un solde retirable.

## L'idée centrale

Un compte porte un **mode de règlement** et un **plafond de crédit**. Le débit
obéit à une règle unique, valable dans les deux modes :

```
solde + plafond_de_credit >= montant de la course
```

En **prépayé**, le plafond vaut zéro : le solde doit rester positif, le
titulaire recharge à l'avance. En **postpayé**, le solde descend sous zéro, et
ce solde négatif **est** l'encours du mois, plafonné.

Il n'y a donc pas deux modules, ni deux journaux, ni deux règles de débit. Il y
a un modèle et un paramètre.

| | Prépayé | Postpayé |
|---|---|---|
| Plafond de crédit | zéro | fixé par `finance` |
| Alimentation | recharge, à l'initiative du titulaire | règlement de la facture |
| Solde | toujours positif | négatif pendant le mois |
| Clôture | aucune | mensuelle, facture émise |
| Remboursement | mouvement `refund` | `refund` avant clôture, avoir après |
| Risque d'impayé | nul | réel |
| Attribution | par défaut à l'ouverture | accordé par `finance` |

## Modèle

Trois tables, dans le schéma `billing`.

`billing_accounts` — un compte par donneur d'ordre.

| Colonne | Rôle |
|---|---|
| `id` | identifiant du compte |
| `owner_type` | `merchant` ou `partner` |
| `owner_id` | identifiant chez Directory ou Identity |
| `settlement_mode` | `prepaid` ou `postpaid` |
| `credit_limit_xof` | zéro en prépayé |
| `balance_xof` | solde courant, entier de francs, négatif = encours |
| `low_balance_threshold_xof` | seuil d'alerte |
| `status` | `active`, `suspended` |
| `xmin` | colonne système, jeton de concurrence |

`account_movements` — la vérité comptable, en ajout seulement.

| Colonne | Rôle |
|---|---|
| `id` | identifiant du mouvement |
| `account_id` | compte concerné |
| `kind` | `topup`, `debit`, `refund`, `invoice_payment`, `adjustment` |
| `amount_xof` | montant signé : positif au crédit, négatif au débit |
| `balance_after_xof` | solde après ce mouvement |
| `reference` | intention de paiement, course, ou facture |
| `invoice_id` | facture sur laquelle ce mouvement a été imputé, sinon nul |
| `idempotency_key` | unique — c'est elle qui interdit le double débit |
| `created_at` | horodatage |

`invoices` — postpayé seulement.

| Colonne | Rôle |
|---|---|
| `id` | identifiant |
| `number` | numéro séquentiel, sans trou |
| `account_id` | compte facturé |
| `period_start`, `period_end` | période couverte |
| `amount_xof` | total des débits imputés |
| `status` | `issued`, `paid`, `overdue`, `cancelled` |
| `due_at` | échéance |
| `normalisation` | références de la facture normalisée, quand elle existe |

Le solde porté par le compte est un **cache**. La vérité est la somme des
mouvements. Un mouvement n'est jamais modifié ni supprimé : une erreur se
corrige par un `adjustment` de sens inverse, qui laisse une trace. Un contrôle
périodique recalcule la somme et signale tout écart — sans lui, un compte est
invérifiable.

## Le débit concurrent

C'est le point où ce genre de module se casse. Deux courses créées à la même
seconde sur le même compte, chacune lisant le solde puis le réécrivant : les
deux passent, et le plafond est dépassé.

Le débit s'exécute dans **une seule transaction**, ouverte par un verrou
pessimiste sur la ligne du compte :

```sql
SELECT balance_xof, credit_limit_xof
  FROM billing.billing_accounts
 WHERE id = @accountId
   FOR UPDATE;
```

`FOR UPDATE` — et non `FOR UPDATE SKIP LOCKED` comme dans l'Outbox : ici on
veut attendre son tour, pas passer au suivant. La sérialisation est **par
compte** : deux donneurs d'ordre différents ne se bloquent jamais.

Puis, dans la même transaction : contrôle de l'invariant, insertion du
mouvement, mise à jour du solde. Si l'`idempotency_key` existe déjà, la
contrainte unique rejette l'insertion et le service renvoie le mouvement
existant — un rejeu ne débite pas deux fois.

Le verrou optimiste `xmin` employé ailleurs dans la base ne convient pas ici :
sur le compte d'un donneur d'ordre chargé, les tentatives échoueraient et se
répéteraient en cascade. Un verrou de ligne tenu quelques millisecondes coûte
moins cher.

## Recharge — prépayé

```
  Commerçant                Billing              Payment            Opérateur
      │                        │                    │                   │
      │  demande de recharge   │                    │                   │
      ├───────────────────────>│                    │                   │
      │                        │  intention         │                   │
      │                        ├───────────────────>│  requestToPay     │
      │                        │                    ├──────────────────>│
      │                        │                    │                   │
      │  <────────── invite sur le téléphone, saisie du code PIN ───────┤
      │                        │                    │                   │
      │                        │  PaymentSucceeded  │  <──── webhook ───┤
      │                        │  <──── Kafka ──────┤                   │
      │                        │                    │                   │
      │                    mouvement `topup`        │                   │
```

Le crédit n'est **jamais** déclenché par la réponse de l'application : seul le
webhook de l'opérateur, vérifié par Payment, fait foi. Consommé via l'Inbox
(ADR 0003), donc idempotent : un webhook rejoué ne crédite pas deux fois.

## Création d'une course — les deux modes

Delivery appelle Billing **en synchrone**, par gRPC, avant de créer la course :

```
  Donneur d'ordre       Delivery              Billing
      │                    │                     │
      │  CreateDelivery    │                     │
      ├───────────────────>│  Debit(compte,      │
      │                    │        montant,     │
      │                    │        clé = id de  │
      │                    │        la course)   │
      │                    ├────────────────────>│  verrou,
      │                    │                     │  solde + plafond ?
      │                    │  <───── OK ─────────┤  mouvement, solde
      │                    │                     │
      │                    │  course créée directement en PAID
      │  <──── 201 ────────┤                     │
```

L'identifiant de la course est **tiré avant l'appel** : il sert de clé
d'idempotence côté Billing. Un rejeu de `CreateDelivery` avec la même clé
retrouve le même débit.

Le choix du synchrone est délibéré. La voie asynchrone — créer la course en
`PENDING_PAYMENT`, publier un événement, laisser Billing débiter — respecterait
mieux le découplage, mais un donneur d'ordre au plafond recevrait un
`201 Created` suivi d'un échec silencieux. Le refus doit être immédiat.

Le prix de ce choix : si le débit réussit et que la création de la course
échoue juste après, le débit est orphelin. Une tâche de balayage recherche les
débits dont la course n'existe pas au bout de quelques minutes et les annule
par un `refund`. C'est une réparation, pas une transaction distribuée — et elle
est vérifiable.

## Plafond atteint

La course est refusée avec `INSUFFICIENT_BALANCE`, le solde courant et le
montant manquant. Rien n'est créé, rien n'est débité. Le code est le même dans
les deux modes ; c'est la marche à suivre qui diffère, et l'application la
déduit du mode du compte : recharger, ou régler la facture en retard.

Sous le seuil d'alerte — ou à l'approche du plafond en postpayé — Billing
publie `LowBalanceReached`. L'événement n'est émis **qu'au franchissement** du
seuil, pas à chaque course sous le seuil : sinon le titulaire reçoit trente
messages par jour et n'en lit aucun.

## Clôture mensuelle — postpayé

À la fin de la période, une tâche planifiée, par compte :

1. rassemble les mouvements `debit` et `refund` de la période non encore
   imputés ;
2. crée une facture au statut `issued`, avec un numéro séquentiel **sans
   trou** ;
3. marque ces mouvements de l'`invoice_id` ;
4. publie `InvoiceIssued`, que Notification transforme en message au titulaire.

La numérotation sans trou est une contrainte réelle, pas une élégance : une
séquence PostgreSQL saute des numéros en cas d'annulation de transaction. Le
numéro est donc attribué par un compteur dédié, pris sous verrou dans la même
transaction que la facture.

Le solde n'est pas remis à zéro par l'émission. Il l'est par le **paiement** :
un mouvement `invoice_payment`, déclenché par le webhook, comme une recharge.

Une facture impayée à l'échéance passe `overdue`. Au-delà d'un délai à définir,
le compte passe `suspended` : les créations de course sont refusées avec
`ACCOUNT_SUSPENDED`, **mais les courses déjà en vol vont à leur terme**. On
n'abandonne pas un colis en chemin pour une facture en retard.

## Annulation et remboursement

Le montant remboursé dépend de la politique d'annulation, **point 3 toujours
ouvert**. Tant qu'il l'est, Billing rembourse l'intégralité : c'est le seul
comportement qu'on ne regrettera pas.

Le chemin, lui, dépend du moment :

- **prépayé, ou postpayé avant clôture** : mouvement `refund`, le solde remonte ;
- **postpayé après émission de la facture** : la facture n'est pas modifiée —
  une facture émise ne se modifie jamais. Un **avoir** est émis, et imputé sur
  la période suivante.

## Changement de mode

Un compte passe de `prepaid` à `postpaid`, ou l'inverse, **uniquement à solde
apuré** : rien à recouvrer, rien à rembourser. Le changement est tracé par un
mouvement `adjustment` de montant nul portant l'ancien et le nouveau mode, pour
que l'historique reste lisible.

Passer un compte en postpayé sans plafond explicite est refusé : un plafond nul
en postpayé est un compte qui ne peut rien créer.

## Rapprochement

Chaque jour, une tâche compare les mouvements `topup` et `invoice_payment` aux
transactions confirmées par l'encaisseur. Trois écarts, trois traitements :

- transaction confirmée sans mouvement : webhook perdu, à rejouer ;
- mouvement sans transaction : anomalie grave, alerte immédiate ;
- montants différents : alerte, aucune correction automatique.

Toute reprise passe par un mouvement `adjustment`. Rien n'est corrigé en
silence.

## Autorisation

Vérifiée côté service, comme partout ailleurs (ADR 0007). Un `merchant_owner`
ne voit que le compte de son commerce ; `merchant_staff` ne voit ni le solde ni
les factures, qui sont des données financières du dirigeant ; un `partner` ne
voit que le sien ; `finance` et `admin` voient tous les comptes, et `finance`
seul change le mode et le plafond ; `ops` et `support` voient les mouvements
sans pouvoir en créer.

## La facture normalisée

Au Bénin, une entreprise assujettie à la TVA délivre des **factures
normalisées** via MECeF ou e-MECeF, et un système de facturation maison doit
être **agréé par la DGI** avant de pouvoir émettre. Ce n'est pas un détail
d'implémentation : c'est ce qui décide si le postpayé est livrable.

Point 13 des points à trancher. Le prépayé n'en dépend pas pour fonctionner ;
le postpayé, si.

## À répercuter si la décision est validée

- `flux-livraison.md` : un donneur d'ordre professionnel ne passe pas par
  `PENDING_PAYMENT`, la course naît en `PAID`. Le schéma actuel ne décrit que
  le parcours du client particulier.
- `matrice-visibilite.md` : ajouter le solde, les mouvements et les factures.
- Contrats : un `billing/v1` à écrire, et `delivery/v1` à compléter des codes
  d'erreur `INSUFFICIENT_BALANCE` et `ACCOUNT_SUSPENDED`.

## Questions ouvertes

1. **Le régime BCEAO.** Un solde non retirable, mono-usage, sort-il du champ de
   l'instruction n°001-01-2024 ? À confirmer par un juriste béninois.
2. **La facture normalisée** : point 13.
3. **Qui obtient le postpayé**, sur quels critères, et avec quel plafond de
   départ.
4. **La période** : mois calendaire ou trente jours glissants, et à quelle
   heure la clôture s'exécute — Cotonou est à UTC+1 toute l'année.
5. **Le délai avant suspension** d'un compte en retard de paiement.
6. **Le montant minimum de recharge**, s'il y en a un.
7. **Le solde d'un compte fermé** : remboursé comment, dans quel délai.
8. **L'encaisseur** : agrégateur couvrant plusieurs opérateurs, ou intégration
   MTN directe qui exclut Moov et Celtiis.
