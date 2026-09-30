# Points à trancher

Ce que le référentiel des acteurs ne décide pas, et qui n'a donc **pas** été
implémenté. Chaque point liste des options.

Un point **TRANCHÉ** porte sa décision et sa date en tête, et garde ses options
en dessous : savoir ce qui a été écarté, et pourquoi, vaut autant que la
décision elle-même le jour où quelqu'un veut la rouvrir. **TRANCHÉ ne veut pas
dire implémenté** — l'état du code est écrit dans chaque point.

---

## 1. Règlement du commerçant donneur d'ordre — TRANCHÉ

**Tranché le 29 septembre 2026 : compte PRÉPAYÉ d'abord, postpayé plus tard.**
Le modèle de l'ADR 0013 est retenu entier — un compte, un mode de règlement, un
plafond de crédit, et la règle unique `solde + plafond ≥ montant` — mais seul le
prépayé est mis en service. Le plafond vaut donc zéro pour tout le monde au
départ : pas d'encours, pas de relance, pas de recouvrement. Le postpayé
n'ajoutera ensuite aucun modèle, seulement un plafond accordé par `finance`.

**La recharge se fait À LA MAIN, et c'est une décision du même jour.** Aucune
ligne de code de paiement : `finance` constate un virement ou un dépôt et crédite
le compte par un mouvement `topup`. La raison est structurelle — `PaymentIntent`
refuse un paiement sans livraison (`MissingDeliveryId`, « un paiement nomme la
livraison qu'il règle ») et une recharge n'a pas de course. Ouvrir ce chemin
demanderait soit de généraliser l'intention de paiement, soit un second agrégat ;
les deux ont été écartés pour le pilote, où HBA Express et HBA Food sont la même
maison et se créditent par une écriture. La recharge par mobile money attend le
premier commerçant **externe**.

**État du code au 29 septembre 2026** : le domaine, la persistance et l'entrée
gRPC de Billing existent, avec vingt-trois tests de domaine.
`CreateDeliveryHandler` refuse toujours avec `MERCHANT_SETTLEMENT_UNDECIDED` :
il reste le client gRPC côté Delivery, et la question de la transition — voir
ci-dessous.

Les options ci-dessous sont conservées pour mémoire.

---

**Marqué « À TRANCHER » dans le référentiel.**

Quand un commerçant commande une livraison pour son propre compte, comment
est-il réglé ?

- **Option A — paiement FedaPay à chaque course.** Même parcours que le client
  particulier. Simple, aucun encours, mais lourd pour un commerçant qui fait
  trente courses par jour.
- **Option B — facturation périodique.** Les courses s'accumulent, une facture
  part chaque semaine ou chaque mois. Confortable pour le commerçant, mais cela
  demande un encours, un plafond, une relance et un recouvrement — donc le
  service Settlement, qui est hors MVP.
- **Option C — compte prépayé.** Apparue après coup : le commerçant alimente un
  compte à l'avance, chaque course le débite. Ni PIN par course, ni encours.

**Proposition en attente de décision** : les options B et C **ensemble** — un
compte par donneur d'ordre, prépayé par défaut, postpayé sur décision de
`finance`. Détaillée par l'ADR 0013, conçue dans
`facturation-donneurs-dordre.md`.

**État du code** : `CreateDeliveryHandler` refuse explicitement la création avec
le code `MERCHANT_SETTLEMENT_UNDECIDED`. Rien n'est à moitié implémenté.

---

## 2. Paiement des livraisons créées par un partenaire — TRANCHÉ

**Tranché le 29 septembre 2026 : le même compte que le point 1.** Un partenaire
— HBA Express, HBA Food ou externe — a un compte prépayé chez Billing, débité à
chaque course. Aucune exception pour les partenaires maison : une course de HBA
Express débite le compte de HBA Express, ce qui garde la frontière comptable
entre les entités nette et permet de brancher un partenaire externe sans rien
refaire.

**État du code** : inchangé. Aucune intention de paiement n'est créée pour un
partenaire, et la livraison reste en `PENDING_PAYMENT`.

Les options ci-dessous sont conservées pour mémoire.

---

**Absent du référentiel.**

Un partenaire (HBA Express, HBA Food, ou externe) crée une livraison. Qui paie,
et quand la livraison quitte-t-elle `PENDING_PAYMENT` ?

- **Option A — le partenaire a déjà encaissé.** HBA Delivery lui fait crédit et
  la livraison est confirmée dès la création. Il faut alors un mécanisme de
  compensation entre HBA Delivery et le partenaire.
- **Option B — le partenaire paie course par course** via FedaPay, exactement
  comme un client.
- **Option C — facturation périodique du partenaire**, avec les mêmes
  conséquences que l'option B du point 1.
- **Option D — compte prépayé**, comme au point 1.

**L'option B est impossible**, vérification faite : chez FedaPay comme chez MTN
MoMo, le payeur valide sur son téléphone avec son code PIN. Aucun débit
serveur, aucun mandat pré-autorisé. Un partenaire est un système ; il n'a
personne devant un téléphone.

**Proposition en attente de décision** : les options C et D **ensemble**,
comme au point 1. Détaillée par l'ADR 0013.

**État du code** : aucune intention de paiement n'est créée pour un partenaire.
La livraison reste en `PENDING_PAYMENT` et n'entre jamais en dispatch. C'est
volontairement bloquant : le premier partenaire branché rendra le sujet visible
immédiatement plutôt que six mois plus tard.

---

## 3. Politique d'annulation — TRANCHÉ EN PARTIE

**Tranché le 29 septembre 2026 : gratuit avant l'attribution d'un livreur,
frais fixes après.** Tant qu'aucun livreur n'a accepté, le client récupère tout.
Dès `DRIVER_ASSIGNED`, une retenue fixe couvre le déplacement engagé et le reste
est remboursé.

**Ce que cela ne dit pas, et qui reste à trancher :**

1. **Le montant de la retenue.** Il doit être affiché AVANT que le client ne
   confirme son annulation — l'écran promet aujourd'hui que « les conditions de
   remboursement vous seront confirmées par le service », ce que personne ne
   tient.
2. **Sa répartition entre HBA et le livreur.** Une retenue qui n'irait pas au
   livreur ne couvre pas ce qu'elle prétend couvrir : c'est lui qui s'est
   déplacé.
3. **Le remboursement lui-même n'existe pas, ET IL NE PEUT PAS ÊTRE AUTOMATISÉ
   AUJOURD'HUI.** Aucun code ne rend d'argent.

   **Cette ligne disait « FedaPay a une API de remboursement, elle n'est pas
   branchée ». C'EST FAUX, et vérifié le 30 septembre 2026 dans la
   documentation du fournisseur :**

   > « Refunds are currently only available with MTN Mobile Money and can only be
   > made from the dashboard of your merchant account. »
   > — docs-v1.fedapay.com/payments/refunding

   L'index de la documentation courante (`docs.fedapay.com/llms.txt`) le
   confirme : la référence API couvre `customers`, `collects`, `payouts`,
   `events`, `balances`, `currencies`, `logs` et `webhooks`. **Aucune section
   remboursement.** Le seul « Remboursements » documenté est un écran du tableau
   de bord, « Suivi et Filtrage ».

   **Trois conséquences pour la décision à prendre :**

   - Un remboursement automatique déclenché par le code est **impossible** tant
     que le fournisseur n'expose rien. Toute promesse faite à l'écran est donc
     une promesse que `finance` tient à la main, opérateur par opérateur.
   - Le remboursement n'existe **que sur MTN Mobile Money**. Un client qui a payé
     par Moov ou Celtiis ne peut pas être remboursé par ce canal du tout — c'est
     un virement à faire autrement, et ça change la politique d'annulation.
   - Ce qui EST branchable sans rien inventer : **constater** un remboursement
     fait depuis le tableau de bord. `FedaPayClient` traduit déjà le statut
     `refunded`, et le webhook relit systématiquement la transaction chez le
     fournisseur. Il manque trois choses : `ApplyProviderOutcomeHandler` traite
     aujourd'hui `Refunded` dans son `default` et le journalise « toujours en
     cours chez le fournisseur » — ce qui est faux ; `PaymentIntent` n'a pas de
     `MarkRefunded` ; et `PaymentRefunded`, déclaré dans le contrat, n'a ni
     producteur ni consommateur. **Reste à vérifier sur le compte marchand si
     FedaPay émet bien une notification lors d'un remboursement fait au tableau
     de bord** : sans elle, même « constater » demande une relecture périodique.

Le texte ci-dessous est conservé pour mémoire.

---

Le référentiel dit que le client « annule selon la politique d'annulation », mais
ne décrit pas cette politique.

À décider : y a-t-il des frais ? À partir de quel état — après affectation,
après arrivée du livreur au point de collecte ? Le livreur est-il dédommagé d'un
déplacement inutile, et sur quelle base ?

**État du code** : l'annulation est autorisée par la table des transitions
jusqu'à `DRIVER_ASSIGNED` inclus, et l'événement `DeliveryCancelled` transporte
le statut précédent. C'est exactement ce qu'il faut pour appliquer une politique
de remboursement — quand elle existera. Aucun calcul de frais n'est fait.

---

## 4. Destin des statuts après `NO_DRIVER_FOUND`

Le remboursement est-il automatique ou passe-t-il par `finance` ? Le
référentiel donne à `finance` le droit de déclencher un remboursement, sans dire
si le cas « aucun livreur » en fait partie.

**État du code** : `NoDriverFound` est publié sur `hba.delivery.events.v1`.
Personne ne le consomme pour l'instant.

**Ce point bloque une demande précise**, faite le 27 septembre 2026 : pouvoir
proposer à la main une course en attente à un livreur en ligne. La
fonctionnalité existe désormais pour les courses en `SEARCHING_DRIVER`
(`OfferToDriver`), mais **pas** pour celles en `NO_DRIVER_FOUND` : cet état est
terminal dans `DeliveryTransitions`, et rouvrir une course close demande
d'abord de répondre à trois questions que le référentiel ne traite pas.

1. Que devient le paiement déjà encaissé — a-t-il été remboursé entre-temps, et
   si oui la course repart-elle impayée ?
2. Le client a-t-il été prévenu que sa course était abandonnée, et que lui
   dit-on si elle repart une heure après ?
3. Jusqu'à quand une course peut-elle repartir ? Sans limite, une course de la
   semaine dernière reste indéfiniment réactivable.

Tant que ce n'est pas tranché, la console affiche l'explication à la place du
bouton, et la voie ouverte reste : clore la course, puis en créer une nouvelle.

---

## 5. Délai de réponse à une offre

Le référentiel donne « 30 s par défaut ». Reste à trancher : combien de vagues,
avec quel élargissement de rayon, et au bout de combien de temps total la course
bascule en `NO_DRIVER_FOUND` ?

**TRANCHÉ**, septembre 2026 : **trois vagues, à 2 / 4 / 6 km, 30 s chacune**,
soit 90 s avant `NO_DRIVER_FOUND`.

Le raisonnement : à Cotonou, 2 km couvrent déjà un quartier entier de
zemidjans, donc la première vague a de bonnes chances d'aboutir tout en
gardant un trajet à vide court. Quatre-vingt-dix secondes est le plus long
qu'un client accepte d'attendre sans croire que l'application est bloquée.

Les trois valeurs restent **en configuration**, pas en dur : ce sont des
paramètres d'exploitation, et le pilote dira si le premier rayon est trop
étroit aux heures creuses.

**État du code** : `Dispatch` est encore une coquille ; le service `Driver`,
lui, sait répondre « qui est disponible autour de ce point, dans ce rayon,
sans ceux des vagues précédentes » — c'est exactement ce que la politique
ci-dessus consomme.

---

## 6. `order.ready` de HBA Food

Le référentiel indique que HBA Food publie `order.ready`. Ce signal ne
correspond à aucun état de la livraison : la commande prête, c'est une
information sur le commerçant, pas sur la course.

À trancher : `order.ready` doit-il décaler l'envoi des offres — pour qu'un
livreur n'attende pas vingt minutes devant un restaurant — ou n'être qu'une
notification ?

**État du code** : le topic `hba.platform.order.events.v1` est créé, personne ne
le consomme.

---

## 7. Rétention des preuves et des positions — TRANCHÉ (sauf les positions des livreurs)

Combien de temps garde-t-on les photos de collecte et de remise dans MinIO ? Et
les positions des livreurs, qui ne sont pas persistées en base relationnelle —
sont-elles archivées, et pour combien de temps ?

C'est une question de droit autant que de stockage : ce sont des données
personnelles de travailleurs indépendants.

**État du code** : aucune politique de rétention n'est écrite.

### Ce que l'audit du 30 septembre 2026 a ajouté à ce point

**La preuve photographique n'existe pas du tout**, et c'est la question d'avant
la rétention. Le champ traverse pourtant tout le système — la passerelle accepte
`ProofObjectKey`, l'agrégat le range dans `DeliveryProofObjectKey` — mais :

- **aucune route ne permet de le produire.** Une preuve appartient à la COURSE
  (`MediaOwnerType.Delivery`), et une course n'a pas de compte : personne ne peut
  « être » ce propriétaire, donc Media refuse le dépôt à tout le monde sauf au
  back-office ;
- **ni l'application livreur ni la console n'envoie ce champ.** Il est mort sur
  le fil ;
- il était néanmoins **stocké sans la moindre vérification**. Depuis le
  30 septembre 2026, une clé non vide est refusée (`PROOF_NOT_SUPPORTED`) :
  stocker une chaîne inventée par celui qu'elle doit engager fabriquait une pièce
  à charge, et l'ignorer en silence aurait laissé croire qu'une preuve était
  gardée.

**Ce qui fait foi en attendant est le code de remise**, dicté par le destinataire
— l'ADR 0005 en fait la preuve de livraison. La photo n'est pas un substitut,
c'est un complément, et il est facultatif tant qu'on ne l'a pas décidé.

### Question 1 — exigée, proposée ou absente — TRANCHÉE

**Décidé le 30 septembre 2026 : la photo est PROPOSÉE, aux deux étapes.** Le
livreur peut en joindre une à la collecte et à la remise ; sans réseau, l'étape
passe sans elle. Cela répond du même coup à la question 2 : les deux étapes, au
même régime.

#### Ce qui a été écarté, et pourquoi

**Exigée aux deux étapes.** C'est la valeur probante maximale, et elle se heurte
à ce sur quoi toute l'application livreur est bâtie. La file d'actions hors ligne
range du JSON dans le coffre chiffré, plafonnée à cinquante entrées : **elle ne
sait pas transporter un fichier**. Rendre la photo obligatoire signifiait donc
soit bloquer une remise quand il n'y a pas de réseau — devant un client déjà
servi, à Cotonou —, soit construire une file binaire : stockage disque, plafond,
purge, reprise, et un état « remise faite, photo en attente » qui n'existe pas.
S'y ajoutent la mémoire pleine et l'appareil photo refusé, qui immobiliseraient
le livreur pour une raison qui ne regarde pas la course.

**Exigée à la collecte seulement.** Tentant : chez le commerçant le réseau est
meilleur, l'adresse est connue, et la photo protège le livreur contre « le colis
était déjà abîmé ». Écartée parce qu'elle crée deux régimes à retenir pour un
livreur qui, lui, fait le même geste deux fois — et parce que le cas « pas de
réseau à la collecte » reste à traiter de toute façon, donc le chantier de la
file binaire n'est qu'à moitié évité.

**Absente.** Le code de remise suffit à prouver la remise (ADR 0005), et c'est
vrai. Mais il ne dit rien de l'ÉTAT du colis, ni à la collecte ni à l'arrivée :
c'est la seule chose qu'une photo apporte et qu'un code ne remplace pas.

#### Ce que cette décision fixe

- La remise **n'attend jamais** la photo. Aucune étape ne se bloque sur elle,
  aucune file binaire n'est construite.
- Sans réseau, l'étape part **sans** photo et n'y revient pas : une photo
  rattrapée plus tard ne prouverait plus le même instant.
- La compression existe déjà (`flutter_image_compress`, utilisé pour le dossier)
  et Media plafonne à 5 Mo : une photo compressée pèse 100 à 300 Ko, ce que le
  forfait du livreur peut porter.

### Question 3 — qui la voit, et pendant combien de temps — TRANCHÉE

**Décidé le 30 septembre 2026 : gardée UN MOIS, revue par l'`admin` SEUL.**

Une photo de remise cadre une porte, une cour, parfois une personne qui n'a rien
demandé et qui n'est même pas cliente. C'est ce qui justifie les deux bornes :
une durée courte, et le rôle le plus étroit.

#### Ce que cette décision fixe

- **Un mois, puis l'effacement.** `PurgeDesPreuves`
  (`src/Services/Media/Hba.Media.Api/Scheduling/PurgeDesPreuves.cs`) balaie
  toutes les six heures, par lots de deux cents, et efface **l'objet d'abord, la
  ligne d'inventaire ensuite** — l'inverse exact de l'ordre du dépôt. Une ligne
  sans objet se voit et se rejoue ; un objet sans ligne est un fichier que plus
  rien ne nomme, c'est-à-dire précisément ce que la rétention devait empêcher.
  Les trois valeurs sont dans `appsettings.json`, section `ProofRetention`.
- **`admin` seul, et le `service`.** `MediaAccess.PeutVoirCeGenreDeMedia` refuse
  `DeliveryProof` à `ops`, à `support` et à `finance`, qui lisent pourtant tous
  les autres médias. Le `service` passe parce que le point 27 l'exige : c'est
  Delivery qui autorisera un jour un donneur d'ordre à revoir la preuve de SA
  course, puis demandera l'URL signée avec un jeton de service.
- **La liste tait ce qu'elle refuserait de rendre.** `ListMediaHandler` retire
  les preuves de l'inventaire rendu à un appelant qui n'y a pas droit : laisser
  paraître la fiche apprendrait à `ops` qu'une photo existe, quand elle a été
  prise et par qui, pour une pièce qu'il ne peut pas ouvrir.
- La matrice est consignée dans `docs/architecture/matrice-visibilite.md`,
  section 3, et éprouvée par `AutorisationDesMediasTests` et
  `ListeDesMediasTests`.

#### Ce que cette décision coûte, et qui a été dit avant de l'écrire

**Un agent du `support` qui traite une réclamation ne verra pas la photo** et
devra passer par un `admin`. C'est la conséquence directe de « l'admin » pris au
mot : le référentiel des acteurs interdit d'élargir de soi-même à `ops` ou
`support`. L'élargissement se fait en ajoutant un rôle dans
`PeutVoirCeGenreDeMedia`, et nulle part ailleurs.

**Les positions des livreurs ne sont PAS tranchées par là.** Ce point 7 les
posait dans la même phrase que les photos ; la décision ci-dessus ne porte que
sur les preuves. La rétention des positions reste ouverte.

### Question 4 — par où passe le dépôt — TRANCHÉE

**Décidé le 30 septembre 2026 : DELIVERY PORTE LES OCTETS.**

Le livreur envoie sa photo à la passerelle, qui la relaie à Delivery. Delivery
vérifie **sur son propre agrégat** que c'est bien le livreur affecté et que
l'étape a eu lieu, puis pousse le fichier chez Media **avec un jeton de service**
et rattache l'identifiant rendu à la course.

#### Pourquoi ce chemin plutôt qu'un autre

Le point 27 avait tranché le principe — le service propriétaire autorise, Media
ne discute pas — mais trois faits du code fermaient les autres portes :

- `MediaAccess.PeutDeposer` n'accepte un dépôt que **pour soi**, et une course
  n'a pas de compte : le jeton du livreur ne passe pas, même pour SA course ;
- Media ne peut pas savoir qui est affecté à quelle course sans faire le métier
  de Delivery, ce que le point 27 lui interdit ;
- la passerelle **ne détient aucun jeton de service**, et c'est délibéré.

#### Ce que cette décision coûte, et qui a été dit avant de l'écrire

- **Delivery gagne une route HTTP qui porte des octets**, alors que toutes ses
  autres entrées sont en gRPC. L'ADR 0021 l'explique — gRPC porte mal les
  binaires — mais c'est la deuxième exception du genre après Media, et il faut
  la voir comme telle. Une seule route, `POST /internal/v1/deliveries/{id}/proof`.
- **Media s'ouvre en écriture au rôle `service`**, ce qu'il refusait
  explicitement. L'ouverture est aussi étroite que la décision :
  `MediaAccess.EstUnDepotDeService` n'accepte qu'un couple,
  `MediaOwnerType.Delivery` + `MediaKind.DeliveryProof`. Rien d'autre — et ce
  couple est sûr parce qu'aucun jeton d'utilisateur ne peut l'atteindre.
- **Les octets font un saut de plus** : téléphone → passerelle → Delivery →
  Media. C'est le prix de ne pas donner de jeton de service à la passerelle.

#### Ce qui a été écarté

**Un laissez-passer signé.** Delivery rend un ticket court, l'app dépose chez
Media qui vérifie la signature et non le métier. Lecture la plus littérale du
point 27, mais elle demande une clé partagée, une fenêtre de rejeu à fermer, et
**trois appels réseau depuis un téléphone à Cotonou** là où il en faut un.

**Un jeton de service pour la passerelle.** Le moins de machinerie neuve, et le
plus cher : la passerelle deviendrait un appelant de confiance pour Media, donc
la moindre faille chez elle ouvrirait tous les médias de la plateforme, pièces
d'identité comprises.

**Le livreur comme propriétaire du média.** Rien à construire, le relais existant
suffisait. Écarté parce que la preuve aurait vécu dans le dossier du livreur —
donc emportée par l'effacement de son compte — et parce que cela contredit ce qui
est déjà écrit dans le code : une preuve de livraison appartient à la COURSE.

#### Les règles que l'agrégat applique

Une preuve se rattache à une étape **qui a eu lieu**, par le livreur **qui l'a
faite**, **une seule fois**, et **tant que c'est encore le même moment** :

| Refus | Code | Pourquoi |
| --- | --- | --- |
| l'étape n'a pas eu lieu | `PROOF_STEP_NOT_DONE` | une preuve de ce qui n'est pas arrivé n'est pas une preuve |
| une preuve est déjà là | `PROOF_ALREADY_ATTACHED` | sinon le livreur choisit laquelle raconte l'histoire |
| l'étape est trop ancienne | `PROOF_TOO_LATE` | « une photo rattrapée plus tard ne prouverait plus le même instant » |
| l'appelant n'est pas le livreur affecté | `ForbiddenException` | la preuve engage le livreur : elle ne vient que de lui |

Le **même identifiant** renvoyé deux fois ne lève pas : c'est un rejeu, pas un
remplacement. Pour la remise, c'est le **statut** `Delivered` qui fait foi et non
`CompletedAt`, parce qu'une annulation pose aussi une date de clôture — sans quoi
une course annulée aurait accepté la preuve d'une remise qui n'a jamais eu lieu.

#### L'écran livreur, construit le 30 septembre 2026

Un bouton **« Ajouter une photo (facultatif) »** au-dessus de la glissière, à la
collecte et à la remise. Le mot « facultatif » est la règle elle-même : ce qui
fait foi reste le code de remise (ADR 0005).

- **La photo se prend AVANT le geste et part APRÈS.** C'est la seule forme qui
  tienne les deux moitiés de la décision : proposée aux deux étapes, et « l'étape
  ne l'attend jamais ». La prendre après voudrait retenir un livreur déjà reparti ;
  l'envoyer avant bloquerait une remise devant un client qui attend.
- **Sans réseau, l'étape part et la photo est abandonnée — et on le dit.** « La
  photo n'a pas pu être jointe : elle ne montrerait plus ce moment si elle partait
  plus tard. » La taire laisserait croire qu'elle est jointe.
- **Rien de l'envoi ne peut faire échouer l'étape.** L'appareil photo refusé, un
  serveur qui refuse, une coupure : tous finissent par un mot discret, jamais en
  rouge — le rouge dit « votre course a un problème », et la course va bien.
- **La photo n'entre pas dans la file hors ligne** et n'est jamais réessayée,
  puisque la file ne sait pas transporter un fichier et que le service refuse
  passé la fenêtre.
- Réduction à 1200 px / qualité 80 — plus petit que pour une pièce d'identité :
  une CNI doit rester **lisible**, une photo de remise seulement
  **reconnaissable**. Le livreur paie ses données à la recharge.

### Question 5 — la durée de la fenêtre de dépôt — TRANCHÉE

**Décidé le 30 septembre 2026 : TRENTE MINUTES**
(`Delivery.FenetreDeDepotDeLaPreuve`).

#### Ce que ces trente minutes paient, et ce sont deux choses distinctes

- **L'envoi lui-même : six minutes dans le pire cas réel.** Le délai d'envoi est
  de trois minutes, et un jeton expiré ajoute une seconde tentative après
  rafraîchissement (voir le constat S7).
- **Le reste absorbe une horloge fausse**, et c'est la vraie raison du chiffre.

#### Le défaut que cette décision assume plutôt que de le réparer

**La soustraction mêle deux horloges.** `PickedUpAt` et `CompletedAt` portent
l'horodatage du **téléphone** — `NormalizeTimestamp` l'accepte tel quel jusqu'à
vingt-quatre heures dans le passé, parce que l'application met ses étapes en file
quand le réseau tombe. Le dépôt, lui, est daté par le **serveur**. Un téléphone
qui retarde de vingt minutes, courant sur un appareil d'entrée de gamme sans
réglage automatique, consomme donc vingt des trente minutes avant que le livreur
ait touché son appareil photo.

**Ce qui reste possible, et qui a été pesé** : un téléphone qui retarde de plus
d'une demi-heure refusera toujours la photo, avec un message que le livreur ne
pourra pas relier à sa cause.

**La réparation propre** — un instant posé par le serveur à chaque étape — coûte
deux colonnes et une migration, pour une pièce qui reste facultative. Elle a été
écartée à ce prix-là, pas par oubli. **Si le refus se voit en exploitation, c'est
ce chantier-là qu'il faut ouvrir, pas ce nombre qu'il faut augmenter.**

#### L'autre bout du marché

Une photo prise vingt-cinq minutes après la remise est acceptée comme si elle
montrait l'instant. C'est le prix de la tolérance ci-dessus, et il est supportable
parce que la photo n'est pas ce qui fait foi : l'ADR 0005 donne ce rôle au code
de remise.

Éprouvé par `PreuveDeLivraisonTests` — les tests de frontière lisent la constante,
un test garde sa valeur et tombe le jour où quelqu'un la change.

---

## 8. Amorçage du premier administrateur — TRANCHÉ

Option B retenue et implémentée : `AdminBootstrap`, un service hébergé d'Identity
qui crée le compte au démarrage si `Bootstrap:AdminEmail` et
`Bootstrap:AdminPassword` sont renseignés et qu'aucun administrateur n'existe.
Idempotent, et bruyant dans les journaux.

Ce qui reste ouvert : rien n'oblige à changer ce mot de passe à la première
connexion, ni à retirer la configuration ensuite. Un indicateur
« mot de passe à renouveler » sur le compte serait la suite logique.

---

## 9. Rétention des sessions et des comptes

Un jeton de rafraîchissement expiré ou révoqué reste en base : c'est ce qui
permet de détecter un rejeu. Mais pendant combien de temps ?

À trancher : au bout de combien de jours purge-t-on `refresh_tokens` ? Et que
devient le compte d'un livreur qui ne s'est pas connecté depuis un an — les
données d'un travailleur indépendant qui a cessé son activité sont un sujet de
droit autant que de stockage.

**État du code** : aucune purge. La table grossit indéfiniment.

---

## 10. Opérateur SMS

Le référentiel n'en nomme aucun, et le service Notification est écrit sans
adaptateur réel : `Sms:Provider` vaut `none` en production, et chaque envoi est
consigné en « ignoré ».

Le choix engage plus qu'il n'y paraît : coût par message, couverture effective
de MTN et Moov au Bénin, délai de remise, accusé de réception, et surtout
déclaration de l'expéditeur — un nom d'expéditeur alphanumérique comme « HBA »
demande souvent une autorisation préalable auprès de l'opérateur.

Options usuelles : un agrégateur international (Twilio, Vonage), un agrégateur
régional, ou un accord direct avec chaque opérateur béninois. Le premier est le
plus rapide à brancher et le plus cher au message ; le dernier, l'inverse.

**Proposition en attente de décision** : WhatsApp pour le code de connexion,
avec le SMS en repli, détaillée par l'ADR 0014. Le point NE DISPARAIT PAS pour
autant : le code de remise part vers le destinataire, qui ne peut pas donner
d'opt-in, donc le SMS reste indispensable et son opérateur reste à choisir.

Ce qu'il faut demander à chaque candidat : êtes-vous connecté en direct à MTN,
Moov et Celtiis, ou passez-vous par un tiers ? Et à quel délai de remise
médian ? Aucun acteur ne publie ses tarifs Bénin ni ses taux de remise : le
choix se fera sur mesure, pas sur documentation, en comparant cent envois
réels vers les trois réseaux. La table `notification.sent_notifications` est
déjà faite pour ça.

**État du code** : l'interface `INotificationSender` attend son implémentation.
En développement, `Sms:Provider=log` écrit le message dans les journaux — et le
service refuse ce mode hors développement, puisque les codes y figurent en
clair.

---

## 11. Rétention du journal des envois

`notification.sent_notifications` grossit d'une ligne par message. Il sert à
répondre à « je n'ai pas reçu mon code », à rapprocher la facture de l'opérateur
et à repérer un numéro qui échoue toujours — trois usages à horizon court.

À trancher : au bout de combien de temps purge-t-on ? Le contenu rendu n'est
déjà pas conservé ; restent le numéro, le modèle et l'issue.

**État du code** : aucune purge.

---

## 12. Encaisseur de la recharge

Ouvert par l'ADR 0013. Si les donneurs d'ordre rechargent un compte, le nombre
de transactions s'effondre et l'écart de commission cesse de peser. Reste la
couverture.

- **Option A — agrégateur** (FedaPay, ou un équivalent régional) : une
  intégration, plusieurs opérateurs, une commission.
- **Option B — intégration MTN MoMo directe** : commission négociée, mais les
  clients Moov et Celtiis ne peuvent pas recharger. Chaque opérateur ajouté
  ensuite est un contrat, un jeu de clés, un webhook et un rapprochement de
  plus.

**TRANCHÉ — option A, FedaPay**, septembre 2026. Détaillée par l'ADR 0017.

**État du code** : le service Payment est implémenté. `IPaymentProvider` isole
le fournisseur dans la couche Application ; le parcours est la page de paiement
hébergée, et le webhook signé — dont le verdict est relu par l'API avant d'être
appliqué — est la seule source de vérité. Changer d'agrégateur reste une
implémentation à écrire, pas une modification du domaine.

**Ce que l'intégration a confirmé, et qui pèse sur les points 1 et 2** : chez
FedaPay comme chez MTN MoMo, le payeur valide sur son téléphone avec son code
PIN. Il n'existe ni prélèvement serveur, ni mandat pré-autorisé.

**Reste ouvert** : le remboursement. `RefundPayment` renvoie `UNIMPLEMENTED` —
qui déclenche, sous quel délai, avec ou sans frais, et comment la part du
livreur est reprise quand la course a déjà été faite, n'est décrit nulle part.

---

## 13. La facture normalisée

Ouvert par l'ADR 0013, et il conditionne le postpayé.

Au Bénin, une entreprise assujettie à la TVA délivre des **factures
normalisées** portant un numéro d'identification de machine, une signature
numérique et un code électronique, produites par une machine certifiée (MECeF)
ou par sa version en ligne (e-MECeF). Un système de facturation maison — ce que
serait `Billing` — doit être **agréé par la DGI** avant d'émettre : compte
développeur, plateforme de test, jeu de cas de tests à passer, agrément par
courriel.

Les sanctions annoncées ne laissent pas de marge : dix fois le montant de la
facture au premier manquement, un million de francs au minimum ; vingt fois et
trois mois de fermeture administrative en cas de récidive.

Deux remarques avant de trancher.

D'abord, **l'obligation ne vient pas du postpayé mais de l'assujettissement à
la TVA**. Si HBA y est assujettie, elle concerne aussi les ventes à la course.
Le postpayé ne la crée pas ; il la rend inévitable, parce qu'un commerçant qui
reçoit une facture mensuelle en a besoin pour sa propre comptabilité et
refusera un document non normalisé.

Ensuite, **le prépayé n'en dépend pas pour fonctionner**. Un compte peut être
rechargé et débité sans qu'aucune facture soit émise. C'est ce qui permet de
livrer le prépayé sans attendre l'agrément.

- **Option A — agrément DGI du service `Billing`** comme système de facturation
  d'entreprise. Intégration directe, factures émises par la plateforme.
  Sandbox, jeu de tests, délai d'agrément non maîtrisé.
- **Option B — passer par un éditeur déjà agréé**, et laisser `Billing`
  produire les données. Plus rapide, dépendance externe, coût par facture.
- **Option C — facturer hors ligne au pilote** : peu de comptes postpayés,
  factures émises par l'outil comptable existant à partir d'un export de
  `Billing`. Le code ne porte alors que le calcul, pas l'émission.

**État du code** : rien. Le champ `normalisation` de la table `invoices` est
prévu pour recevoir les références, et reste vide.

**À vérifier avant tout** : HBA Tech et Trade est-elle assujettie à la TVA ?
La réponse conditionne tout le reste, et je ne l'ai pas.

---

## 14 — Moteur de stockage objet — TRANCHÉ

**Déclencheur** : en septembre 2026, MinIO a retiré ses images publiques de
Docker Hub *et* de quay.io. Plus aucun tag n'est tirable anonymement, y compris
les digests épinglés. Ce n'est pas une panne passagère : c'est un changement de
distribution. `make up` échouait dessus.

**Constat qui change la question** : le dépôt ne contient **aucun appel** à
MinIO. Ni code C#, ni section de configuration, ni chaîne de connexion. La seule
trace est un `PackageReference Include="Minio"` dans
`Hba.Driver.Infrastructure`, que rien n'utilise. Le conteneur tournait à vide.

Le bloc est donc **neutralisé, pas remplacé** : choisir aujourd'hui un moteur de
stockage pour satisfaire un conteneur que personne n'appelle, ce serait arbitrer
sans le besoin sous les yeux.

**Ce qui déclenchera la décision** : la première fonctionnalité qui écrit des
binaires — photos de preuve de livraison (`Driver`), pièces justificatives
livreur, catalogue commerçant.

- **Option A — Garage** (`dxflrs/garage:v2.4.1`). Rust, un seul binaire, conçu
  pour l'auto-hébergement sur matériel modeste et liaisons lentes : le profil
  d'un VPS à Cotonou. Demande un `garage.toml` et une étape d'initialisation
  (`layout assign` puis `layout apply`) avant de servir. Pas de console web.
- **Option B — SeaweedFS** (`chrislusf/seaweedfs`). Mode `server -s3` en un seul
  conteneur. Plus simple à démarrer que Garage, mais l'authentification S3
  réclame un fichier de configuration séparé : sans lui, l'accès est anonyme,
  ce qui interdit la production.
- **Option C — stockage objet managé** (Scaleway, OVH, Cloudflare R2).
  Zéro opération, facturé à l'usage, mais les binaires sortent du périmètre
  auto-hébergé et transitent hors UEMOA selon le fournisseur.
- **Option D — système de fichiers + volume**, et rien d'autre tant que le
  pilote de Cotonou tient sur une seule machine. Le moins de pièces mobiles ;
  interdit la mise à l'échelle horizontale sans reprise.

**Conséquence sur le code** : les options A, B et C parlent toutes l'API S3, donc
le choix reste réversible tant que l'accès au stockage passe par un port du
domaine et non par le SDK en direct. L'option D, elle, ne l'est pas.

**Décision** : option A — Garage. Le déclencheur annoncé est arrivé :
l'inscription du livreur dans l'application, qui dépose CNI, permis, carte
grise et photo. Voir l'[ADR 0021](../adr/0021-depot-des-pieces-livreur.md),
qui tranche aussi le chemin des octets et les règles de dossier.

La référence NuGet `Minio` de `Hba.Driver.Infrastructure` est conservée : ce SDK
parle le S3 standard et sert d'adaptateur à Garage.

Ce qui reste ouvert : la rétention des pièces d'identité. Le point 7 couvre les
preuves de livraison et les positions ; les pièces posent la même question et
n'y figurent pas.

---

## 15. Sortir d'une approbation de versement

Ouvert le 27 septembre 2026, par la mise en service de la file des versements.

Une demande de versement suit `Demandée → Approuvée → Versée`, ou
`Demandée → Refusée`. Le refus n'est admis que depuis « demandée », et c'est
volontaire : approuver engage la maison, et laisser défaire un engagement sans
règle écrite revient à ne pas l'avoir pris.

**La conséquence est réelle.** Une demande approuvée dont le virement n'a jamais
lieu — compte mobile money fermé, numéro erroné, opérateur qui refuse, dossier
simplement oublié — reste « en cours ». Elle bloque donc le livreur, qui ne peut
pas en ouvrir une autre : une seule demande vivante à la fois, garantie par un
index unique en base. Aujourd'hui, rien dans le système ne l'en sort. La console
l'affiche en clair sur l'écran de consignation plutôt que de faire semblant.

**Ce qu'il faut décider, et c'est trois questions, pas une :**

1. **Qui défait.** `finance` seule, ou `admin` aussi ? Celui qui a approuvé, ou
   n'importe qui d'autre — c'est-à-dire : veut-on une séparation des rôles entre
   celui qui engage et celui qui annule ?
2. **Vers quel état.** Retour à « demandée », donc le dossier revient dans la
   file comme s'il n'avait jamais été approuvé ? Ou un état « annulée » distinct,
   qui garde la trace de l'approbation et de sa reprise ? Le premier est plus
   simple et efface une décision ; le second est honnête et ajoute un état au
   contrat.
3. **Ce que le livreur en voit.** Un motif lui est-il dû, comme pour un refus ?
   Une approbation reprise sans explication est pire qu'un refus motivé : il a vu
   passer un accord.

**Options :**

- **Option A — admettre le refus depuis « approuvée ».** Une ligne dans
  `PayoutRequest.Reject`, motif obligatoire comme aujourd'hui. Le moins de code.
  Mais « refusée » recouvrirait alors deux faits différents — jamais approuvée,
  et approuvée puis reprise — et le relevé ne les distinguerait plus.
- **Option B — un état `Cancelled` de plus.** Le contrat, le domaine, la console
  et l'écran du livreur bougent. En échange, l'historique reste lisible : on voit
  qu'un accord a été donné puis repris, par qui, et pourquoi.
- **Option C — une échéance sur l'approbation.** Passé N jours sans référence de
  virement, la demande retombe seule dans la file. Aucun geste humain, mais il
  faut choisir N, et une tâche de fond qui modifie des engagements sans que
  personne ne l'ait demandé est le genre de mécanisme qu'on découvre le jour où
  il se déclenche mal.
- **Option D — ne rien faire, et traiter les cas à la main en base.** Ce qui se
  passe aujourd'hui de fait. À écarter : une correction en base ne laisse aucune
  trace imputable, et c'est précisément ce que tout ce chantier évite.

**Ce que ce point ne couvre pas** : l'annulation par le livreur de sa propre
demande, qui n'existe pas non plus et pose une autre question — effacer une
demande effacerait la preuve qu'il l'avait faite.

**Ce que le livreur voit en attendant** : son écran Gains affiche la demande
approuvée avec « accord donné », et lui dit qu'une seule demande est possible à
la fois. Il n'a donc aucun moyen de se débloquer lui-même, et rien à l'écran ne
lui laisse croire le contraire.

---

## 16. Vélo et tricycle ne sont pas des types de véhicule — TRANCHÉ (Option A)

Ouvert le 27 septembre 2026, par la demande d'afficher le véhicule du livreur
sur la carte — une moto, un vélo, un tricycle ou une voiture, comme le fait
Uber.

**Le contrat n'en connaît que trois.** `hba.common.v1.VehicleType` vaut
`UNSPECIFIED`, `MOTORCYCLE`, `CAR`, `VAN`. Ni vélo, ni tricycle. L'application
dessine donc aujourd'hui le pictogramme des trois types qui existent et laisse
le disque nu pour les autres ; les deux lignes manquantes sont écrites en
commentaire, prêtes.

**Ce n'est pas une affaire de pictogramme.** Ce type est une **clé de tarif**
dans Pricing — l'index porte sur `(zone, véhicule, date de validité)`. Un type
de véhicule sans grille tarifaire ne produit pas un prix approximatif : il ne
produit **aucun devis**, donc aucune course. Il filtre aussi les livreurs dans
`FindAvailableNearby`, et il voyage avec la course jusqu'à Delivery.

**Un défaut déjà présent, et qui se réveillerait au premier vélo.** Le dossier
du livreur considère qu'un véhicule est déclaré quand la **plaque** est
renseignée (`vehiculeDeclare => plaque.isNotEmpty`). Un vélo n'a pas de plaque :
un cycliste resterait indéfiniment « véhicule non déclaré », sans rien à
corriger. Le profil, lui, utilise une autre définition — le type, pas la plaque.
Les deux divergent déjà ; un type sans plaque le rendrait visible.

**Ce qu'il faut décider, et c'est quatre questions :**

1. **Les pièces.** Carte grise, permis et photo du véhicule sont exigés
   aujourd'hui. Aucun n'a de sens pour un vélo. Faut-il un jeu de pièces par
   type de véhicule ? C'est une règle de dossier, pas un formulaire.
2. **Le tarif.** Un vélo coûte-t-il moins cher qu'une moto, ou le même prix ?
   Il faut une grille par zone et par type, sans quoi le devis échoue.
3. **Le plafond.** Poids et encombrement maximum par type. Le devis porte déjà
   `packageWeightGrams` ; rien ne l'oppose aujourd'hui au véhicule.
4. **La plaque.** Devient-elle facultative selon le type ? Et alors, qu'est-ce
   qui prouve qu'un véhicule est déclaré ?

**Options :**

- **Option A — ajouter `BICYCLE` et `TRICYCLE` au contrat**, avec leurs grilles
  tarifaires, leurs pièces et leurs plafonds. Complet, et c'est le seul chemin
  si ces véhicules doivent vraiment livrer. Touche Pricing, Driver, Delivery,
  la passerelle et les deux applications.
- **Option B — les traiter comme des variantes de `MOTORCYCLE`**, avec un
  simple libellé d'affichage à côté. Aucun changement de contrat, aucun tarif à
  créer — mais le système croit alors qu'un vélo peut prendre une course de
  vingt kilos à l'autre bout de la ville, et rien ne l'en empêche.
- **Option C — ne rien changer tant que le pilote de Cotonou tourne à la moto.**
  Le tricycle et le vélo apparaissent le jour où un livreur s'inscrit avec.
  C'est l'état actuel, et il est tenable tant que personne n'en a.

**Ce que ce point ne couvre pas** : `VAN` existe dans le contrat mais n'a ni
grille tarifaire vérifiée ni règle de capacité. La même question s'y pose déjà,
en silence.

### Décidé le 30 septembre 2026 — Option A

`VEHICLE_TYPE_BICYCLE = 4` et `VEHICLE_TYPE_TRICYCLE = 5` entrent au contrat.
Les numéros ne se réutilisent ni ne se réordonnent : un message protobuf voyage
par son numéro, et intercaler le vélo en 2 aurait fait lire « vélo » à tout
message déjà écrit qui disait « voiture ».

#### Question 1 — les pièces — TRANCHÉE

**Le vélo n'exige que CNI, photo d'identité et photo du véhicule.** Ni permis ni
carte grise : il n'en a pas, et les exiger fermait ce type de livreur avant même
de l'ouvrir. **Le tricycle n'est pas une exception** — c'est un trois-roues
motorisé, immatriculé comme tel, donc mêmes pièces que la moto.

`DriverAggregate.PiecesRequises` devient `PiecesRequisesPour(vehicule)`. Le défaut
reste la liste complète, et c'est le bon sens du refus : un type qu'on aurait
oublié de traiter se verrait demander **trop** de pièces, pas trop peu — l'erreur
se voit alors au guichet, pas sur la route.

**La photo du véhicule reste exigée du vélo**, et c'est ce qui distingue cette
liste du minimum absolu : sans elle, ops ne voit jamais l'engin et n'a rien pour
le reconnaître le jour d'une réclamation.

La liste exigée **voyage désormais dans le contrat** (`required_documents`) :
l'application affichait les cinq natures en dur, et un cycliste y aurait vu deux
lignes qu'il n'aurait jamais pu satisfaire. La règle est métier ; la recopier
côté application en aurait fait une seconde version.

#### Question 3 — le plafond — TRANCHÉE : aucun

**Rien n'empêche un vélo de recevoir une course lourde.** Le livreur voit le
colis dans l'offre et décline ce qu'il ne peut pas porter. Zéro code, aucun
nombre à décider.

**Ce que cela coûte, et qui a été pesé** : chaque refus fait perdre une vague de
dispatch, donc du temps au client ; et un livreur qui accepte quand même se met
en danger. Le matériau d'un plafond existe des deux côtés et n'est branché nulle
part — `Vehicle.CapacityGrams` et `Delivery.PackageWeightGrams` — le jour où le
terrain montre que c'est nécessaire.

#### Question 4 — la plaque — TRANCHÉE

**Elle reste obligatoire pour tout ce qui a un moteur, et cesse de prouver
qu'un véhicule est déclaré.** `Vehicle.EstDeclare` porte désormais la règle :
un type sans plaque est déclaré par son type seul, les autres par leur plaque.

**Ce qui rend cette règle sûre** : aucun véhicule ne *naît* sans plaque. Le
défaut d'inscription est une moto, qui en exige une ; un type sans plaque ne
peut donc avoir été posé que par une déclaration. Le fait voyage dans le contrat
(`vehicle_declared`) — l'application le devinait, faute de mieux.

#### Question 2 — le tarif — NON TRANCHÉE, ET ELLE BLOQUE L'OUVERTURE

Les montants ne sont pas dans le référentiel : ils viennent de la configuration.
**Mais rien ne permet aujourd'hui de créer une seconde grille.** Vérifié le
30 septembre 2026 :

- `pricing_service.proto` n'expose que `GetQuote`, `GetQuoteById` et
  `ConsumeQuote`. **Aucune route ne crée ni ne modifie une grille**, ni pour ops
  ni pour admin ; la section « Tarification » de la console est un intitulé sans
  écran derrière.
- `InitialTariffSeeder` sème **une seule** grille, depuis
  `Tariff:Initial` — un objet, pas une liste : une zone, un véhicule.

**Conséquence concrète.** Le contrat accepte le vélo, un livreur peut déclarer
son vélo, ops peut valider son dossier — et la première course en vélo échouera
sur `NO_TARIFF`. Le message de ce refus nomme désormais **le véhicule autant que
la zone**, parce que sans cela l'exploitation irait chercher un problème de zone.

**Il reste donc à trancher, avant d'ouvrir les deux types** : par où se créent
les grilles. Une administration des tarifs est une fonctionnalité à part entière
— qui a le droit de modifier une grille, comment on la versionne, à partir de
quand elle s'applique — et elle n'est pas écrite.

---

## 17. Rien n'enregistre qu'un livreur a accepté les conditions

Ouvert le 27 septembre 2026, en écrivant les deux documents juridiques de
l'application livreur.

Les conditions du livreur et la politique de confidentialité s'affichent
désormais **dans** l'application, hors ligne, avec un numéro de version. Ce qui
manque est l'autre moitié : **personne n'accepte rien, et rien n'est conservé**.
Aucune case, aucun horodatage, aucune version acceptée en base.

Le jour où un livreur conteste une règle — une retenue, une suspension, un refus
de versement —, la question posée sera « quelle version avait-il sous les yeux,
et quand l'a-t-il acceptée ». Aujourd'hui la réponse est : on ne sait pas.

**Ce qu'il faut décider :**

1. **Quand.** À l'inscription, avant le premier passage en ligne, ou à la
   validation du dossier ? Les trois moments n'engagent pas la même chose.
2. **Quoi conserver.** La version acceptée, la date, et l'identifiant du
   livreur suffisent. Où : dans Identity avec le compte, ou dans Driver avec le
   dossier ?
3. **Ce qui se passe à la version suivante.** Un livreur qui n'accepte pas la
   nouvelle version peut-il continuer à travailler ? Le bloquer un matin sans
   prévenir serait le pire moment ; ne rien faire vide l'acceptation de son sens.

**Deux autres points restent ouverts et sont écrits dans les documents
eux-mêmes**, plutôt que comblés par une formule : la nature du lien entre HBA et
le livreur (indépendant, prestataire, salarié), et la raison sociale exacte avec
le point de contact chargé des données personnelles.

**Les deux textes portent un bandeau « TEXTE PROVISOIRE » dans l'application.**
Il disparaît quand `brouillon` passe à faux dans `DocumentLegal` — et ce n'est
pas une correction de code : c'est la décision de l'entreprise que le texte a
été relu.

---

## 18 — Le son d'offre et son réglage — TRANCHÉ

Le signal d'arrivée d'une offre jouait dans le **canal des notifications**. Un
livreur qui met son téléphone en sourdine — geste ordinaire — coupait donc sans
le savoir le seul avertissement qui existe. Constaté sur l'appareil de test :
aucun son, aucune vibration, l'offre s'ouvrait et expirait en silence.

Le son est passé au **canal des alarmes**, qui survit à la sourdine, et la
vibration à un vrai moteur de vibration plutôt qu'au retour tactile de
l'interface. Le signal fonctionne désormais dans les deux situations.

**Ce que cela crée en retour :** un livreur en ligne ne peut plus obtenir le
silence autrement qu'en se mettant hors ligne.

**Décidé le 27 septembre 2026 :**

1. **Le canal des alarmes est confirmé.** Se mettre en ligne vaut demande à être
   dérangé. C'est le seul canal qui survive à la sourdine et qui ne se mette pas
   en pause pendant un guidage vocal, c'est-à-dire pendant que le livreur roule.
2. **Un interrupteur unique dans Profil : « Son à l'arrivée d'une offre ».**
   Quand il est éteint, **la vibration reste**. Il n'y a pas de troisième état,
   et c'est le cœur de la décision : couper les deux reviendrait à se rendre
   injoignable sans le savoir — un livreur en ligne qui ne reçoit plus rien
   conclut que l'application est cassée, pas qu'il a éteint son propre signal.
   Le sous-titre de la ligne dit lequel des deux reste.
3. **Le réglage vit sur le téléphone**, dans le coffre déjà utilisé par la file
   d'actions (`hba.driver.son_offre`). Aucun attribut ajouté au livreur, aucune
   route, aucune migration. Conséquence assumée : changer d'appareil remet le
   son à « actif ».

**Implémenté :** `apps/driver_app/lib/core/preferences.dart` (`sonDOffreProvider`),
la carte « Réglages » dans Profil, et `Signal.offre(avecSon:)`.

**Le défaut est « actif », et il est rendu avant la lecture du coffre** — la
lecture est asynchrone, l'état doit exister tout de suite. Se tromper dans un
sens fait entendre un son à un livreur qui l'avait coupé ; dans l'autre, fait
manquer une course. On se trompe du côté bruyant, et l'écran d'accueil réveille
le réglage dès son `initState` pour que la fenêtre ne dure que le démarrage.

---

## 19. Le destinataire et le contact du lieu de livraison sont-ils la même personne

Sur l'écran de course, le livreur voyait **deux fois le même nom et le même
numéro** : une fois dans la carte « Livraison », une fois dans la carte
« Destinataire ». Demande a été faite de fusionner les deux.

**Ce sont deux champs distincts du contrat, et la distinction n'est pas
décorative.** `hba.common.v1.Location.contact_name` est documenté comme « nom de
la personne présente sur place » — un gardien, une secrétaire, le comptoir d'une
boutique, quelqu'un qui descend ouvrir un portail. `recipient_name` est la
personne à qui le colis revient. Les deux diffèrent dès qu'une livraison se fait
dans une entreprise, une école, une administration — ou simplement quand le
client fait livrer chez quelqu'un d'autre.

**Ce qui a été fait, qui ne tranche rien :** la carte « Destinataire » ne
s'affiche plus quand ses deux champs sont **exactement** identiques à ceux du
lieu de livraison ; elle ne garde alors que la description du colis, sous le
titre « Colis ». La comparaison est exacte, sans normalisation des numéros :
rapprocher « +229 01 97 … » de « 0197… » demanderait de décider ce qu'est le
même numéro au Bénin, et se tromper cacherait une personne qui n'est pas le
contact sur place. En cas de doute, les deux s'affichent.

**Ce qu'il reste à décider :**

1. **Le client saisit-il les deux, ou un seul ?** Aujourd'hui rien dans le
   parcours de commande ne dit lequel est demandé, ni si l'un recopie l'autre
   par défaut. Tant que ce n'est pas tranché, les données de test ressembleront
   à un doublon et les données réelles à un désordre.
2. **Si un seul est saisi, lequel disparaît du contrat.** Supprimer
   `Location.contact_name` du point de livraison, ou supprimer
   `recipient_name` — les deux ont des conséquences sur la preuve de remise :
   le code OTP est dicté par le destinataire, pas par le gardien.
3. **Qui le livreur appelle en premier.** L'écran met aujourd'hui le contact du
   lieu en avant, parce que c'est lui qui ouvre la porte. Si la règle métier est
   l'inverse, l'ordre des deux cartes doit changer.

**Rien n'est fusionné dans le contrat ni dans le code.**

---

## 20. La vie d'un livreur après la validation de son dossier

Un livreur change de moto. Son permis arrive à échéance. Sa carte grise change
de propriétaire. **Aujourd'hui le système n'a de parcours pour aucun de ces
trois cas**, et ce n'est pas un oubli d'écran : c'est une règle de domaine
explicite.

**Ce que le code dit, mot pour mot.** `DriverAggregate.DossierModifiable` ne
vaut que pour `PendingVerification` et `Rejected`. `AttachDocument` et
`DeclareVehicle` appellent tous deux `ExigerDossierModifiable()`, qui refuse
avec « Ce dossier est valide et ne se modifie plus. Contactez HBA pour le
rouvrir. » Le commentaire donne la raison, et elle est bonne : « Laisser
remplacer une CNI après validation reviendrait à valider une personne et à en
laisser travailler une autre. »

**Ce qui fonctionne déjà**, et qui répond à la moitié de la demande : tant que
le dossier est en attente ou rejeté, l'application laisse **déjà** changer le
type de véhicule, l'immatriculation, et redéposer n'importe quelle pièce. Rien
n'est à construire pour ces états.

**Ce qui n'existe nulle part : la date d'expiration d'une pièce.**
`DriverDocument` ne porte que `UploadedAt`. Le `readUrlExpiresAt` rendu par la
passerelle est l'expiration de l'URL signée, pas celle du permis. Le référentiel
acteurs ne mentionne pas non plus de date de validité dans les données du
livreur. **Le système ne peut donc pas savoir qu'une pièce est périmée**, ni le
signaler, ni bloquer un livreur en conséquence.

**Ce qui a été fait, qui ne tranche rien :** les deux endroits où le geste
échoue — la carte « Mon véhicule » et la liste des pièces — donnent désormais
le chemin qui existe déjà : « Écrire à HBA » et « Appeler HBA », avec un objet
pré-rempli (« Changement de véhicule - <id> », « Renouvellement d'une pièce -
<id> ») qui se trie à l'œil côté support. Jusqu'ici l'écran disait « contactez
HBA » sans numéro, sans adresse et sans bouton.

**Ce qu'il faut décider — changement de véhicule :**

1. **Est-ce une réouverture complète ou un parcours à part ?** Suspendre le
   livreur pour qu'il change de plaque l'empêche de travailler pendant
   l'examen, ce qui est cher pour un changement de moto ; ne rien suspendre
   laisse rouler quelqu'un dont la carte grise ne correspond plus.
2. **Le changement de TYPE est-il le même cas que le changement de plaque ?**
   Passer de moto à voiture change ce que le dispatch peut lui proposer et la
   capacité annoncée au client. Changer de moto pour une autre moto ne change
   rien pour personne, sauf pour ops qui compare la plaque.
3. **Quel état du dossier pendant l'examen du nouveau véhicule ?** Il faudrait
   probablement un état qui n'existe pas encore — travailler avec l'ancien
   véhicule validé pendant qu'on examine le nouveau.

**Ce qu'il faut décider — pièces et expiration :**

4. **Ajoute-t-on une date de validité à `DriverDocument` ?** Elle n'est pas dans
   le référentiel : l'y ajouter est une modification du référentiel, pas une
   décision d'implémentation. Sans elle, aucune relance n'est possible.
5. **Qui la saisit ?** Le livreur au dépôt (déclaratif, donc faux quand ça
   l'arrange), ops à l'examen (fiable, mais du travail manuel à chaque pièce),
   ou une lecture automatique (hors de portée aujourd'hui).
6. **Que se passe-t-il à l'échéance ?** Prévenir le livreur X jours avant,
   suspendre automatiquement le jour J, ou laisser ops décider. Une suspension
   automatique un matin, sans prévenir, est le pire moment possible ; ne rien
   faire vide la date de son sens.

**Rien n'est implémenté sur ces six points.** L'application ne prétend pas
davantage qu'avant : elle dit seulement à qui s'adresser.

---

## 21 — Le livreur en ligne, écran éteint — TRANCHÉ (chemin A)

Un livreur en ligne qui range son téléphone **cesse d'exister pour le dispatch au
bout de 120 secondes** : le service Driver écarte des recherches toute position
plus vieille que `DriverLocations:FreshnessSeconds`, et l'application n'envoie de
position que pendant que l'écran d'accueil est ouvert.

**Le piège, et il a failli coûter un chantier entier :** on croit que le
problème est d'être *joignable*, donc qu'il faut une notification poussée. Il
est d'abord d'être *éligible*. Sans position fraîche, il n'y a aucune offre à
pousser — le livreur n'est pas injoignable, il n'existe plus pour le moteur.

**Décidé le 27 septembre 2026 : chemin A.** Un service Android de premier plan,
démarré au passage en ligne et arrêté au passage hors ligne, qui tient la
position à jour depuis son propre isolat. Pas de notification poussée pour
l'instant.

**Ce que cela implique, et qui est assumé :**

- Une **notification permanente** est obligatoire pour ce type de service. Ce
  n'est pas un inconvénient : elle dit « vous êtes en ligne », ce qui est le
  remède direct au point 1.1 du premier audit — l'application qui ment sur son
  propre état.
- Un service de premier plan de type `location` **démarré alors que
  l'application est au premier plan n'exige pas `ACCESS_BACKGROUND_LOCATION`**.
  Il demande `FOREGROUND_SERVICE` et `FOREGROUND_SERVICE_LOCATION`, plus un
  formulaire de déclaration depuis Android 14 — mais pas la revue manuelle
  « position en arrière-plan » du Play Store, qui est la plus lourde.
- Le **sondage continue**, en permanence tant que le livreur est en ligne. La
  facture du point 2.1 de l'audit s'applique alors sans interruption. C'est le
  prix du chemin A, et c'est pourquoi `make mesure-attente` doit tourner avant
  qu'on en discute.

**Pourquoi pas le chemin B** (service de premier plan + notification poussée) :
quatre pièces au lieu d'une — le service, un registre de jetons d'appareil qui
n'existe nulle part, un émetteur Push côté Notification, et un pont
`OfferSent` → commande de notification. Le contrat et le consommateur existent
déjà (`NotificationCommand.send_push`, `NotificationChannel.Push`), le reste non.
B reste ouvert, et redeviendra intéressant le jour où la consommation mesurée le
justifiera.

**Ce qui reste à trancher :**

1. **Le référentiel doit le dire.** Il ne mentionne pas le suivi en
   arrière-plan ; le manifeste le notait déjà : « tant qu'il ne le tranche pas,
   on ne le demande pas. » Cette décision doit y remonter, sinon le code ira
   plus loin que la référence unique du projet.
2. **La mise hors ligne automatique.** Au bout de combien de temps sans
   mouvement, sans course acceptée ou sans interaction repasse-t-on un livreur
   hors ligne ? Sans règle, on proposera des courses à quelqu'un qui dort, et on
   videra sa batterie pour rien.
3. **iOS.** Il n'a pas d'équivalent au service de premier plan : ce serait
   `UIBackgroundModes: location`, l'indicateur bleu permanent, et APNs. À savoir
   s'il entre dans le périmètre de la v1.
4. **Ce que la politique de confidentialité doit dire.** J'ai d'abord écrit ici
   qu'elle deviendrait fausse. **C'est l'inverse.** Elle dit : « Lorsque vous
   passez EN LIGNE, l'application envoie votre position toutes les vingt
   secondes. Lorsque vous passez hors ligne, elle cesse immédiatement. » Elle ne
   parle ni d'écran, ni de premier plan — et c'est **aujourd'hui** qu'elle est
   optimiste, puisque l'envoi s'arrête en réalité dès que le livreur quitte
   l'écran d'accueil. Le service de premier plan la rend vraie.

   Il reste qu'un livreur mérite que ce soit dit en toutes lettres : la position
   part **même écran éteint et application fermée**, et une notification
   permanente le lui rappelle. Ces deux phrases doivent entrer dans le document
   — et dans la version, qui est encore en `v0.1` — **avant** que le service ne
   démarre chez quelqu'un.

---

## 22 — Supprimer un compte client ou livreur — TRANCHÉ sur le principe

**Une promesse est déjà faite, et rien ne peut la tenir.** L'écran « Supprimer
mon compte » de l'application livreur affiche aujourd'hui, en production :

> Ce qui sera supprimé : votre compte, votre profil livreur et vos pièces
> justificatives. (…) Ce qui sera conservé : les courses déjà effectuées
> restent dans l'historique de HBA (…) **Votre nom en est retiré.**
>
> DEMANDE, PAS SUPPRESSION IMMÉDIATE — votre demande part vers l'équipe HBA,
> qui la traite et vous confirme la suppression.

Cette demande part **par courriel**, et **aucun écran, aucune route, aucune
table ne la reçoit**. Le livreur écrit, et le système ignore qu'il a écrit.

**À corriger d'abord dans la tête : les fiches, elles, existent.** La console a
`clients/page.tsx` (identifiant, téléphone, e-mail, inscription, adresses,
cumul facturé, journal des accès) et `livreurs/page.tsx` (téléphone, véhicule,
plaque, inscription, validation, motif, dossier KYC, activité, validation et
suspension). Ce qui manque n'est pas la consultation.

### Décidé le 27 septembre 2026

1. **Supprimer veut dire ANONYMISER.** Compte fermé, pièces KYC effacées,
   courses conservées sans le nom. Ce n'est pas un choix neuf : c'est le texte
   déjà affiché au livreur. En choisir un autre obligeait à réécrire ce texte
   d'abord.
2. **Par une FILE DE DEMANDES**, pas par un bouton. L'application annonce une
   demande traitée puis confirmée ; un bouton d'administrateur rendrait cette
   phrase fausse.
3. **`admin` seul.** Le référentiel réserve déjà à `admin` seul le journal des
   accès aux données personnelles et le TOTAL facturé. Une suppression est
   irréversible : même cercle. `support` reçoit la demande, `admin` l'exécute.

### Ce que « retirer le nom » veut dire, et où

**Aucune notion d'anonymisation n'existe dans le code — j'ai cherché dans les
cinq services.** Un nom de personne vit aujourd'hui à quatre endroits au moins :

| Service | Où | Ce qu'il faut en faire |
|---|---|---|
| Identity | `Account.DisplayName`, téléphone, e-mail | Fermer le compte, effacer les identifiants de connexion |
| Directory | `Customer` — nom, téléphone, adresses favorites | Anonymiser ; les adresses favorites sont des données personnelles à part entière |
| Driver | `DriverAggregate` — nom, téléphone, véhicule, **pièces dans le stockage objet** | Anonymiser, et effacer les objets |
| Delivery | `AssignedDriver`, `Location.contactName` / `phone`, destinataire | **Le point le plus délicat** : ces champs sont dupliqués sur chaque course, et une course concerne AUSSI le client et le commerçant |

C'est donc une **saga**, pas un DELETE : une demande approuvée publie un
événement, chaque service anonymise sa part et confirme, et la demande ne passe
à « faite » que lorsque tous ont répondu. Un service muet doit se voir.

### Ce que cela NE tranche pas

- **La purge des pièces KYC**, que le référentiel laisse explicitement ouverte :
  « supprimer une carte d'identité veut dire effacer l'objet dans le stockage,
  la ligne en base, et décider ce que devient un livreur validé dont les pièces
  ont disparu — reste-t-il validé ? » Une suppression de compte tranche le cas
  facile (le livreur s'en va) ; elle ne tranche pas la purge par ancienneté.
- **Ce qui BLOQUE une suppression.** Une course en cours, une demande de
  versement ouverte, un **solde non versé**. Supprimer un livreur à qui HBA
  doit de l'argent est un problème comptable avant d'être un problème technique.
- **Le côté client.** Aucun texte n'a été montré à un client, contrairement au
  livreur : la politique de confidentialité de l'application cliente devra dire
  la même chose avant qu'un client puisse demander quoi que ce soit.
- **Le délai.** Combien de temps entre la demande et l'exécution, et le livreur
  peut-il se rétracter ? La demande de versement, elle, a déjà tranché
  l'inverse (point 15).

### L'ordre de construction

1. **La demande** — un agrégat dans Identity, qui possède le compte. Les routes
   pour la déposer (livreur, client) et pour la lister, l'approuver, la refuser
   (`admin`). L'application livreur cesse d'envoyer un courriel. **C'est la
   moitié manquante de la promesse, et elle se tient seule.**
2. **L'écran** dans la console, à côté des versements, dont il reprend la forme :
   une file, une décision motivée, une trace.
3. **L'anonymisation**, service par service, derrière l'événement — Identity,
   Directory, Driver, puis Delivery, le plus délicat en dernier.
4. **Les pièces du stockage objet**, une fois la question de la rétention
   tranchée.

**Rien n'est implémenté.** Les trois décisions ci-dessus fixent la forme ; les
quatre questions ouvertes doivent l'être avant l'étape 3.

---

## 23 — Un livreur à la fois, le plus proche d'abord — TRANCHÉ

**Constaté sur le terrain :** deux livreurs de la même zone reçoivent la même
course en même temps, et c'est le plus rapide à appuyer qui l'emporte.

**La cause, et elle n'était pas une décision.** `Dispatch:DriversPerWave` valait
**5** : chaque vague diffusait la course à cinq livreurs d'un coup. Le point 5 a
tranché *le nombre de vagues, les rayons et le délai* — **jamais la largeur de
la diffusion**. Le 5 est arrivé dans le code sans discussion.

### Décidé le 28 septembre 2026

**Un seul livreur à la fois, le plus proche d'abord ; la recherche s'arrête
quand tout le périmètre a été balayé.**

Deux raisons, et la seconde pèse plus que la première :

1. Chaque offre immobilise un livreur trente secondes. En solliciter cinq pour
   une course en gèle quatre pour rien, et les courses voisines n'ont plus
   personne.
2. **La course revenait à celui qui tape le plus vite, pas au plus proche.** Le
   client attendait donc un livreur plus loin que nécessaire, et celui qui était
   à côté perdait une course qui lui revenait. Les candidats arrivent déjà
   triés par distance : il suffisait de ne plus les mettre en concurrence.

### Le défaut de structure trouvé en corrigeant, et qui comptait davantage

Ramener `DriversPerWave` à 1 **sans toucher au reste aurait empiré la
situation.** Le rayon se lisait dans le tableau **à l'indice de la vague** :

```
var rayon = _options.RadiusForWave(dispatch.CurrentWave + 1);
if (rayon is null) { dispatch.Exhaust(...); }
```

Passé la troisième vague il n'y avait plus de rayon, donc plus de recherche.
**Le nombre de tentatives était borné par la longueur d'un tableau de
distances.** Un livreur à la fois aurait donc donné **trois livreurs sollicités
au total**, puis `NO_DRIVER_FOUND` — même avec douze livreurs disponibles à deux
kilomètres et aucun sollicité.

Corrigé : on demande au **premier anneau qui rend encore quelqu'un**. Les déjà
sollicités sont exclus côté Driver, donc un anneau épuisé ne rend plus rien et
l'on passe au suivant tout seul. La recherche s'arrête quand le plus large des
anneaux est vide. **La liste borne la recherche, plus le compteur.**

`RadiusForWave` disparaît : plus personne ne l'appelle.

### Ce que cela révoque, et qu'il faut assumer

**Les quatre-vingt-dix secondes du point 5 ne tiennent plus.** Ce point les
justifiait ainsi : « quatre-vingt-dix secondes est le plus long qu'un client
accepte d'attendre sans croire que l'application est bloquée ». Avec un livreur
toutes les trente secondes, dix livreurs dans le périmètre font **cinq
minutes**.

C'est le choix qui a été fait — la liste borne, pas le temps — et il a une
conséquence à traiter ailleurs : **l'application cliente doit dire ce qui se
passe pendant ce temps.** Un écran qui cherche en silence pendant cinq minutes
sera fermé avant la fin.

### Les trois questions ouvertes, tranchées le 28 septembre 2026

**1. Le délai par offre reste à trente secondes.** L'argument pour quinze — un
livreur seul en lice décide plus vite — suppose qu'il regarde son téléphone.
Celui qui conduit, qui remet un colis ou qui traverse un carrefour ne décide pas
plus vite parce qu'il est seul : il décide quand il peut s'arrêter. Quinze
secondes n'auraient pas accéléré sa réponse, elles l'auraient éliminé — et
c'est précisément le livreur le plus proche, donc le meilleur candidat, qu'on
aurait éliminé le plus souvent. Le gain était un compteur, la perte un livreur.

**2. Le plafond est un nombre de livreurs, pas une durée.**
`Dispatch:MaxDriversSolicited = 10`. Un plafond de temps mesure la patience du
client ; un plafond de livreurs mesure ce qu'on a réellement essayé. Les deux
donnent aujourd'hui le même chiffre — dix livreurs à trente secondes font cinq
minutes — mais ils ne vieillissent pas pareil : le jour où le délai passe à
vingt secondes, le plafond en durée changerait silencieusement le nombre de
livreurs atteints, alors que le plafond en nombre garde son sens. Et le sens est
clair : **si dix livreurs ont laissé passer la course, le problème n'est pas le
onzième.**

Le plafond est vérifié **avant** d'interroger Driver, et il produit son propre
message de journal — « plafond de 10 livreurs sollicités atteint » — distinct de
« périmètre épuisé ». Les deux abandons se ressemblent dans la base et ne se
ressemblent pas du tout à l'exploitation : l'un dit qu'il n'y a personne, l'autre
que personne ne veut. Un seul message pour les deux aurait rendu la question
impossible à poser.

Une validation au démarrage refuse `MaxDriversSolicited < DriversPerWave` : la
configuration qui abandonne avant la première vague ne doit pas démarrer.

**3. Le client voit le temps écoulé et une phrase qui explique.** Écran de suivi,
statut `searchingDriver` : un compteur qui avance à la seconde, et « HBA contacte
les livreurs un par un, du plus proche au plus éloigné ».

**Le compteur annonce ce qu'il mesure, et ce n'est pas la recherche.** Le client
ne reçoit que `createdAt` : la date de la commande. Le libellé dit donc
« Commandée il y a 2 min 05 », pas « Recherche depuis 2 min 05 ». L'écart est de
quelques secondes aujourd'hui — celles du paiement — et personne ne le
remarquerait ; c'est exactement ce qui rend l'approximation dangereuse, parce
qu'elle survivra au jour où l'écart grandira.

*Reste ouvert, mineur :* exposer côté client l'instant d'ouverture du dispatch
permettrait d'afficher la vraie durée de recherche. Tant que ce n'est pas fait,
le libellé reste celui de la commande.

## 24 — Le client voit les livreurs autour de lui — TRANCHÉ (révisé le 30/09/2026)

**Demandé le 28 septembre 2026** : une carte d'accueil dans l'application
cliente, montrant les livreurs alentour comme la carte de l'application
livreur.

### La contradiction, signalée avant d'écrire quoi que ce soit

**Le référentiel ne dit rien des positions des livreurs côté client.** La
matrice de visibilité accorde au client le téléphone du livreur *pendant la
mission*, l'adresse de destination, l'OTP de remise. Elle est **muette** sur
les positions — et le silence n'est pas une autorisation, la consigne de
production étant : « N'invente pas d'acteur, d'attribut ou de règle qui n'y
figure pas. Si une information manque, signale-la au lieu de la supposer. »

Trois options ont été posées : ne rien afficher, des pastilles anonymes sans
nombre exact, ou les positions réelles.

### Décidé : les positions réelles

**Ce que cela apporte.** Un client qui voit trois livreurs à deux rues de chez
lui commande. Un écran vide, ou une carte sans personne dessus, laisse croire
qu'il n'y a personne — et sur un marché où l'application est jeune et le
maillage encore mince, cette impression coûte des courses réelles.

### Ce que cela coûte, et qu'il faut assumer

**La position d'un livreur est la donnée personnelle d'un indépendant, pas une
donnée de l'entreprise.** L'afficher à tout titulaire de compte client signifie
que n'importe qui peut ouvrir l'application et regarder les livreurs se
déplacer, sans commander, sans se justifier, aussi longtemps qu'il veut.

Trois conséquences qui ne se rattrapent pas après coup :

1. **La politique de confidentialité de l'application livreur doit le dire.**
   Elle annonce aujourd'hui « en ligne → position envoyée » sans préciser À QUI.
   Tant qu'il ne s'agissait que du dispatch et du back-office, la formule
   passait. Elle devient fausse le jour où des clients la voient.
2. **Les livreurs doivent l'apprendre autrement que par surprise.** Un livreur
   qui découvre que sa position est publique aux clients apprend en même temps
   qu'on ne le lui avait pas dit.
3. **Le grain d'affichage est un choix, pas un détail.** Une position au mètre
   près suit un livreur jusque devant chez lui.

### La limite qui n'avait PAS été levée — et le type de véhicule l'est depuis le 30/09/2026

**Ce qui tient toujours** : aucune **identité** avant `DRIVER_ASSIGNED`. Pas
d'identifiant, pas de nom, pas de plaque. Un identifiant stable suffirait à
suivre le même livreur d'un jour sur l'autre, ce qui est exactement ce que la
matrice refuse. C'est la raison pour laquelle la mise en forme se fait **à la
passerelle** : `FindAvailableNearby` rend des identifiants parce que le dispatch
en a besoin, et c'est à la frontière du client qu'ils doivent disparaître.

**Ce qui a changé** : la route rend désormais **le type de véhicule** avec
chaque position, pour que la carte du client montre une moto, un vélo, un
tricycle ou une voiture — et non quatre pastilles identiques.

#### Ce que cela apporte

Un client qui voit une voiture sait qu'un colis encombrant peut partir ; un
client qui ne voit que des vélos le sait aussi. L'information servait déjà,
en moins précis, par le bandeau « N livreurs autour de vous » et son
pictogramme de moto — qui affirmait « moto » pour tout le monde, ce qui était
faux dès l'ouverture du vélo et du tricycle.

#### Ce que cela coûte, et qui a été pesé avant d'écrire

**Un type de véhicule est semi-identifiant.** Il prend cinq valeurs, et quatre
sont rares à Cotonou : un tricycle au milieu des motos se repère. Suivi d'un
jour sur l'autre au même endroit, il permet de singulariser un livreur sans
jamais lire son nom — c'est-à-dire d'approcher ce que l'interdiction de
l'identifiant stable voulait empêcher.

**Cela ne rend pas l'identifiant, et c'est ce qui borne le risque.** Deux
livreurs à moto restent indistinguables, et rien ne relie une position du jour
à une position de la veille.

**La politique de confidentialité de l'application livreur doit le dire.** Elle
devait déjà annoncer à qui la position est envoyée (conséquence 1 ci-dessus,
toujours ouverte) ; elle doit maintenant dire que le véhicule part avec.

### Ce qui reste ouvert

1. **Le grain.** Position exacte, ou arrondie à un pâté de maisons ? La
   décision porte sur « les positions réelles » ; le nombre de décimales n'a
   pas été discuté.
2. **La fraîcheur et la cadence de rafraîchissement** côté client, qui décident
   du coût en données pour le client et en charge pour Driver.
3. **Le plafond du rayon et du nombre** rendus par la route, sans quoi une
   requête suffit à cartographier une ville.
4. **La politique de confidentialité du livreur**, qui n'annonce toujours ni à
   qui sa position est envoyée, ni que son type de véhicule part avec. Les deux
   sont des faits, pas des intentions : ils sont dans le code.

---

## 25 — Le client crée sa fiche quand l'inscription ne l'a pas fait — TRANCHÉ

### La contradiction, signalée avant d'écrire quoi que ce soit

`IdentityEventsConsumer.cs` porte cette phrase depuis l'origine : « C'est le
**seul** chemin de création d'un profil client : aucune demande de livraison ne
doit en fabriquer un au passage. » Ouvrir une route qui crée la fiche depuis
l'application cliente la contredit de front. Elle a été signalée, et la décision
prise en connaissance de cause.

### Le fait qui a forcé la question

Le compte `01a0e4d9-…` existe dans Identity depuis le 27 septembre 21:51:24.
L'événement `AccountRegistered` a été **posé dans l'outbox et publié**, sans
erreur, 0 tentative. Et pourtant Directory n'a aucune fiche. Le client se
retrouve avec un compte valide, des livraisons qui fonctionnent, un écran de
profil qui ne sait que s'excuser, et **aucune adresse favorite possible** — les
quatre routes d'adresses chargent la fiche avant d'appliquer.

Kafka ne rejoue pas un message pour un groupe de consommateurs qui n'existait
pas quand il est passé. Un service arrêté au mauvais moment perd donc
l'événement **définitivement**. Ce n'est pas un incident rare : c'est le
comportement normal du transport.

### Décidé le 28 septembre 2026 : une route de rattrapage, explicite

`POST /api/client/v1/me` → `DirectoryService.EnsureCustomer`.

Quatre propriétés la rendent acceptable, et les défaire reviendrait à annuler la
décision :

1. **Elle ne prend aucun champ.** `EnsureCustomerRequest` est vide et doit le
   rester. Le nom, le téléphone et le courriel sont lus par Directory dans le
   jeton qu'il a **lui-même validé** — jamais dans un corps de requête, jamais
   sur la parole de la passerelle. Rien n'est forgeable par l'appelant.
2. **Elle est explicite.** C'est un geste du client sur un bouton, pas un effet
   de bord d'une lecture ni d'une demande de livraison. La règle citée plus haut
   visait précisément l'effet de bord ; elle tient toujours.
3. **Elle est idempotente.** Une fiche déjà présente est rendue telle quelle.
4. **Elle journalise en AVERTISSEMENT.** Chaque ligne est un profil que
   l'événement aurait dû créer. Si elles se multiplient, c'est la chaîne
   Identity → Kafka → Directory qu'il faut réparer, pas ce rattrapage qu'il faut
   élargir.

Un livreur ou un commerçant ne peut pas s'en servir : le rôle `customer` est
exigé. Savoir si un livreur peut aussi être client reste ouvert, et cette route
ne le tranche pas à la place de la décision.

### Ce que cela ne dit pas

**Pourquoi l'événement s'est perdu n'est toujours pas établi.** La route rend le
client autonome ; elle ne remplace pas le diagnostic. `make fiche` lit
maintenant l'inbox de Directory, qui distingue « jamais reçu » de « reçu, marqué
traité, jamais rejoué » — deux causes, deux réparations.

### Le défaut trouvé en chemin, et qui comptait autant

`Address.Create` (Directory) **exige** un téléphone valide, un nom de contact et
un repère écrit : c'est l'objet-valeur d'un point de **collecte**, où quelqu'un
attend sur place. L'application cliente envoyait `phone: ''` et
`contactName: ''`, avec un commentaire affirmant que ce n'était pas un oubli.
L'ajout d'adresse favorite ne pouvait donc **pas** fonctionner, même avec une
fiche — et le commentaire expliquait pourquoi c'était normal.

Décidé : la passerelle reprend le téléphone et le nom **du compte**, lus dans le
jeton, quand l'application ne les fournit pas. C'est vrai — c'est son adresse,
c'est lui le contact — et le corps garde la main pour « chez maman ». Le repère,
lui, devient obligatoire **dans le formulaire** : l'échec arrive avant l'appel
réseau, pas après.

**La correction de fond n'est pas faite** : elle serait de donner à Directory un
objet-valeur propre à l'adresse favorite, sans contact obligatoire. Elle touche
le domaine, la base et le contrat, et n'a pas été décidée.

---

## 26 — Les textes légaux de l'application cliente — RÉDIGÉS EN BROUILLON

Le 28 septembre 2026, l'application cliente a reçu ses deux documents :
`conditions_client.dart` et `confidentialite_client.dart`, affichés hors ligne
par le même écran que ceux du livreur — `DocumentLegal` et `DocumentScreen` ont
quitté `driver_app` pour `hba_ui`, où les deux applications les atteignent.

**Ils portent le drapeau `brouillon`, donc le bandeau « non relu ».** Le passer à
faux est une décision de l'entreprise, pas une correction de code : une
politique de confidentialité est un engagement sur ce qu'on fait des données,
des conditions sont un contrat, et rien de tout cela n'a été relu par un
juriste.

### Ce qu'ils disent, et qui est vérifiable dans le dépôt

Le prix figé à la confirmation (le devis est consommé), le code de remise jamais
transmis au livreur, l'impossibilité d'un débit sans validation du payeur
(aucun prélèvement serveur n'existe chez FedaPay), les coordonnées seules tant
qu'aucun livreur n'est attribué (point 24), la position envoyée seulement à
l'ouverture de la carte et au-delà de cinq cents mètres de déplacement.

**Ce qu'ils ne disent PAS, et c'est délibéré :** l'application cliente n'envoie
aucun rapport de plantage. Le document du livreur a une section Crashlytics ;
recopier la section aurait déclaré à Google une transmission qui n'existe pas.

### Les décisions que la rédaction a mises au jour

Chacune est écrite dans le document comme un point non arrêté, visible du
client, plutôt que comblée par une formule :

1. **Le remboursement** — délai, canal, qui décide. Aucune règle appliquée.
2. **L'annulation** — frais, remboursement, et à partir de quand elle est
   tardive. Le système enregistre l'annulation et n'en tire rien.
3. **La responsabilité** en cas de perte ou de dommage, et son plafond. Aucune
   assurance n'est adossée aux courses.
4. **La liste des objets interdits**, et ce qui se passe quand un colis en
   contient. Aucun contrôle de contenu.
5. **Les durées de conservation**, comme côté livreur : le mécanisme de purge
   existe, aucun délai n'y est inscrit.
6. **La raison sociale, le siège et le contact « données personnelles »**, qui
   doivent figurer dans la politique et n'y sont pas.
7. **L'acceptation d'une nouvelle version** : aucune n'est demandée ni
   conservée, pour le client comme pour le livreur.

### Le consentement WhatsApp, lui, n'était pas un point à trancher

C'était un manque. Le domaine d'Identity le portait depuis le 25 septembre —
colonne, événement auditable, `CanReceiveWhatsApp` qui décide déjà du canal du
code de connexion — sans aucune commande pour l'appeler. `GetWhatsAppConsent` et
`SetWhatsAppConsent` (Identity), `GET`/`PUT /api/client/v1/me/whatsapp`
(passerelle) et un interrupteur dans le profil comblent le trou. Explicite : le
défaut reste faux. Révocable : le même appel retire le consentement.

---

## 27 — Un service Media pour tous les binaires — TRANCHÉ

**Décidé le 28 septembre 2026.** Les photos, les pièces de dossier, les PDF et
les futures factures passent par un service dédié : **Media**.

### Ce qui a forcé la question

`DocumentEndpoints.cs` portait cette phrase depuis sa création : « LA SEULE
ROUTE HTTP INTERNE DU DÉPÔT […] Si une deuxième route interne en HTTP se
présente, ce n'est plus une exception mais une tendance, et l'ADR 0015 devra
être rouvert. » La photo de profil du client était exactement cette deuxième
route.

Trois issues étaient possibles : assumer la tendance et réécrire l'ADR ; signer
des URL d'écriture pour que le téléphone dépose en direct ; ou faire passer les
octets par gRPC, ce que l'ADR 0021 avait déjà écarté. **Aucune n'a été retenue.**

### Ce que la quatrième voie résout, et que les trois autres contournaient

Un service dont le métier EST d'ingérer des octets n'a pas besoin d'une
exception : une route HTTP y est la règle. L'ADR 0015 ne se rouvre pas à
contrecœur, il se réécrit proprement — « les binaires entrent par Media, en
HTTP ; tout le reste passe par gRPC ».

### Les trois décisions qui fixent sa forme

**1. Media a sa propre base.** Une table de médias : identifiant, propriétaire,
nature, clé de stockage, type, taille, date, auteur du dépôt. Les autres
services ne gardent qu'un identifiant de média.

C'est ce qui rend possible ce que le point 22 laissait ouvert : une purge par
rétention, un journal des consultations, et surtout une suppression de compte
qui n'oublie pas les fichiers. Sans inventaire, personne ne sait ce qu'il y a
dans le stockage — et un objet qu'on ne sait pas nommer ne se supprime pas.

**2. L'autorisation reste au service propriétaire.** Directory décide qui voit
la photo d'un client, Driver qui voit une pièce d'identité ; ils demandent
ensuite une URL signée à Media, qui ne discute pas.

Media ne peut pas savoir qui est le livreur en mission sur une course donnée :
lui confier la règle l'obligerait à connaître le métier des autres. La règle du
référentiel tient — « toute autorisation se vérifie côté service » — et c'est le
service qui détient la donnée qui la vérifie.

**3. Les pièces du dossier livreur migrent tout de suite.** Le chemin actuel
disparaît, la route interne de Driver aussi.

**LA FENETRE EST OUVERTE AUJOURD'HUI ET SE REFERMERA.** Les bases ont été vidées
le 28 septembre : il n'existe aucune pièce déposée à reprendre. Migrer coûte
donc le code et rien d'autre. Au premier dossier livreur réel, ce sera une
reprise de données sur des pièces d'identité — l'opération qu'on repousse
toujours.

### Ce que cela ne dit pas

1. **La rétention** — combien de temps garder une photo, une pièce, une facture.
   Media rend la purge POSSIBLE ; aucun délai n'est décidé (point 22, question
   ouverte n° 4).
2. **Les factures n'existent pas.** Rien dans le dépôt n'en produit. Media est
   prêt à les stocker ; il n'a pour l'instant que deux usages réels.
3. **L'authentification de service à service** pour demander une URL signée.
   `IssueServiceToken` existe dans le contrat d'Identity ; son usage ici n'a pas
   été détaillé.
4. **Qui vérifie « le livreur en mission ».** La règle de visibilité a été
   arrêtée — le client lui-même, et le livreur affecté à sa course en cours —
   mais la décision 2 ci-dessus la confie à Directory, **qui ne sait pas ce
   qu'est une course**. Les deux issues sont détaillées plus bas ; aucune n'a
   été choisie, et le chemin du livreur n'est donc PAS implémenté.

### Ce qui a été construit le 28 septembre — et ce qui ne l'a pas été

**FAIT : la photo du client, bout en bout, pour le client lui-même.**

- `customers.photo_media_id` dans Directory — un identifiant de média, ni une
  clé de stockage (elle obligerait Directory à connaître le rangement du
  stockage) ni une URL (elle est signée, donc périmée en quelques minutes).
- `SetCustomerPhoto`, `RemoveCustomerPhoto`, `GetCustomerPhotoLink` au contrat
  de Directory. Le lien est un appel À PART et non un champ de `Customer` :
  l'y mettre imposerait un appel à Media à chaque lecture de profil, y compris
  pour les clients sans photo.
- `POST`, `DELETE` et `GET /api/client/v1/me/photo` à la passerelle. Le POST
  fait les deux temps — octets vers Media, identifiant vers Directory — pour
  que le téléphone n'ait qu'une requête à réussir. Une coupure entre les deux
  aurait laissé un fichier déposé que le profil n'aurait jamais réclamé.
- **Trois vérifications, aucune ne fait confiance à la précédente.** Media
  refuse un dépôt pour autrui (`PeutDeposer`) ; Directory redemande à Media ce
  qu'est le média avant de l'attacher, et refuse ce qui n'est pas une photo de
  profil appartenant à l'appelant ; `DirectoryAccess.ResolveCustomerId` décide
  qui peut demander le lien. La passerelle ne fait qu'acheminer.

**PAS FAIT, ET IL FAUT LE SAVOIR :**

1. **Le livreur en mission ne voit rien.** Deux issues, et c'est la question
   ouverte n° 4 :
   - *Directory appelle Delivery* pour demander « ce livreur est-il sur une
     course en cours de ce client ? ». Respecte la lettre de la décision 2,
     mais inverse la dépendance habituelle — Delivery dépend de Directory, pas
     l'inverse.
   - *Delivery répond lui-même*, sur une route qui part d'une course : il
     connaît le client, le livreur affecté et l'état. Il demande à Directory
     l'identifiant du média, puis à Media le lien. Aucune dépendance nouvelle,
     et c'est bien « le service qui détient la donnée » — la donnée vérifiée
     étant la mission, pas la photo. Mais cela s'écarte de la lettre de la
     décision 2.

   **Rien ne presse tant que l'application livreur n'affiche aucun portrait de
   client** — ce qui est le cas aujourd'hui. Ce point se tranche quand cet
   écran se dessine, pas avant.

2. **Les pièces du dossier livreur n'ont pas migré.** La décision 3 tient
   toujours, la fenêtre aussi : les bases ont été vidées le 28 septembre, il
   n'y a rien à reprendre. **ELLE SE FERMERA AU PREMIER DOSSIER LIVREUR REEL**,
   et ce sera alors une reprise de données sur des pièces d'identité.

3. **Quatre avertissements `buf` de plus.** `SetCustomerPhoto` et
   `RemoveCustomerPhoto` rendent `Customer`, comme les six autres commandes de
   profil. C'est un choix : ces huit appels rendent la même fiche, et en faire
   diverger deux pour satisfaire le linter aurait coûté plus de lisibilité
   qu'il n'en rapporte. Les 119 avertissements préexistants se traitent
   ensemble ou pas du tout. `GetCustomerPhotoLink`, lui, a sa réponse propre,
   et le contrat de Media reste sans avertissement.

## 28 — La suppression d'un compte client — TRANCHÉ EN PARTIE

**Décidé le 28 septembre 2026.** Le titulaire supprime son compte depuis
l'application. Le compte entre en sursis, ses sessions tombent, et l'effacement
a lieu **30 jours plus tard**. Se reconnecter pendant la fenêtre l'annule.

### Ce qui a forcé la question

La politique de confidentialité portait cette phrase : « L'application cliente
n'offre aujourd'hui aucun bouton pour déposer cette demande ». Apple exige
depuis 2022 qu'une application permettant de créer un compte permette d'en
**amorcer** la suppression depuis l'application elle-même ; renvoyer vers le
support n'y suffit pas, et un courriel pré-rempli se fait refuser une fois sur
deux. Le point ne pouvait donc pas rester ouvert jusqu'à la publication.

### Les trois décisions qui fixent sa forme

**1. Un délai de grâce de trente jours, annulable.** Un téléphone resté
déverrouillé, un appui malheureux, et l'historique d'un client part sans retour
possible — le support ne peut alors rien pour lui, puisqu'il ne reste rien à
restaurer.

`ACCOUNT_STATUS_PENDING_DELETION` **n'est pas une suspension**, et les confondre
casserait l'annulation : un compte suspendu ne se connecte plus, celui-ci le
doit. Le délai est un réglage (`AccountDeletion:GraceDays`), mais **changer le
réglage ne déplace aucune échéance déjà annoncée** — la date est figée dans le
compte au moment de la demande, jamais recalculée à la lecture.

**2. Le compte est effacé pour de bon, numéro compris.** C'est ce qu'un client
attend quand il demande la suppression. Conséquence assumée : le numéro
redevient libre, et au Bénin les numéros se recyclent. Le prochain titulaire
créera un compte neuf, ce qui est correct ; nous n'aurons aucune trace que
l'ancien a existé, ce qui est le prix.

Cela révise — sans la contredire — la phrase qui vivait dans `AccountStatus` :
« un compte n'est jamais effacé ». Ce qu'elle visait tient toujours :
l'**administration** n'efface pas, elle suspend. La règle exacte est désormais :
*un compte n'est effacé que par celui à qui il appartient.*

**3. L'effacement voyage par événement, dans la transaction qui supprime la
ligne.** `MarkErased` lève, `Remove` supprime, `SaveChanges` écrit les deux
ensemble et l'Outbox porte le message. Inverser l'ordre ferait disparaître le
compte d'Identity pendant que Directory garderait le nom, le courriel et les
adresses — pour toujours, sans que rien ne le signale.

`AccountErased` **ne porte ni nom ni téléphone** : un événement de suppression
qui recopierait l'identité du titulaire la ferait vivre dans Kafka pendant toute
la rétention du topic, au moment précis où quelqu'un demande qu'elle disparaisse.

### Ce que l'effacement emporte, et ce qu'il laisse

| Service | Ce qu'il détient | Ce qu'il en advient |
|---|---|---|
| Identity | téléphone, nom, courriel, sessions | **la ligne disparaît** |
| Directory | profil, adresses favorites, journal des lectures | **effacés** |
| Media | la photo de profil | **effacée**, par `DeleteOwnerMedia` |
| Delivery | `CustomerId`, et le destinataire | **rien à faire** — voir ci-dessous |
| Payment | `PayerPhone` | **intact** — à vérifier |
| Notification | le numéro de chaque message envoyé | **intact** — à vérifier |

**Delivery ne détient pas le nom du client**, contrairement à ce que la politique
de confidentialité laissait croire en promettant que « votre nom en est retiré ».
Il ne garde qu'un identifiant, qui ne résout plus rien une fois Identity et
Directory effacés. Le destinataire, lui, est un **tiers** : son nom et son
téléphone appartiennent à la course, pas au client, et la suppression du compte
de celui qui a commandé ne donne aucun droit dessus.

### Ce que cela ne dit pas

1. **Combien de temps la comptabilité oblige à garder un paiement.** Les
   règles OHADA imposent une durée de conservation aux pièces comptables ;
   personne ici ne la connaît, et l'inventer serait pire que de ne rien faire.
   `Payment` et `Notification` ne sont donc **pas** touchés par l'effacement, et
   la politique de confidentialité le dit désormais au client. **À VÉRIFIER
   AUPRÈS D'UN COMPTABLE**, puis à brancher.
2. **Le rapprochement FedaPay a-t-il besoin du numéro en clair ?** C'est ce qui
   décide si `PayerPhone` peut être remplacé par une empreinte ou non.
3. **Rien n'accueille le client à la reconnexion.** Le bandeau « votre compte
   sera supprimé le … » vit dans l'écran Profil ; or le client déconnecté
   revient sur l'accueil. Tant que la connexion ne le lui dit pas, il peut
   traverser ses trente jours sans jamais l'apprendre.
4. **Le back-office ne voit rien** des comptes en sursis, et n'a aucune façon
   de supprimer le compte de quelqu'un à sa demande écrite.

## 29 — Le reçu de course, par courriel — TRANCHÉ EN PARTIE

**Décidé le 28 septembre 2026.** Une course terminée déclenche un **reçu** par
courriel. Le transport est **SMTP**, sans fournisseur nommé dans le code.

### Les deux décisions qui fixent sa forme

**1. Resend, par son API HTTP.** `Email:Provider` vaut `resend` par défaut ;
`smtp` reste disponible comme issue de secours, avec n'importe quel relais —
Resend en offre un lui aussi.

Le choix initial s'était porté sur SMTP seul, pour ne nommer aucun prestataire.
Resend ayant été choisi, l'API l'emporte sur son propre relais SMTP pour trois
raisons que SMTP ne donne pas :

- **l'identifiant du message**, rendu par l'API : c'est ce qu'on colle dans un
  ticket quand un client dit n'avoir rien reçu ;
- **la clé d'idempotence**, honorée vingt-quatre heures. Elle referme la fenêtre
  où un envoi réussi suivi d'un échec de l'Inbox ferait partir le reçu deux fois
  — l'Inbox seule n'écarte qu'un message rejoué par Kafka, pas celui-là ;
- **des erreurs qui désignent leur cause** : un domaine non vérifié donne un 403
  nommé, là où le même refus arrive en « 550 » opaque par SMTP.

**LE DOMAINE DOIT ÊTRE VÉRIFIÉ CHEZ RESEND AVANT QUE QUOI QUE CE SOIT PARTE.**
Tant que les enregistrements DKIM et SPF de `hbatechettrade.com` ne sont pas
posés dans le DNS et validés dans le tableau de bord, chaque envoi reçoit un
403. Avant vérification, Resend n'autorise que l'expéditeur
`onboarding@resend.dev`, et uniquement vers l'adresse du titulaire du compte :
**un essai qui « marche » dans ces conditions ne prouve rien sur la
production.**

**Ce qu'on ne fait pas encore : les webhooks.** Resend sait notifier la remise,
le rebond et la plainte ; rien ne les écoute. On saura donc que Resend a
ACCEPTÉ le message, pas que le client l'a reçu. Pour un reçu, c'est tenable ;
pour un code de connexion, ça ne le serait pas — et c'est précisément pourquoi
`NotificationChannel.Email` ne portera jamais de code.

**2. Un reçu, pas une facture, et le texte le dit lui-même.** Une facture au
Bénin suppose une numérotation continue, l'IFU, le régime de TVA de HBA et des
mentions obligatoires. **Aucune de ces données n'existe dans le dépôt**, et les
inventer produirait un document faux que des clients garderaient. Le corps du
message porte donc la phrase « Ce n'est pas une facture ».

### Le point d'architecture qui a demandé à être tranché

**Qui connaît l'adresse ?** Le service qui sait qu'une course est terminée est
Delivery ; l'adresse vit dans Directory. Trois issues :

- *Delivery appelle Directory au moment de clôturer* — mettrait un appel réseau
  dans la transaction qui termine une livraison. Directory injoignable, et le
  livreur ne peut plus clore sa mission. **Écartée.**
- *Directory publie la commande* — ferait de l'annuaire un producteur de
  notifications, ce qu'il n'est pas. **Écartée.**
- *Notification résout l'adresse au moment d'envoyer* — c'est déjà sa raison
  d'être (« Notification décide du canal et du modèle »), et un échec ne coûte
  alors qu'un renvoi, pas une course bloquée. **Retenue.**

`SendEmail` porte donc un `subject_id` et non une adresse, comme `SendPush` le
fait déjà pour la même raison. **Un client sans courriel n'est pas une erreur** :
l'adresse est facultative dans ce produit, l'envoi est consigné « ignoré », avec
son motif.

### Ce que cela ne dit pas

1. **Aucune clé n'est configurée.** `RESEND_API_KEY` et `RESEND_FROM` sont
   vides : les reçus sont consignés « ignoré » et ne partent pas. Le service le
   dit au démarrage plutôt que de le taire — et rappelle dans la même ligne que
   le domaine doit être vérifié.
2. **Les webhooks de Resend ne sont écoutés par personne.** Remise, rebond,
   plainte : une adresse morte restera dans Directory, et le reçu partira dans
   le vide à chaque course.
3. **`ServiceToken:ClientSecret` de Notification n'existe pas encore.** Sans
   lui, l'appel à Directory part sans jeton et l'adresse ne sera jamais
   résolue. À créer comme ceux de Delivery et Dispatch.
4. **Rien n'est envoyé sur une course annulée ou remboursée.** Or c'est
   exactement le moment où un client veut une trace écrite.
5. **Le reçu n'est pas consultable dans l'application** — ni téléchargeable.
   `MediaKind.Invoice` existe et attend ; en faire un PDF gardé dans Media est
   la suite naturelle, et elle n'a pas été décidée.
6. **Trois des cinq modèles du catalogue ne sont envoyés par personne.**
   `delivery_otp`, `delivery_assigned` et `delivery_completed` existent depuis
   la création du service ; seul `otp_login` part réellement, publié par
   Identity. Le reçu est le deuxième. **`delivery_otp` est le plus grave : sans
   lui, le destinataire ne reçoit jamais son code de remise, et l'ADR 0005 en
   fait la preuve de livraison.**

## 30 — La course impayée : invisible, périssable — TRANCHÉ EN PARTIE

**Tranché le 29 septembre 2026.** Une course non payée n'est pas une course pour
celui qui l'a commandée. La question posée était : faut-il créer la course avant
ou après l'encaissement ?

### Ce qui a été écarté, et pourquoi

**Créer la course seulement au webhook.** C'était la demande initiale, et elle ne
supprime pas ce qu'elle vise. FedaPay doit être appelé avec un montant et une
référence, et le webhook, pour créer la course, doit trouver quelque part les
adresses, le destinataire et le colis : ces données sont persistées avant le
paiement quelle que soit l'option. On ne supprime donc pas la ligne en base — on
la renomme. Et on y gagne un défaut : si le webhook se perd, l'argent est
encaissé et il ne reste aucune trace de commande.

### Ce qui a été retenu

`PENDING_PAYMENT` reste l'état de création, mais :

1. **Invisible.** `DeliveryAccess` retire ce statut du périmètre `customer`,
   quel que soit le filtre demandé. Le back-office continue de le voir : un
   paiement abandonné est un fait commercial. Le suivi d'une course précise
   reste lisible par son propriétaire — c'est l'écran où il attend sa
   confirmation.
2. **Périssable.** `AbandonDesImpayees` la passe en `PAYMENT_FAILED` au bout de
   **quinze minutes**, la durée de vie d'un devis. Le devis n'est pas rendu :
   à cet instant il a expiré de son côté.
3. **Reprenable.** Rejouer la même clé d'idempotence rend la même livraison
   **et la même adresse de paiement**. Ce chemin rendait `null` auparavant : un
   client qui fermait la page de paiement n'avait plus aucun moyen de payer.
4. **Seulement pour ceux qui paient par FedaPay.** Le balayage exige une
   intention de paiement rattachée. Une commande de partenaire naît dans le même
   statut et n'en a pas — voir le point sur la facturation des donneurs d'ordre.

### Ce que cela ne dit pas

1. **Un paiement confirmé APRÈS l'abandon n'a aucun traitement.** FedaPay
   encaisse, le webhook arrive à la seizième minute, `ConfirmPayment` refuse la
   transition `PAYMENT_FAILED → PAID` — et l'argent est chez le client sans
   course en face. Les deux voies possibles : **rembourser automatiquement**, ou
   **ressusciter la course** au prix figé alors qu'il a expiré. Aucune n'est
   implémentée ; rien ne remonte non plus à l'ops aujourd'hui.
2. **Le suivi ne permet pas de reprendre un paiement.** L'écran dit « terminez
   le paiement » sans offrir de bouton pour le faire : il faut ressortir et
   recréer depuis le formulaire, où la clé d'idempotence retrouve la commande.
3. **Le délai est écrit deux fois.** Côté serveur dans
   `UnpaidDelivery:GraceMinutes`, côté téléphone dans `delaiDePaiementMinutes`
   — le BFF ne publie pas la valeur. Les deux doivent bouger ensemble.
4. **Le règlement du commerçant donneur d'ordre reste non tranché**, et la
   création lui est toujours refusée.

## 31 — Le numéro qui paie n'est pas forcément celui du compte — NON TRANCHÉ

**Constat du 29 septembre 2026.** Le service transmettait à FedaPay le téléphone
porté par le jeton, étiqueté du pays d'encaissement sans vérification. Un compte
créé avec un numéro français partait donc comme « +33… au Bénin » : la
transaction se créait, la page de paiement s'ouvrait, et le débit échouait — le
fournisseur sollicite le numéro du client lorsque la demande n'en porte pas
d'autre, et aucun opérateur béninois ne débite un +33.

**Corrigé en partie** : le numéro n'est transmis que si son indicatif correspond
au pays d'encaissement (`FedaPay:CustomerCountry`). Sinon, aucun `customer`
n'est envoyé et le payeur saisit son numéro sur la page du fournisseur.

### Ce qui reste à trancher

1. **L'application ne demande jamais le numéro mobile money.** Elle suppose que
   celui du compte en tient lieu. C'est vrai pour la plupart des clients à
   Cotonou, faux pour qui paie avec le numéro d'un proche, et faux pour tout
   compte étranger. Les options : demander le numéro de paiement au moment de
   commander, le garder dans la fiche client comme préférence, ou continuer de
   laisser la page du fournisseur le demander à chaque fois.
2. **`MISSING_PAYER_PHONE` refuse la création d'une course quand le jeton ne
   porte pas de téléphone.** Ce refus a été écrit quand le numéro servait
   réellement à payer. Maintenant qu'il peut ne pas être transmis, refuser la
   commande pour son absence n'a plus la même justification.
3. **La liste des opérateurs proposés par la page dépend du compte marchand.**
   Sur le compte de test, elle ne propose que « Momo Test » et des opérateurs
   ivoiriens — aucun opérateur béninois. À vérifier avant toute conclusion sur
   un échec de débit.

## 32 — Qui prévenir quand le compte d'un donneur d'ordre tombe bas — NON TRANCHÉ

**Constat du 29 septembre 2026.** Le domaine de Billing lève
`LowBalanceReached` au franchissement du seuil, et il est testé depuis le
premier jour. Il ne sortait nulle part : le contexte ne vidait pas ses faits de
domaine dans l'Outbox.

**Corrigé en partie** : le fait est désormais publié sur
`hba.billing.events.v1` (`hba.billing.v1.LowBalanceReached`), dans la
transaction du mouvement qui l'a provoqué. Une fois publié, il est
RATTRAPABLE — un consommateur ajouté plus tard lira la rétention du topic —
alors qu'un fait jamais émis est perdu pour toujours.

**PERSONNE NE L'ÉCOUTE ENCORE, ET C'EST LÀ QU'EST LA DÉCISION.** Prévenir le
titulaire suppose de savoir QUI joindre, et Billing ne connaît qu'un
`merchant_id` ou un `partner_id` : ni un compte Identity, ni un numéro. Les deux
formes de commande d'envoi existantes sont donc inutilisables en l'état —
`SendSms` veut un numéro, `SendEmail` veut un sujet Identity dont Notification
demande l'adresse à Directory.

### Les options

1. **Notification résout le contact d'un commerçant auprès de Directory**, comme
   il le fait déjà pour l'adresse d'un client. Il faudrait une commande d'envoi
   qui désigne un commerçant ou un partenaire plutôt qu'un sujet, et une lecture
   correspondante dans Directory. C'est la voie la plus cohérente avec ce qui
   existe : l'annuaire reste l'annuaire, et Notification reste le seul à décider
   du canal.
2. **Le compte de facturation porte son contact de notification**, renseigné à
   l'ouverture par `finance`. Le plus direct, mais c'est un attribut de plus sur
   un agrégat qui n'en a pas besoin pour sa règle, et un second endroit où
   l'adresse d'un commerçant peut être fausse.
3. **Aucune notification : le back-office surveille.** Un écran des comptes bas
   dans l'administration, et c'est l'exploitation qui appelle. Défendable au
   démarrage, avec dix commerçants ; intenable ensuite.

Tant que rien n'est tranché, le titulaire découvre son solde vide au moment où
une course est refusée — ce qui est exactement le défaut que l'alerte doit
éviter.

## 33 — Le débit orphelin après un arrêt brutal — NON TRANCHÉ

**Constat du 29 septembre 2026.** `CreateDeliveryHandler` débite le compte AVANT
de créer la course, pour qu'un donneur d'ordre au plafond l'apprenne avant
d'avoir une course qui ne partira jamais. Le prix de ce choix : si l'écriture de
la course échoue ensuite, l'argent est parti et rien n'est arrivé.

**Corrigé pour le cas courant** : le gestionnaire rattrape son propre échec et
appelle `ReverseDebit`, qui rend exactement le montant du débit annulé. L'appel
est rejouable — la clé de l'annulation est dérivée de celle du débit — et il est
ouvert au titulaire, parce qu'aucun montant n'y est choisi.

**CE QUI RESTE DÉCOUVERT : L'ARRÊT DU PROCESSUS ENTRE LES DEUX.** Conteneur tué,
machine perdue, et personne ne compense. Aucun des deux services ne peut le
retrouver seul : Billing voit un débit dont il ne sait pas s'il a une course en
face, Delivery n'a aucune trace d'un débit dont la course n'a jamais été écrite.

### Les options

1. **Un rapprochement périodique, côté exploitation.** Il confronte les
   mouvements de Billing aux courses de Delivery sur une fenêtre, et signale les
   débits sans course. Le plus simple à écrire, et il sert de toute façon au
   contrôle comptable — dont le solde d'un compte a besoin pour être
   vérifiable.
2. **Delivery inscrit son intention avant d'appeler Billing**, et un balayage
   reprend les intentions restées sans course. C'est une saga, avec son état et
   sa reprise ; correct, et bien plus de code pour un cas rare.
3. **On assume le trou et on le rend visible.** Le journal porte déjà, en erreur,
   le compte et la clé du débit à rendre. Tenable si le rapprochement du mois
   existe.

Aucune de ces trois options n'est implémentée. La première est la seule qui ne
demande rien à personne d'autre que du temps d'écriture.

## 34 — La liste des motifs d'incident du livreur — NON TRANCHÉ

**Décidé le 30 septembre 2026** : le livreur peut déclarer un incident quand la
remise est impossible. La course part en `FAILED` avec un motif et il est libéré.

**Ce qui n'est pas décidé, c'est la liste des motifs.** Le référentiel acteurs
marque l'incident « hors MVP » et ne propose aucune taxonomie. Le service
enregistre donc un **texte libre**, et l'application propose quatre formulations
plus un champ « Autre » :

- destinataire injoignable ;
- adresse introuvable ;
- destinataire refuse le colis ;
- code de remise bloqué.

Ces quatre-là sont les cas qu'on sait déjà possibles — pas une liste pesée. Le
champ libre est ce qui permettra de la fixer : après quelques semaines, ce que
les livreurs y écrivent dit quels motifs manquent.

**Pourquoi ce n'est pas dans le contrat.** Une énumération protobuf et une colonne
d'énumération en base se corrigent mal : renommer un motif oblige à migrer
l'historique, et en retirer un le rend illisible. Tant que la liste n'est pas
sûre, le texte libre coûte une statistique moins propre et rien d'autre.

### Ce qui reste à trancher

1. **La liste elle-même**, et si elle doit devenir une énumération du contrat.
2. **Le sort du colis.** L'écran dit au livreur que « HBA revient vers vous pour
   le colis ». Rien dans le code ne décrit ce retour : ni dépôt, ni délai, ni
   décharge de responsabilité.
3. **Le remboursement du client.** Une course en `FAILED` a été payée, et le
   colis n'est pas arrivé. C'est le point 3, et il est bloqué par le point 3 du
   fournisseur : FedaPay ne rembourse que depuis son tableau de bord, et
   seulement sur MTN.
4. **La part du livreur.** Il s'est déplacé et a porté le colis. Le grand livre ne
   lui crédite rien sur une course en `FAILED` ; personne n'a décidé si c'est
   juste.
