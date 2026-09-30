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

### Ce verrou n'a rien verrouillé pendant une journée — corrigé le 30 septembre 2026

Le `SELECT … FOR UPDATE` était bien là, et son propre commentaire énonçait la
condition : « sans transaction ouverte, ce verrou ne vaut rien ». **Aucun
appelant n'en ouvrait.** `BeginTransaction` n'apparaissait qu'une seule fois dans
tout le dépôt : dans le test d'intégration du verrou, qui l'ouvrait lui-même. Le
test prouvait donc que PostgreSQL sérialise quand on entoure le `FOR UPDATE`
d'une transaction — ce qui est vrai de PostgreSQL et n'engageait en rien le
service. Une garantie testée, documentée, et absente à l'exécution : le pire cas
de figure, parce que le vert inspire confiance.

Ce qui sauvait l'invariant en attendant n'était pas le verrou mais le jeton
`xmin` — celui-là même dont le paragraphe ci-dessus dit qu'il ne convient pas.
La seconde écriture échouait en conflit de concurrence, donc en **erreur
interne** et non en refus métier : le donneur d'ordre lisait « une erreur est
survenue » là où le service croyait lui répondre `INSUFFICIENT_BALANCE`.

**Trois changements, dont deux qui empêchent de reperdre la garantie :**

1. `ITransactionRunner` (port) et `EfTransactionRunner` (infrastructure) : les
   trois écritures qui prennent le verrou — `Debit`, `Credit`, `ReverseDebit` —
   ouvrent la transaction **avant** la lecture verrouillée et la valident après
   la dernière écriture. Le runner est réentrant : appelé dans une transaction
   déjà ouverte, il la réutilise, parce que PostgreSQL n'imbrique pas les
   transactions et qu'un second `BEGIN` ferait perdre ce qui était verrouillé.
2. **`GetForUpdateAsync` lève** si aucune transaction n'est ouverte. Un
   avertissement en commentaire ne protège rien ; cette exception fait échouer le
   premier appel fautif, au premier essai, en nommant ce qu'il faut faire.
3. Le test du verrou passe maintenant par `EfTransactionRunner`, donc par le même
   chemin que les gestionnaires. S'il cesse de passer, c'est le service qui a
   changé, pas le montage du test. Un second test vérifie que le verrou hors
   transaction est bien refusé.

### Et la requête elle-même ne passait pas — trouvé au premier vrai passage

`SELECT * FROM billing.billing_accounts … FOR UPDATE` : **`xmin` est une colonne
système de PostgreSQL, et `SELECT *` ne rend jamais les colonnes système.** Or
l'agrégat la mappe comme jeton de concurrence, et EF Core exige que toutes les
colonnes mappées soient présentes dans le résultat d'un `FromSql`. Chaque appel
levait donc « The required column 'xmin' was not present in the results of a
'FromSql' operation ».

**Le débit n'avait jamais fonctionné contre une vraie base.** La requête est
maintenant `SELECT *, xmin`. Rien ne pouvait le dire plus tôt : cela compile,
cela passe la revue, et le premier passage des tests d'intégration l'a trouvé en
vingt-neuf millisecondes — les deux débits concurrents échouaient tous les deux
sans même atteindre l'attente qui les fait se chevaucher. C'est exactement ce que
ce projet de tests existe pour attraper, et c'est un argument de plus contre le
fournisseur en mémoire, qui n'a pas de colonne système à oublier.

L'Outbox fait un `SELECT *` et s'en porte bien : son message n'a pas de jeton de
concurrence. C'est la combinaison `FromSql` + `xmin` qui casse, pas `FromSql`.

**À savoir si Billing active un jour `EnableRetryOnFailure`** : une transaction
ouverte à la main est incompatible avec une stratégie de reprise ; il faudrait
passer par `Database.CreateExecutionStrategy()`. Aucun service du dépôt ne
l'active aujourd'hui.

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

## La porte de la finance — ouverte le 30 septembre 2026

**Rien ne permettait d'ouvrir un compte ni d'y porter une recharge.**
`OpenAccount` et `Credit` n'existaient que sur le gRPC interne de Billing, que le
proxy ne route pas : la décision du 29 septembre — « recharge à la main par
`finance`, pour commencer » — n'avait littéralement pas de porte. La première
course de tout partenaire échouait en « compte de facturation introuvable », et
son devis était déjà consommé.

Trois routes, sous `/api/admin/v1/billing/accounts`, groupe back-office et
décision côté service (`BillingAccess` n'admet que `finance` et `admin`) :

| Route | Ce qu'elle fait |
|---|---|
| `GET /{ownerType}/{ownerId}` | solde, disponible, régime, seuil d'alerte |
| `POST` | ouvre un compte, en prépayé, à zéro |
| `POST /{ownerType}/{ownerId}/credit` | porte au compte un virement constaté |

Et un écran de console, `Facturation`, qui fait les trois.

**La clé d'idempotence de la recharge est dérivée de la référence du virement** —
`topup:<référence>`. C'est la seule protection réelle contre une double recharge :
la demander à l'opérateur reviendrait à lui faire inventer un identifiant, qu'il
composerait différemment à chaque essai, et deux clics sur « recharger »
créeraient deux recharges. Dérivée, la seconde tentative retrouve le mouvement
déjà écrit ; l'index unique étant global, un même virement ne peut pas non plus
être porté à deux comptes. Ce qu'elle ne protège pas : **une référence mal
recopiée est un autre virement** aux yeux du système — d'où la double saisie à
l'écran.

**Ce qui est délibérément absent de cette porte.**

- **Aucun débit.** Un débit est la conséquence d'une course, jamais un geste
  d'exploitation, et `BillingAccess` le refuse au back-office faute de cas
  d'usage. Un écran ne contourne pas cette règle.
- **Aucune ouverture automatique au premier débit.** Ce serait commode — cela
  supprimerait le 404 de la première course — mais un compte de facturation
  accompagne un contrat commercial. En ouvrir un parce qu'une course est arrivée
  reviendrait à faire crédit à quelqu'un que personne n'a accepté. En revanche,
  **Delivery vérifie maintenant l'existence du compte AVANT de consommer le
  devis** : le refus est propre, le devis survit, et le message dit qu'il faut
  demander l'ouverture au service financier.
- **Aucun montant minimum de recharge.** C'est la question ouverte 6 ci-dessous,
  et rien ne l'a tranchée : le service accepte donc tout montant strictement
  positif.
- **Aucun relevé des mouvements.** `GetAccount` rend un solde, pas une histoire.
  Pour le pilote, la clé dérivée suffit à empêcher le double comptage ; un relevé
  demandera un `ListMovements` au contrat, et c'est la suite naturelle de cet
  écran.
- **Aucune recherche de titulaire.** Il n'existe pas d'annuaire des commerçants
  dans la console, et Billing ne connaît que des identifiants : l'opérateur saisit
  le couple, tel qu'il le lit dans la fiche du partenaire. Inventer une recherche
  supposerait de décider où la chercher.

## Autorisation — implémentée le 29 septembre 2026

Vérifiée côté service, comme partout ailleurs (ADR 0007), dans
`BillingAccess` — appelée par chaque gestionnaire, jamais par la porte gRPC.

**CE SERVICE N'EST PAS PROTÉGÉ PAR LE FAIT DE N'ÊTRE APPELÉ QUE PAR DELIVERY.**
Delivery l'appelle à travers `TokenForwardingInterceptor`, qui reporte **le jeton
de l'utilisateur final** : c'est un porteur de jeton commerçant ou partenaire qui
arrive ici, pas un service de confiance. Avec le seul `[Authorize]` de classe
qu'il avait au départ, n'importe quel jeton valide pouvait débiter le compte de
n'importe qui, et surtout **créditer le sien** — c'est-à-dire se donner du solde
sans payer.

| Opération | Qui |
|---|---|
| `Debit` | le titulaire, et lui seul : `merchant_owner` sur son `merchant_id`, `partner` sur son `partner_id` |
| `Credit` | `finance` et `admin` uniquement |
| `OpenAccount` | `finance` et `admin` uniquement |
| `ReverseDebit` | le **système** (jeton de service), `finance`, `admin` — jamais le titulaire |
| `GetAccount` | le titulaire, et tout le back-office |

Trois niveaux, et la différence est celle de l'argent. **Débiter** appauvrit le
titulaire : il peut le faire pour lui-même. **Créditer** l'enrichit, et le
montant est choisi : personne ne le fait pour soi — `ops` et `support` non plus,
constater un paiement reçu étant le métier de `finance`. **Lire** ne change
rien : le titulaire et le back-office le font librement.

`ReverseDebit` a été ouvert au titulaire le 29 septembre, **sur un raisonnement
faux, et refermé le 30**.

L'argument était : « aucun montant n'y est choisi, il est lu sur le débit annulé,
donc le pire qu'un titulaire obtienne est de récupérer son propre argent ». La
première moitié est vraie et sans intérêt. La seconde est fausse : **la clé du
débit est l'identifiant de la course**, et Delivery le rend au donneur d'ordre
dans la réponse de création. Un partenaire appelait donc `ReverseDebit` avec
l'identifiant de sa course **en cours** — remboursé, et il gardait sa livraison.
Le pire n'était pas son argent, c'était une course gratuite.

**Billing ne peut pas distinguer les deux cas, et n'a pas à le pouvoir.** Il ne
connaît pas les courses : un débit sans contrepartie et un débit dont la
contrepartie existe lui sont identiques. Seul l'appelant sait lequel est lequel,
donc la règle porte sur l'appelant et non sur le montant.

**Delivery force son jeton de service sur ce seul appel.** Son débit part avec le
jeton du donneur d'ordre — c'est ce qui permet à Billing de vérifier lui-même que
celui qui débite est le titulaire. Son annulation part avec le jeton de service,
obtenu par `GetTokenAsync` et non `AuthorizationAsync` : cette dernière cède
délibérément la place au jeton de l'utilisateur quand l'appel descend d'une
requête, et c'est précisément ce qu'il ne faut pas ici.
`TokenForwardingInterceptor` n'ajoute l'en-tête que s'il est absent, donc
l'en-tête posé à la main gagne.

Hors périmètre, la lecture répond **introuvable** et non **interdit** :
distinguer les deux permettrait d'énumérer les commerçants qui ont un compte.

Un `merchant_staff` n'est admis nulle part : il ne crée pas de livraison, donc
il n'a rien à débiter, et le solde est une donnée financière du dirigeant.

**Ce que l'autorisation ne permet plus, et qui manque :** réduire un solde
trop crédité. `BillingAccount.Adjust` admet les montants négatifs mais n'est pas
exposé en gRPC, et `Debit` est fermé au back-office par moindre privilège. Le
manque est assumé, pas caché.

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
- Contrats : `billing/v1` existe — `billing_service.proto` (`Debit`, `Credit`,
  `ReverseDebit`, `GetAccount`, `OpenAccount`) et `billing_events.proto`
  (`LowBalanceReached`, sur `hba.billing.events.v1`). Reste `delivery/v1` à
  compléter des codes d'erreur `INSUFFICIENT_BALANCE` et `ACCOUNT_SUSPENDED`.

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
