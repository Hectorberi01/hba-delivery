# Points à trancher

Ce que le référentiel des acteurs ne décide pas, et qui n'a donc **pas** été
implémenté. Chaque point liste des options ; aucun n'a été choisi.

---

## 1. Règlement du commerçant donneur d'ordre

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

## 2. Paiement des livraisons créées par un partenaire

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

## 3. Politique d'annulation

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

## 7. Rétention des preuves et des positions

Combien de temps garde-t-on les photos de collecte et de remise dans MinIO ? Et
les positions des livreurs, qui ne sont pas persistées en base relationnelle —
sont-elles archivées, et pour combien de temps ?

C'est une question de droit autant que de stockage : ce sont des données
personnelles de travailleurs indépendants.

**État du code** : aucune politique de rétention n'est écrite.

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

## 16. Vélo et tricycle ne sont pas des types de véhicule

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
