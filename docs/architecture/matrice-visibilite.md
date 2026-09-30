# Matrice de visibilité — où elle est réellement appliquée

La matrice du référentiel n'est pas qu'un tableau de documentation : elle est
implémentée à deux endroits précis du service Delivery, et nulle part ailleurs.

| Donnée | Client | Destinataire | Livreur | Commerçant | Partenaire | Admin |
|---|---|---|---|---|---|---|
| Prix et détail du devis | Oui | Non | Sa rémunération seulement | Si donneur d'ordre | Oui | Oui |
| Téléphone du livreur | Pendant la mission | Par SMS pendant la mission | — | Pendant la mission | Non | Oui |
| Adresse de destination | Oui | Oui | Après acceptation | Oui | Oui | Oui |
| OTP de remise | Oui | Oui | Non (il le saisit) | Non | Non | Non |
| Documents KYC livreur | Non | Non | Les siens | Non | Non | Oui (URL signée) |

## Les deux points d'application

### 1. `DeliveryAccess` — le périmètre

`src/Services/Delivery/Hba.Delivery.Application/Authorization/DeliveryAccess.cs`

Décide **ce que l'appelant a le droit de voir du tout** :

- `ScopeFor` impose le filtre d'une liste. Le client ne peut pas demander les
  livraisons d'un autre : le `CustomerId` du filtre est écrasé par celui du
  jeton, quoi que l'appelant ait envoyé.
- `EnsureCanRead` échoue en **NotFound** et non en Forbidden hors périmètre. Un
  403 confirmerait l'existence de la livraison à quelqu'un qui n'a rien à en
  savoir.

### 2. `DeliveryViewMapper` — le contenu

`src/Services/Delivery/Hba.Delivery.Application/Views/DeliveryViewMapper.cs`

Décide **ce que l'appelant voit de la livraison**. C'est le seul endroit du
service où un agrégat devient un DTO, et il n'existe pas de version « complète »
du DTO qui pourrait circuler par erreur : les champs interdits valent `null`.

- l'OTP n'est renseigné que pour le client donneur d'ordre, et seulement tant
  que la course est ouverte ;
- le livreur reçoit `DriverEarningXof` et jamais `Pricing` ;
- l'adresse de destination et le destinataire sont nuls pour un livreur non
  affecté ;
- le téléphone du livreur n'apparaît que pendant la mission, pour le client et
  le commerçant ;
- **le téléphone du destinataire s'éteint pour le livreur à la clôture** —
  ajouté le 30 septembre 2026. Il n'avait aucune borne de temps, alors que l'OTP,
  lui, était déjà coupé : l'historique rendant les vingt-cinq dernières courses,
  un livreur gardait indéfiniment le numéro personnel de vingt-cinq
  destinataires, pour des courses finies. L'adresse reste — il est allé là-bas,
  son historique doit rester relisible, et un repère écrit n'est pas un moyen de
  joindre quelqu'un.

Le destinataire n'apparaît pas dans cette matrice côté API : **ce n'est pas un
utilisateur authentifié**. Il reçoit l'OTP et l'adresse par SMS ou WhatsApp, via
le service Notification. Aucun compte ne doit lui être créé implicitement.

### 3. `MediaAccess` — la preuve de livraison

`src/Services/Media/Hba.Media.Application/Assets/MediaAccess.cs`

Décide **qui peut revoir un fichier déposé**. Une preuve de livraison n'y est pas
un média comme un autre, et c'est une décision explicite du 30 septembre 2026
(point 7 de `points-a-trancher.md`).

| Appelant | Photo de profil, pièces du dossier livreur, facture | Preuve de livraison |
| --- | --- | --- |
| le titulaire (client, livreur, commerce) | oui, pour ce qui est à lui | — *(une course n'a pas de compte)* |
| `ops`, `support`, `finance` | oui | **non** |
| `admin` | oui | **oui** |
| `service` | oui | oui |
| non authentifié | non | non |

- **La preuve est gardée un mois**, puis effacée par `PurgeDesPreuves`
  (`Hba.Media.Api/Scheduling`), objet d'abord, ligne d'inventaire ensuite.
- **`admin` seul la revoit**, parce qu'une photo de remise cadre une porte, une
  cour, parfois quelqu'un qui n'a rien demandé et qui n'est même pas client. La
  restreindre au rôle le plus étroit est le prix de la garder.
- **Conséquence assumée** : un agent du `support` qui traite une réclamation ne
  verra pas la photo et devra passer par un `admin`. L'élargir se fait en
  ajoutant un rôle dans `PeutVoirCeGenreDeMedia`, et nulle part ailleurs.
- **La liste tait ce qu'elle refuserait de rendre.** `ListMediaHandler` retire
  les preuves pour un appelant qui n'y a pas droit : laisser paraître la fiche
  apprendrait à `ops` qu'une photo existe, quand elle a été prise et par qui,
  pour une pièce qu'il ne peut pas ouvrir.
- **La suppression suit la lecture.** `DeleteMediaHandler` passe par la même
  vérification : `ops` ne peut pas effacer une preuve qu'il ne peut pas voir. La
  purge par rétention, elle, passe par le dépôt directement — elle n'a pas
  d'appelant à qui demander la permission, et c'est voulu. L'effacement de compte
  (`DeleteOwnerMediaCommand`) aussi : une suppression de compte doit tout
  emporter, sans exception de nature.
- **Le dépôt suit la même logique, à l'envers.** `MediaAccess.PeutDeposer`
  n'accepte du rôle `service` qu'un seul couple — une preuve, sous une course —
  parce que c'est Delivery qui dépose à la place du livreur affecté (point 7,
  question 4). Aucun jeton d'utilisateur ne peut atteindre ce couple, une course
  n'ayant pas de compte ; c'est ce qui rend l'ouverture sûre.
- **Le `service` passe, et il le faut.** Le jour où le client devra revoir la
  preuve de SA livraison, c'est Delivery qui l'autorisera puis demandera l'URL
  signée avec un jeton de service — point 27 : « l'autorisation reste au service
  propriétaire […] Media ne discute pas ». Media ne sait pas qui a commandé quoi,
  et le lui apprendre serait lui donner le métier d'un autre service.

Éprouvé par `AutorisationDesMediasTests` et `ListeDesMediasTests`
(`src/Services/Media/tests/Hba.Media.Application.Tests/`).

### 4. La carte du client — ce qu'elle ne montre pas

`apps/client_app/lib/features/deliveries/livreurs_proches.dart`

Les livreurs autour du client sont rendus **en coordonnées et en type de
véhicule** — et rien d'autre.

| Champ | Avant `DRIVER_ASSIGNED` | Après |
| --- | --- | --- |
| position | oui (point 24) | oui |
| **type de véhicule** | **oui, depuis le 30/09/2026** | oui |
| nom | non | oui |
| téléphone | non | oui, pendant la mission |
| plaque | non | oui |
| identifiant | **jamais** | — |

- **L'identifiant n'est jamais rendu, et c'est ce qui borne tout le reste** : il
  permettrait de reconnaître le même livreur d'un jour sur l'autre, donc de
  suivre ses horaires et ses trajets. Deux livreurs à moto restent
  indistinguables, et rien ne relie une position du jour à celle de la veille.
- **Le type de véhicule est semi-identifiant**, et c'est le prix assumé de la
  décision : quatre de ses cinq valeurs sont rares à Cotonou, donc un tricycle au
  milieu des motos se repère. Le point 24 porte le raisonnement.
- **La mise en forme se fait à la passerelle.** `FindAvailableNearby` rend des
  identifiants parce que le dispatch en a besoin ; c'est à la frontière du client
  qu'ils disparaissent. Ajouter un champ à cette projection reste une décision de
  référentiel, pas une commodité d'affichage.

Les deux applications partagent le dessin des épingles (`Epingles`, dans
`hba_ui`) et, depuis cette décision, ce qu'elles y mettent : le même véhicule en
relief sur les deux cartes.

## Ce qui est volontairement absent

Les BFF ne filtrent rien. Ils traduisent REST en gRPC et remontent la réponse
telle quelle. Si un BFF se mettait à masquer des champs, il y aurait deux
sources de vérité pour la même règle — et celle du BFF serait contournable en
appelant le service directement depuis le réseau interne.
