# 0012 — Une route publique ne doit rien dire de plus que nécessaire

Statut : acceptée — septembre 2026

## Contexte

`RequestOtp` est appelable par n'importe qui, sans jeton : c'est par là qu'on
obtient le premier. Sa première version renvoyait `isNewAccount`, pour que
l'application sache s'il fallait demander un nom après la vérification. Elle
refusait aussi explicitement les numéros rattachés à un compte à mot de passe.

Deux confidences de trop. Avec une liste de numéros béninois et une boucle,
n'importe qui pouvait dresser la liste de ceux qui ont un compte chez HBA, et
distinguer les clients des commerçants. C'est exploitable pour de l'hameçonnage
ciblé, et c'est une donnée que HBA n'a aucune raison de distribuer.

La limitation de débit ne rattrapait rien : elle était par numéro, et une
énumération teste chaque numéro une seule fois.

## Décision

- **`RequestOtp` répond la même chose dans tous les cas.** Un défi est toujours
  créé et toujours renvoyé, avec les mêmes champs et les mêmes durées, que le
  numéro soit inconnu, client, livreur, commerçant ou suspendu.
- **Le SMS, lui, ne part que si le numéro a le droit de se connecter ainsi.**
  Le défi porte un indicateur interne ; sans code envoyé, la vérification
  échoue avec le message ordinaire « code incorrect ».
- **`is_new_account` est retiré du contrat**, son numéro de champ réservé.
  L'application demande le nom APRÈS vérification, quand elle sait déjà qu'elle
  parle au titulaire du numéro.
- **La limitation par adresse IP est posée au BFF**, pas dans Identity : le
  service ne voit que l'adresse du BFF. La limitation par numéro reste dans
  Identity, avec en plus un plafond horaire — sinon on contourne le délai entre
  deux envois en attendant soixante secondes, indéfiniment.
- **Connexion par mot de passe** : identifiant inconnu et mot de passe faux
  donnent déjà la même réponse.

## Conséquences

- L'application client perd le raccourci qui lui disait « c'est une
  inscription ». Elle demande le nom à tout le monde après vérification, et
  l'ignore si le compte en a déjà un. Un écran de plus, contre un annuaire de
  moins.
- Un utilisateur suspendu ne reçoit plus d'explication à sa tentative de
  connexion : il reçoit « code incorrect ». C'est délibérément frustrant, et
  c'est au support de le renseigner — pas à une route publique.
- Un test de bout en bout vérifie la garantie : le même numéro, une fois lié à
  un compte de commerçant, répond exactement comme un numéro inconnu.
- Reste ouvert : la mesure du temps de réponse. Un numéro éligible déclenche une
  écriture d'Outbox que les autres n'ont pas, donc une réponse très légèrement
  plus lente. Combler cet écart demanderait un travail constant, et n'est pas
  fait.
