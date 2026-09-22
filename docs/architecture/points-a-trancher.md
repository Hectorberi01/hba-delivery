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

---

## 5. Délai de réponse à une offre

Le référentiel donne « 30 s par défaut ». Reste à trancher : combien de vagues,
avec quel élargissement de rayon, et au bout de combien de temps total la course
bascule en `NO_DRIVER_FOUND` ?

**État du code** : `Dispatch` est une coquille, et `DispatchExhausted` transporte
`waves_attempted` — le nombre de vagues n'est figé nulle part.

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

**Proposition en attente de décision** : l'email pour le pilote, détaillée par
l'ADR 0014 — provisoire par construction, et sans réponse pour le destinataire,
qui n'a pas d'adresse. Le choix d'un canal durable (WhatsApp, SMS, ou les deux)
reste entier.

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

**État du code** : `IPaymentClient` isole le sujet ; le service Payment est une
coquille. Le choix ne coûte rien à défaire aujourd'hui.

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
