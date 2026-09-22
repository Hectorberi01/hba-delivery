# driver_app — application livreur (Flutter)

## Ce que l'application fait

S'inscrire et déposer ses documents KYC, passer en ligne ou hors ligne, recevoir
des offres, accepter ou refuser, signaler l'arrivée, confirmer la collecte,
confirmer la remise avec l'OTP, consulter ses gains.

Authentification : téléphone + OTP, JWT. Rôle `driver`, actif seulement après
validation du KYC.

## La contrainte qui structure tout : la connexion

L'application doit fonctionner avec une connexion instable. Concrètement :

- **File d'actions locale.** Une action se met en file, s'horodate côté client
  et se rejoue au retour du réseau.
- **Idempotency-Key sur chaque action mutante.** Le rejeu ne doit jamais
  produire deux effets. Le BFF transmet l'en-tête `Idempotency-Key` tel quel.
- **L'horodatage client est borné côté service** : jamais dans le futur, pas
  plus de 24 h dans le passé. Un téléphone mal réglé ne réécrit pas l'histoire
  d'une course.

## Ce à quoi il faut faire attention

- **L'OTP ne s'affiche jamais.** Le livreur le saisit, dicté par le
  destinataire. Aucun écran ne doit le montrer, ni le journaliser.
- **L'adresse exacte de destination n'arrive qu'après acceptation.** Avant, le
  livreur voit le repère de collecte, la distance et sa rémunération — c'est
  tout ce que contient l'offre.
- **Le prix client n'apparaît jamais.** Seule sa rémunération est renvoyée.
- **Une offre perdue n'est pas une erreur.** `409` avec `rejectionCode` est le
  fonctionnement normal d'une vague : un autre livreur a été plus rapide.
  L'écran doit l'annoncer calmement.
- Un livreur ne porte **qu'une seule offre à la fois**. Au retour de
  l'application, `GET /offers/current` resynchronise.

## Authentification

```
POST /api/driver/v1/auth/otp/request   { phone, deviceId }
POST /api/driver/v1/auth/otp/verify    { challengeId, code, deviceId, displayName }
POST /api/driver/v1/auth/refresh       { refreshToken, deviceId }
POST /api/driver/v1/auth/logout        { refreshToken }
```

Le compte naît à la première vérification, avec le rôle `driver`. Il reste
inactif tant que le KYC n'est pas validé : l'application doit prévoir cet état
intermédiaire, entre « inscrit » et « peut travailler ».

`otp/request` répond la même chose quel que soit le numéro : le nom se demande
après vérification, pas avant.

Le jeton de rafraîchissement **ne sert qu'une fois**. Avec une file d'actions
hors connexion, c'est le point le plus délicat : deux rafraîchissements
concurrents avec le même jeton révoquent la session. Un seul appel de
rafraîchissement à la fois, sérialisé, et le nouveau jeton écrit avant tout
autre appel.

## Démarrage

```bash
cd apps/driver_app
flutter create --org com.hbatechettrade --project-name hba_driver .
```

Base d'API en développement : `http://localhost:5102/api/driver/v1`.
