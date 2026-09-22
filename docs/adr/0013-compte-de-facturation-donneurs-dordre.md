# 0013 — Un compte de facturation par donneur d'ordre, prépayé ou postpayé

Statut : **proposée** — septembre 2026. En attente de décision.

Tranche les points 1 et 2 de `architecture/points-a-trancher.md`.

## Contexte

Le référentiel des acteurs laisse ouvert le règlement du commerçant donneur
d'ordre, et ne dit rien du paiement d'une course créée par un partenaire.

Une contrainte technique ferme la plupart des issues. Chez FedaPay comme chez
MTN MoMo, un paiement mobile money se termine par une invite sur le téléphone
du payeur, qui valide en saisissant son code PIN. Il n'existe ni débit
déclenché côté serveur, ni mandat pré-autorisé, ni prélèvement récurrent.

Un commerçant qui fait trente courses par jour saisirait donc trente fois son
code. C'est supportable une semaine, pas un trimestre.

Un **partenaire**, lui, est un système. HBA Food ne dispose d'aucun humain
placé devant un téléphone au moment où sa commande est prête. Une course créée
par un partenaire resterait indéfiniment en `PENDING_PAYMENT` et n'entrerait
jamais en dispatch. Ce n'est pas une gêne : c'est un cas d'usage impossible.

Deux réponses existent, et elles ne s'adressent pas aux mêmes entreprises. Le
**prépayé** convient à un petit commerçant : il paie ce qu'il consomme, HBA ne
lui fait aucun crédit. Le **postpayé** convient à un partenaire installé ou à
une enseigne qui refusera d'immobiliser de la trésorerie et exigera une facture
mensuelle pour sa propre comptabilité.

## Décision

Le **client** particulier continue de payer course par course : une course, un
paiement, un code PIN. C'est ce qu'il attend, et son volume est faible.

Chaque donneur d'ordre professionnel — `merchant_owner` et `partner` — possède
un **compte de facturation** portant un **mode de règlement** : `prepaid` ou
`postpaid`.

Les deux modes partagent **le même journal de mouvements et la même règle de
débit**. Ils ne diffèrent que par un paramètre :

> **Le prépayé est un postpayé dont le plafond de crédit vaut zéro.**

À la création d'une course, Billing vérifie un seul invariant :

```
solde + plafond_de_credit >= montant de la course
```

En prépayé le plafond est nul, le solde doit donc rester positif. En postpayé
le solde descend sous zéro : ce solde négatif **est** l'encours du mois.

1. **Le prépayé est le mode par défaut.** Tout compte s'ouvre en prépayé, avec
   un plafond nul. Aucun crédit n'est accordé implicitement.

2. **Le postpayé est accordé, jamais demandé.** Seul `finance` change le mode
   et fixe le plafond, compte par compte. C'est une décision commerciale
   assumée, pas une case à cocher dans un formulaire d'inscription.

3. **En prépayé, la recharge est un paiement mobile money ordinaire.** Une
   invite, un code PIN, une fois par semaine plutôt que trente fois par jour.

4. **En postpayé, le mois est clôturé et une facture est émise.** Son paiement
   crédite le compte et ramène l'encours à zéro.

5. **La création d'une course débite le compte**, dans les deux modes. C'est
   une écriture comptable : aucun appel à un opérateur, aucun PIN, aucune
   latence réseau dans le chemin critique.

6. **Plafond atteint : la course est refusée**, avec `INSUFFICIENT_BALANCE`, le
   solde et le montant manquant. Le message est le même dans les deux modes ;
   seule la façon de le résoudre diffère — recharger, ou régler sa facture.

7. **Le solde n'est pas retirable.** Il ne sert qu'à payer des courses HBA
   Delivery. Un solde reversable en mobile money serait de la détention de
   fonds pour compte de tiers, ce qui relève d'un tout autre régime.

8. **L'argent vit dans un service `Billing` propre à HBA Delivery.** Delivery
   possède la course et le coursier ; Billing possède le compte, les
   mouvements, les factures. Aucun des deux ne lit la base de l'autre.

## Conséquences

- **Le point 2 se résout sans rien inventer.** Un partenaire n'a plus besoin
  d'un humain, et la livraison quitte `PENDING_PAYMENT` à l'instant où elle est
  créée, parce qu'elle est déjà réglée ou imputée.

- **Un seul modèle, pas deux.** Le journal de mouvements, le verrou de débit,
  l'idempotence et le remboursement sont écrits une fois. Un mode de plus ne
  serait qu'un plafond de plus.

- **Le postpayé rétablit le risque que le prépayé supprimait**, et c'est
  délibéré. Un encours est un crédit : il se plafonne, il se relance, il ne se
  recouvre pas toujours. C'est la raison pour laquelle il est accordé et non
  demandé — et pour laquelle le défaut reste le prépayé.

- **Une facture est un document légal, pas un PDF.** Au Bénin, une entreprise
  assujettie à la TVA délivre des factures normalisées via MECeF ou e-MECeF, et
  un système de facturation maison doit être agréé par la DGI avant d'émettre.
  Les sanctions sont lourdes. Ce point est ouvert et traité à part : il
  conditionne le postpayé, pas le prépayé.

- **La commission ne baisse pas.** Une commission proportionnelle prélève
  autant sur une recharge de 50 000 F que sur trente-trois courses à 1 500 F.
  Ce qui baisse, c'est le nombre d'appels d'API, de webhooks, de lignes de
  rapprochement et de modes de panne.

- **Le service Payment rétrécit.** Il ne lui reste qu'un seul flux : créer une
  intention d'encaissement — recharge ou règlement de facture — et encaisser un
  webhook.

- **Le choix de l'encaisseur change de critère.** Le nombre de transactions
  s'effondrant, l'écart de commission pèse peu. Reste la couverture : une
  intégration MTN directe exclut les clients Moov et Celtiis, et chaque
  exclusion est un donneur d'ordre qui ne peut pas payer. Point ouvert.

- **Un solde prépayé est une dette, pas un produit.** Tant que la course n'est
  pas livrée, l'argent reçu appartient encore au titulaire. Cela se traduit en
  comptabilité, et impose de savoir rembourser un solde à la fermeture d'un
  compte.

- **C'est un service de plus à écrire avant le pilote**, et le postpayé en
  double la surface. Le coût est réel et il est assumé : sans Billing, aucun
  partenaire ne peut fonctionner.

- **L'annulation recrédite le compte** — ou donne lieu à un avoir si la facture
  est déjà émise, car une facture émise ne se modifie pas. Cela couple cette
  décision au point 3, toujours ouvert.

- **Une question juridique reste posée.** L'instruction BCEAO n°001-01-2024
  encadre les services de paiement dans l'UMOA. Un solde non retirable,
  utilisable uniquement pour acheter les prestations de son émetteur, est un
  cas bien moins exposé qu'une cagnotte reversable — mais la frontière doit
  être confirmée par un juriste béninois ou par la BCEAO avant d'ouvrir le
  premier compte. Ce document ne tranche pas ce point et n'en a pas la
  compétence.

## Ce qui a été écarté

- **Paiement à la course pour le commerçant** (point 1, option A) : trente
  codes PIN par jour.
- **Partenaire payant course par course** (point 2, option B) : impossible, le
  payeur doit valider sur un téléphone.
- **Crédit accordé au partenaire avec compensation ultérieure** (point 2,
  option A) : c'est le postpayé sans son cadre — ni plafond, ni facture, ni
  relance.
- **Le postpayé comme mode par défaut** : il accorde un crédit à un donneur
  d'ordre dont on ne sait rien encore.
- **Deux modèles séparés**, un module prépayé et un module de facturation : le
  débit, l'idempotence et le remboursement auraient été écrits deux fois, et
  auraient divergé.

## Pourquoi un compte mono-usage et non un portefeuille générique

La tentation existe de modéliser un portefeuille universel — un agrégat
`Wallet` avec un type de propriétaire, des sous-comptes typés, ouvert au
client, au livreur et à la plateforme autant qu'au commerçant.

Ce n'est pas le même objet. Le compte décrit ici est mono-usage et non
retirable : il ne sert qu'à acheter des courses HBA Delivery, il n'appartient
qu'aux donneurs d'ordre professionnels, et c'est précisément ce qui le tient à
distance du régime des services de paiement. Un portefeuille dont le solde peut
ressortir en mobile money est un autre métier, avec un autre cycle de vie et un
autre profil réglementaire.

Les réunir dans un même agrégat serait une mutualisation de façade, payée par
un modèle qui doit satisfaire deux contraintes opposées.

La règle « Delivery possède la course, l'argent est ailleurs » est respectée :
l'ailleurs s'appelle `Billing`, et il vit dans ce dépôt.
