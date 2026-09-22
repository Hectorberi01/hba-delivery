# 0005 — L'OTP du destinataire est la preuve de livraison

Statut : acceptée — septembre 2026

## Contexte

Il faut une preuve que le colis est bien arrivé entre les bonnes mains. Une
signature sur écran ne prouve pas grand-chose et suppose un téléphone qui la
gère correctement. Une photo prouve qu'un colis a été posé quelque part.

## Décision

Un code à six chiffres est généré à la création de la livraison. Le destinataire
le reçoit par SMS ou WhatsApp ; le client donneur d'ordre peut le voir dans son
application. Le livreur ne le reçoit **jamais** : il le saisit, dicté par le
destinataire au moment de la remise.

La transition vers `DELIVERED` n'existe que pour l'acteur `Driver` et exige un
code valide. Au bout de cinq échecs, le code se verrouille et la livraison doit
passer par le support.

## Conséquences

- Le code est stocké **en clair**, parce que le client doit pouvoir le lire dans
  son application. Un hachage rendrait la matrice de visibilité inapplicable.
  C'est un compromis conscient : le code n'a de valeur que pendant la course, il
  n'est jamais réutilisé, et aucune projection destinée au livreur, au
  commerçant, au partenaire ou à l'administrateur ne le contient.
- L'alphabet est numérique : le code se dicte à voix haute, souvent en fon ou en
  français, parfois dans le bruit d'un carrefour.
- Un destinataire injoignable bloque la remise. C'est voulu — mais cela impose
  un parcours support, qui reste à écrire.
