# Matrice de visibilité — où elle est réellement appliquée

La matrice du référentiel n'est pas qu'un tableau de documentation : elle est
implémentée à deux endroits précis du service Delivery, et nulle part ailleurs.

| Donnée | Client | Destinataire | Livreur | Commerçant | Partenaire | Admin |
|---|---|---|---|---|---|---|
| Prix et détail du devis | Oui | Non | Sa rémunération seulement | Si donneur d'ordre | Oui | Oui |
| Téléphone du livreur | Pendant la mission | Par SMS pendant la mission | — | Pendant la mission | Non | Oui |
| **Position du livreur AFFECTÉ, en direct** | **Pendant la mission, depuis le 01/10/2026** | **Non** | — | **Non** | **Non** | Oui |
| Adresse de destination | Oui | Oui | Après acceptation | Oui | Oui | Oui |
| OTP de remise | Oui | Oui | Non (il le saisit) | Non | Non | Non |
| Documents KYC livreur | Non | Non | Les siens | Non | Non | Oui (URL signée) |
| Habilitations du livreur | Non | Non | Les siennes | Non | Non | Oui |

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

Et, **depuis le 03/10/2026**, un champ de plus — et surtout un filtre en moins :

| Champ | Avant `DRIVER_ASSIGNED` | Après |
| --- | --- | --- |
| **disponibilité** | **oui, depuis le 03/10/2026** | — |

- **LA CARTE MONTRE DÉSORMAIS TOUS LES LIVREURS DU RAYON, disponibles ou non.**
  Elle ne rendait que les offrables ; c'était un choix de la passerelle, jamais
  une règle de ce document. Le client voit donc aussi, en point éteint, un
  livreur **en mission pour quelqu'un d'autre**. C'est le vrai élargissement.
- **Ce qui le borne est inchangé** : ni identifiant, ni nom, ni plaque. Le
  point 37 accorde au client servi un livreur NOMMÉ qu'il suit du début à la
  fin ; ici personne n'est nommé, et rien ne relie un point d'aujourd'hui à un
  point d'hier.
- **La disponibilité ne dit pas la raison.** « Indisponible » couvre la mission,
  la déconnexion récente et le dossier redevenu invalide. Rendre la raison
  supposerait que Driver la donne par livreur, ce qui en dirait bien plus sur
  chacun.
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

### 5. Le suivi en direct — point 37, tranché le 1er octobre 2026

**La matrice était MUETTE sur ce point, et le silence n'est pas une
autorisation.** Elle disait quand le client voit le *téléphone* du livreur
affecté — pendant la mission — et ce qu'il voit des livreurs *non affectés* —
une position anonyme et un type de véhicule, point 24. Elle ne disait rien de la
**position du livreur affecté**, parce qu'avant la phase 8 personne ne pouvait
la lui montrer : l'écran de suivi affichait un nom, un véhicule, un code de
remise, et aucune carte.

**Décision : le client voit la position en direct du livreur affecté à sa
mission, du moment de l'affectation à la clôture. Et rien avant, rien après.**

| | Avant `DRIVER_ASSIGNED` | Pendant la mission | Après la clôture |
| --- | --- | --- | --- |
| position du livreur affecté | *il n'y en a pas* | **oui, en direct** | **non** |
| cap, vitesse | *il n'y en a pas* | **non** | non |
| précision GPS | *il n'y en a pas* | **non** | non |
| historique du trajet | *il n'y en a pas* | **non** | **non** |

Le raisonnement, dans l'ordre où il a été fait :

- **Ce que le client gagne.** Savoir si le livreur arrive dans deux minutes ou
  dans vingt, sans appeler. C'est la question qu'il pose au téléphone quand il
  n'a pas la carte, et chaque appel évité est un appel que le livreur ne prend
  pas en roulant.
- **Ce qu'il voyait déjà.** Le nom, le téléphone, la plaque et le véhicule de
  cette personne précise. La position en direct n'ouvre donc pas l'identité :
  elle est déjà ouverte. Elle ajoute le **déplacement** d'un homme déjà nommé —
  ce qui reste une divulgation réelle, et c'est pourquoi elle est bornée.
- **Ce que les bornes protègent.** La position s'éteint à la clôture, en même
  temps que le téléphone du livreur. Sans cela, un client qui garde l'écran
  ouvert suivrait un livreur chez son client suivant, puis chez lui. C'est
  exactement la dérive que le point 24 refuse pour les livreurs non affectés, et
  elle serait pire ici : la personne est nommée.
- **Pourquoi ni le cap, ni la vitesse, ni la précision.** Ils ne répondent à
  aucune question du client — « où est-il » suffit à décider d'attendre — et la
  vitesse d'un homme au travail est une donnée de surveillance. L'exploitation
  les voit, parce qu'elle doit comprendre pourquoi une course traîne ; le client
  n'a pas à savoir si le livreur roule à 15 ou à 45.
- **Pourquoi pas d'historique.** Rien n'est conservé : le flux est un instant,
  pas une piste. Le dépôt ne stocke aucun trajet, et la phase 9 a écarté
  l'historique précisément pour ne pas avoir à répondre à cette question.

**Le destinataire n'y a pas droit, et c'est volontaire.** Il reçoit son OTP par
SMS et le téléphone du livreur par SMS ; il n'a pas de compte, pas d'écran, et
suivre quelqu'un sur une carte demande d'être identifié. Le commerçant non plus :
il remet un colis, il ne l'escorte pas.

**Où c'est appliqué.** `CustomerHub`, dans le service `Hba.Realtime` : un client
ne reçoit le flux d'une mission qu'après vérification auprès du service
propriétaire — Ride ou Delivery — que la mission est la sienne ET qu'un livreur
y est affecté. L'abonnement n'est jamais accordé sur la parole du client, et il
est révoqué quand la mission se clôt.

## Les avis et les notes — service Rating (point 41, 3 octobre 2026)

**Le client voit : ses propres missions à noter, et la note qu'il a posée.** Rien
d'autre. La liste des avis en attente ne porte **aucun identifiant de livreur**,
ni avant, ni après : le point 24 réserve l'identité du livreur à l'affectation,
et la fin de la mission ne la rouvre pas. Le client note sa COURSE ; Rating sait
à qui la note revient, parce qu'il l'a écrit lui-même au moment où la mission
s'est achevée, depuis l'événement du service propriétaire.

**Le client ne voit la note d'aucun livreur.** Aucune route cliente ne la rend —
ni avant l'affectation, ni après. La question est ouverte au point 41, et le
silence n'est pas une autorisation.

**Le livreur voit : sa moyenne, et ses avis un par un.** Sans identifiant de
client, et sans la date de naissance de la rancune : il apprend ce qu'on lui
reproche, pas QUI le lui reproche. Une note nominative se règle entre quatre
yeux, et un avis qui se règle ne sert plus à rien. La mission, elle, est nommée
par sa nature et sa date — sans quoi « deux étoiles » ne se rattache à aucun
trajet, donc ne se corrige pas.

**La moyenne n'est montrée que quand elle tient debout**, et c'est le SERVICE qui
le dit, par un booléen. Le seuil ne se rejoue dans aucune application : trois
écrans avec trois seuils finiraient par se contredire, et le livreur lirait
« 4,0 » là où le client lit « pas encore de note ».

**Le dispatch ne lit pas les notes.** Une note basse ne désactive personne et
n'influence pas l'attribution. C'est une décision, pas un oubli, et les deux
applications l'écrivent à l'écran du livreur pour qu'il ne la devine pas de
travers.

**Où c'est appliqué.** Dans les handlers de `Hba.Rating.Application`, jamais dans
la passerelle : `ListPendingReviews` et `SubmitReview` reprennent le sujet du
jeton quand l'appelant est un client, `GetDriverRating` et `ListDriverReviews`
reprennent le `driver_id` du jeton quand l'appelant est un livreur. Un
identifiant passé dans la requête ne dit pas qui appelle, et un back-office — qui
traite les réclamations — est le seul à pouvoir nommer quelqu'un.

## Ce qui est volontairement absent

Les BFF ne filtrent rien. Ils traduisent REST en gRPC et remontent la réponse
telle quelle. Si un BFF se mettait à masquer des champs, il y aurait deux
sources de vérité pour la même règle — et celle du BFF serait contournable en
appelant le service directement depuis le réseau interne.
