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

## 24 — Le client voit les livreurs autour de lui — TRANCHÉ

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

### La limite qui n'a PAS été levée

**Aucune identité avant `DRIVER_ASSIGNED`.** La ligne « Téléphone du livreur :
pendant la mission » de la matrice tient toujours, et rien dans cette décision
ne la touche. La route cliente rend donc des **coordonnées seules** : pas
d'identifiant, pas de nom, pas de plaque, pas de type de véhicule. Un
identifiant stable suffirait à suivre le même livreur d'un jour sur l'autre,
ce qui est exactement ce que la matrice refuse.

C'est la raison pour laquelle la mise en forme se fait **à la passerelle** :
`FindAvailableNearby` rend des identifiants parce que le dispatch en a besoin,
et c'est à la frontière du client qu'ils doivent disparaître.

### Ce qui reste ouvert

1. **Le grain.** Position exacte, ou arrondie à un pâté de maisons ? La
   décision porte sur « les positions réelles » ; le nombre de décimales n'a
   pas été discuté.
2. **La fraîcheur et la cadence de rafraîchissement** côté client, qui décident
   du coût en données pour le client et en charge pour Driver.
3. **Le plafond du rayon et du nombre** rendus par la route, sans quoi une
   requête suffit à cartographier une ville.

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
