# Audit des trois parcours — 29 septembre 2026

Audit de lecture du dépôt `hba-delivery` : parcours du client particulier, du
livreur, et du partenaire B2B. Aucun code n'a été exécuté — ni dotnet, ni
Flutter, ni Docker sur la machine d'audit — tout ce qui suit vient de la lecture
des sources.

**Comment lire les constats.** *Vérifié ligne à ligne* veut dire que j'ai
rouvert le fichier moi-même après l'audit et relu le code qui le prouve.
*Rapporté* veut dire que le constat vient de la lecture détaillée du parcours
mais que je ne l'ai pas revérifié une seconde fois : la référence
`fichier:ligne` est là pour que tu tranches en dix secondes. *Déjà connu*
renvoie à `points-a-trancher.md` — ce n'est pas une découverte, c'est un rappel
que le point est encore ouvert.

---

## Ce qui bloque un pilote, dans l'ordre

Sept choses. Les trois premières sont des impasses complètes : le parcours ne va
pas au bout, quoi qu'on fasse.

| # | Parcours | Constat | Effet |
|---|---|---|---|
| 1 | Partenaire | Aucune porte n'existe pour **ouvrir** ni **créditer** un compte de facturation | La première course de tout partenaire échoue en 404, devis brûlé |
| 2 | Client | Le reçu ne peut partir pour personne : Notification appelle Directory **sans jeton** | Aucun courriel de fin de course, et le journal accuse « pas d'adresse » |
| 3 | Client | Un compte en attente de suppression **ne peut plus se reconnecter** | Le client est dehors trente jours, puis effacé, malgré la promesse inverse à l'écran |
| 4 | Partenaire | Le `FOR UPDATE` de Billing **ne verrouille rien** : aucune transaction n'est ouverte | Deux courses simultanées passent le plafond, et le refus arrive en « erreur interne » |
| 5 | Partenaire | `ReverseDebit` ouvert au titulaire = **course gratuite** | Le partenaire se rembourse sa propre course et la garde |
| 6 | Livreur | Une annulation pendant l'offre laisse le livreur **RÉSERVÉ à vie** | Il ne reçoit plus jamais d'offre, sans rien voir |
| 7 | Client + Partenaire | Le `MerchantId` est pris dans le **corps de la requête**, jamais vérifié | Un tiers lit et **annule** une course qui n'est pas la sienne |

Les points 4 et 5 portent sur du code écrit aujourd'hui même, et le 5 sur un
raisonnement que j'ai signé : je le corrige plus bas sans détour.

---

# 1. Parcours client

## Vérifié ligne à ligne

### C1 — Un compte en attente de suppression ne peut plus se reconnecter

`src/Services/Identity/…/RequestOtpHandler.cs:103`

```csharp
if (account.Status != AccountStatus.Active) { return false; }
```

Le client demande la suppression, le compte passe en `PendingDeletion`, ses
sessions tombent. L'écran lui a promis « vous reconnecter avant cette date vous
permettra de tout garder » (`suppression_compte.dart:223`, repris dans
`ProfileEndpoints.cs:196`). Il redemande un code : **aucun SMS ne part**, et
`VerifyOtpHandler` répond « Code incorrect. » à chaque essai. Le numéro étant
unique, il ne peut pas non plus recréer un compte.

Le refus n'est pas où on le cherche. `Account.EnsureCanAuthenticate` fait
exactement ce qu'il faut — il ne bloque que `Suspended`, et
`AccountStatus.cs:26` explique pourquoi : « se reconnecter est précisément le
geste qui annule la demande ». C'est l'éligibilité de l'OTP, un cran plus haut,
que personne n'a mise à jour.

**Défaut jumeau dans le même parcours** : même si la connexion passait,
`VerifyOtpHandler.cs:73-87` appelle `EnsureCanAuthenticate` puis `RecordLogin`,
et **jamais `CancelDeletion`**. « Se reconnecter annule la demande » n'est
implémenté nulle part.

### C2 — `NO_DRIVER_FOUND` verrouille l'accueil, et le seul bouton offert échoue toujours

`apps/client_app/…/home_screen.dart:161` et `:297`

`noDriverFound` est dans `suiviParAccueil` (`models.dart:87`), donc l'accueil
rend `_attente(course)` et **sort de `build` avant le nettoyage de la ligne
178**. Le `switch` de `_attente` ne traite que `pendingPayment` et
`paymentFailed` : `noDriverFound` tombe dans `_ =>` et affiche
`RechercheLivreurVue`. Sur l'onglet Accueil il n'y a pas de flèche de retour. Le
seul geste est « Annuler la demande », qui appelle `CancelDelivery` sur un état
**terminal** : le service répond 409 et le client lit, dans un snackbar,
« Transition interdite sur Delivery : NoDriverFound -> Cancelled par Customer. »
L'accueil ne se débloque qu'en tuant l'application.

Ce qui rend le diagnostic sûr : `paymentFailed`, l'autre état terminal
atteignable avec le marqueur posé, a bien reçu son bouton « Compris » qui remet
le provider à `null` (ligne 313). `noDriverFound` a simplement été oublié — et
`MarkNoDriverFoundHandler` dit lui-même que c'est « le cas ordinaire aux heures
creuses et au lancement ».

### C3 — Le reçu de course ne peut partir pour personne

`src/Services/Notification/…/Annuaire/CarnetDAdresses.cs:38`

L'appel naît d'un consommateur Kafka : pas de `HttpContext`, donc
`TokenForwardingInterceptor` ne pose rien, et `AddHbaGrpcClient` n'ajoute que
corrélation, report de jeton et délai. L'appel part nu, `DirectoryGrpcService`
est `[Authorize]`, la réponse est `Unauthenticated`. `CarnetDAdresses` l'attrape,
journalise un warning et rend `null` — et `SendNotificationHandler` consigne
alors « **Le compte n'a pas de courriel.** », un motif faux qui cache la cause.

Le commentaire du fichier affirme l'inverse (« IdentityServiceTokenProvider,
posé par `AddHbaGrpcClient` ») : c'est ce qui rend l'erreur invisible à la
lecture. Vérification : `AddHbaServiceToken` n'est appelé que par **Dispatch** et
**Delivery**, et les deux seuls clients qui posent l'en-tête le font à la main
(`DriverGrpcDirectory.cs:22`, `DriverFinder.cs:32`). Notification ne l'appelle
pas.

Deuxième étage également fermé : même avec un jeton de service, il ne porte
**aucun rôle**, et `DirectoryAccess.ResolveCustomerId` refuse en `NotFound` tout
appelant qui n'est ni le sujet ni du back-office.

### C4 — La clé d'idempotence n'est pas cloisonnée, et le rejeu ne vérifie aucun droit

`src/Services/Delivery/…/CreateDeliveryHandler.cs:484` et `:38-67`

`BuildIdempotencyKey` vaut `$"{orderer.PartnerId}:{key}"`, et `PartnerId` est la
constante `hba-internal` pour **tous** les clients et **tous** les commerçants.
Le magasin ne compare que `Scope` et `Key`. Deux appelants différents qui
envoient la même `Idempotency-Key` tombent donc sur la même ligne.

Et le chemin de rejeu est le **seul** de tout le service à ne passer par aucun
`EnsureCanRead` : il rend `DeliveryViewMapper.ToView(known, caller)`, c'est-à-dire
le destinataire, son téléphone, l'adresse de livraison, le contact du point de
collecte et le détail tarifaire d'une course qui n'est pas la sienne. Pire,
`ReprendreIntentionAsync` ouvre alors une intention de paiement sur la course du
**premier** avec le téléphone du **second**.

Atténuation honnête : l'application cliente tire un UUID v4
(`api_client.dart:35`), la collision n'est donc pas devinable depuis l'app. Mais
la clé est choisie par l'appelant, et le seul cloisonnement réel aujourd'hui est
le `partner_id` d'un partenaire tiers, qui est un GUID.

### C5 — Un client rattache sa course au commerçant de son choix, qui la lit et peut l'annuler

`src/Services/Delivery/…/CreateDeliveryHandler.cs:478`

`ResolveOrderer` prend `MerchantId` dans le **corps** de la requête.
`CreateDeliveryValidator` ne le mentionne pas, `Delivery.Create` ne vérifie ni
l'existence du commerçant ni que le point de collecte lui appartient, et aucun
appel à Directory n'a lieu dans le chemin de création — j'ai relu le validateur
entier pour en être sûr.

Conséquence en chaîne : `DeliveryAccess.IsWithinScope` rend `true` pour le
`merchant_owner` dont l'identifiant a été inscrit, qui voit alors dans sa liste
le destinataire, son téléphone, l'adresse de livraison et le prix — et
`EnsureCanCancel` le laisse **annuler la course**.

Le domaine assume qu'un client puisse désigner un commerçant comme point de
collecte. Ce qui n'a pas été pesé, c'est que ce champ ouvre par ricochet un droit
de lecture et d'annulation à un tiers. L'application cliente ne l'envoie jamais :
le champ est purement forgeable.

## Rapporté

- **C6 — L'écran de création réutilise sa clé d'idempotence après un abandon.**
  `new_delivery_screen.dart:43` : `_idempotencyKey` est tirée une fois à la
  construction de l'écran, et rien ne la réinitialise — ni `_invalidateQuote()`,
  ni le changement de point, ni le retour d'un abandon de paiement. Le client
  corrige son adresse, redemande un devis, reconfirme : le service reconnaît la
  clé et lui rend la **première** course, ancienne adresse et ancien prix, sans
  un mot. C'est la définition de « tentative » qui a glissé quand l'écran a cessé
  de se fermer après un abandon.
- **C7 — La photo de profil survit à l'effacement du compte.**
  `MediaCatalogue.cs:92` : même mécanique que C3 — appel depuis un consommateur
  Kafka, sans jeton, `[Authorize]` en face, `catch (RpcException)` qui journalise
  en warning. La fiche part, la photo reste dans MinIO, alors que l'écran promet
  « tout disparaît sans retour possible ».
- **C8 — L'écran promet un remboursement que rien ne fait.**
  `tracking_screen.dart:127` : « Aucun livreur disponible. Vous serez
  remboursé. » Aucun code du dépôt ne rend d'argent. Le dialogue d'annulation,
  lui, prend soin de ne rien promettre : c'est la bonne formulation, celle-ci ne
  l'est pas.
- **C9 — Annuler depuis `PENDING_PAYMENT` ne ferme pas la page FedaPay.** Rien
  ne dit à Payment d'annuler l'intention. Si le client termine le paiement sur la
  page restée ouverte, l'argent est encaissé, `ConfirmPayment` échoue sur un état
  terminal, le message finit abandonné en `inbox_messages`, et rien ne remonte à
  l'exploitation. C'est le mécanisme du point 30.1, mais en une seconde au lieu
  d'un quart d'heure — et cette variante-là n'est pas écrite.
- **C10 — La liste « Mes courses » ne pagine pas** (`delivery_repository.dart:79`) :
  au-delà de 25 courses l'historique est tronqué sans que rien ne le dise.

## Déjà connu

Points 30.1 à 30.3, 3.1 / 3.3 et 4 (retenue non affichée, remboursement
inexistant, sort d'une course sans livreur), 10 (`Sms:Provider = none` en
production), 29.1 (`RESEND_API_KEY` vide), 28.3 / 28.4, 25.

**À corriger dans la doc** : le point 29.6 est périmé. Le `delivery_otp` du
destinataire **a** un producteur depuis
`DeliveryIntegrationEventPublisher.cs:108` ; ce qui manque est l'expéditeur SMS,
c'est-à-dire le point 10.

---

# 2. Parcours livreur

## Vérifié ligne à ligne

### L1 — Une annulation pendant l'offre laisse le livreur RÉSERVÉ à vie

`src/Services/Dispatch/…/DispatchAggregate.cs:370`

```csharp
// La course est annulee ailleurs. Aucun evenement n'est leve […]
foreach (var offer in _offers.Where(o => o.IsPending)) { offer.Supersede(now); }
```

Le livreur est passé `RESERVED` sur `OfferSent`. Le client annule pendant les
trente secondes : Dispatch appelle `Cancel`, qui périme les offres **sans lever
aucun événement**. Or le consommateur de Driver ne libère un livreur que sur
`OfferExpired` — je l'ai relu, le `switch` n'a que trois branches :
`OfferSent → Reserved`, `OfferExpired → Released`, `OfferAccepted → OnMission`.

Le livreur reste donc `RESERVED`, et `FindAvailableAsync` n'accepte que
`Available` : il ne reçoit plus jamais d'offre. Aucun rattrapage —
`ExpireDueOffers` ne repasse que sur les recherches `Searching`. S'il appuie sur
« Refuser », `Offer.Decline` sort en silence. Seule issue : basculer hors ligne
puis en ligne, ce que rien ne lui dit, et que son accueil ne suggère pas
puisqu'il s'affiche toujours « en ligne ».

Ce qui rend le diagnostic sûr : `Accept` et `ExpireDueOffers` lèvent bien un
événement pour le même effet métier. Seul `Cancel` ne le fait pas.

### L2 — Une suspension ne se lève par aucun chemin de code

`src/Services/Driver/…/DriverAggregate.cs:305`

Recherche exhaustive sur tout le service : **aucun** `Unsuspend`, `Reinstate`,
`Reactivate`. Une fois `VerificationStatus.Suspended` posé, `ReviewKyc` lève,
`SubmitForReview` refuse, `AttachDocument` et `DeclareVehicle` refusent,
`GoOnline` refuse. Aucun RPC dans `driver_service.proto`, aucune route
back-office. Le commentaire du domaine promet pourtant « Seul ops la lève ». Un
livreur suspendu par erreur est exclu à vie, réparation en base uniquement.

## Rapporté

- **L3 — Course annulée + acceptation simultanée : le livreur reste EN MISSION,
  définitivement.** Fenêtre d'environ une seconde. Driver consomme
  `OfferAccepted` et passe `ON_MISSION` ; Delivery le consomme et lève sur un
  état terminal, l'événement est abandonné après trois tentatives. Le
  `DeliveryCancelled` déjà publié portait `DriverId = ""`. Ensuite `GoOffline`
  lève depuis `OnMission`, `GoOnline` ne fait rien, aucune route ne remet un
  livreur disponible.
- **L4 — Aucun rattrapage du profil livreur perdu à l'inscription.** Le profil ne
  naît que de `AccountRegistered`. Le point 25 établit, sur un cas réel, que
  Kafka perd cet événement quand le groupe n'existait pas. Une route de
  rattrapage a été créée **pour le client seul**, et exige le rôle `customer`. Le
  livreur a un jeton valide et un `/me` qui répond 404 à vie.
- **L5 — Aucun chemin quand la remise est impossible.** Depuis `PickedUp`, la
  seule transition ouverte au livreur est `Delivered`. `EnsureCanCancel` le
  renvoie vers « il déclare un incident » — et aucune commande d'incident
  n'existe. Destinataire absent, OTP verrouillé après cinq essais : il garde le
  colis, reste `ON_MISSION`, donc ne peut ni se mettre hors ligne ni recevoir
  d'offres, jusqu'à ce qu'un `ops` clôture en `FAILED`.
- **L6 — La feuille d'offre se fige quand le réseau tombe.**
  `home_screen.dart:534` : `onAccept` appelle le dépôt sans `catch` sur
  `OfflineException`, et la feuille est ouverte en `isDismissible: false,
  enableDrag: false` sans bouton de fermeture. Rien ne se passe, aucun message,
  et tant que la feuille est ouverte le sondage d'offres est suspendu.
- **L7 — Une remise mise en file et refusée par le serveur n'est signalée à
  personne.** `file_actions.dart:328` consigne l'échec dans le rapport de
  plantage, pas à l'écran, alors que l'écran a promis « elle sera confirmée au
  retour de la connexion ». Un OTP mal recopié brûle une des cinq tentatives en
  silence.
- **L8 — L'`Idempotency-Key` du livreur est transportée partout et honorée nulle
  part.** Les trois handlers d'action ne lisent jamais `command.IdempotencyKey`.
  Les gardes d'état de l'agrégat suffisent pour « arrivé » et « collecté », pas
  pour un OTP erroné rejoué.
- **L9 — Le livreur garde l'adresse et le téléphone du destinataire après la
  clôture.** `DeliveryViewMapper.cs:28` : `showDropoff` n'a aucune borne de
  temps, alors que l'OTP client, lui, est explicitement coupé à la clôture. La
  charge utile de l'historique est complète, même si l'écran n'en montre qu'une
  partie.
- **L10 — L'application ne relit jamais l'état opérationnel du serveur.** Au
  redémarrage, l'accueil affiche « hors ligne » pendant que le serveur tient le
  livreur `AVAILABLE` : le Profil affiche « DISPONIBLE » à côté d'une bascule
  éteinte, et il peut recevoir une offre qu'il ne verra pas — trente secondes
  perdues pour le client, avec `DriversPerWave = 1`.
- **L11 — `GetDriverPublicProfile` ne vérifie pas son appelant.** Nom, téléphone,
  véhicule et plaque sur simple `driver_id`. Le code le dit lui-même et le
  renvoie « à trancher », mais ce point n'est écrit nulle part dans
  `points-a-trancher.md`. Portée limitée au réseau interne aujourd'hui — aucune
  route de passerelle ne l'expose.

## Déjà connu

Points 5 / 23 (un livreur à la fois, trois anneaux — le code correspond
exactement à ce qui est tranché), 4, 10, 15, 17, 20, 21, 3, 22 / 28.

---

# 3. Parcours partenaire

## Vérifié ligne à ligne

### P1 — Aucun chemin n'existe pour ouvrir ni créditer un compte de facturation

Recherche exhaustive sur `src/Gateways/` : aucune route vers Billing. La seule
occurrence du mot est `GetCustomerBilling`, qui appartient à Delivery et n'a
aucun rapport. `OpenAccount` et `Credit` ne sont exposés que par le gRPC interne,
que le proxy ne route pas. Aucun amorçage non plus.

Donc : un admin crée le client partenaire, le partenaire obtient son jeton,
demande un devis, poste sa course. `CreateDeliveryHandler:89` **consomme le
devis**, puis le débit lève `NotFoundException("Compte de facturation")`. Le
partenaire reçoit un 404 et son devis est brûlé. Il n'existe aucun geste, même
manuel, pour créer ce compte.

La décision du 29 septembre — « recharge à la main par finance, pour commencer »
— n'a pas de porte. **Le parcours B2B est bouché de bout en bout**, et c'est le
premier trou à boucher avant toute mise en service.

### P2 — Le verrou pessimiste du débit ne verrouille rien

`src/Services/Billing/…/BillingAccountRepository.cs:45`

Le `SELECT … FOR UPDATE` est bien là, et son propre commentaire énonce la
condition : « SANS TRANSACTION OUVERTE, CE VERROU NE VAUT RIEN ». Recherche sur
tout `src/` : `BeginTransaction` n'apparaît **qu'une seule fois**, dans mon
propre test d'intégration, qui l'ouvre lui-même. Il n'existe aucun comportement
transactionnel dans le pipeline — `AddHbaApplication` n'enregistre que le
dispatcher, l'horloge, les validateurs et les handlers.

Le test passe donc sur un montage que le service ne reproduit pas. C'est le pire
cas de figure : une garantie testée, documentée, et absente à l'exécution.

Ce qui sauve l'invariant aujourd'hui est le jeton `xmin`, dont le commentaire de
la configuration affirme pourtant qu'il ne protège pas le solde. Résultat : la
seconde écriture échoue en `DbUpdateConcurrencyException`, donc en
`INTERNAL_ERROR`, et le donneur d'ordre reçoit « erreur interne » là où le
service croit lui répondre `INSUFFICIENT_BALANCE`.

### P3 — `ReverseDebit` ouvert au titulaire permet la course gratuite

`src/Services/Billing/…/BillingAccess.cs:137-163`

J'ai écrit, aujourd'hui, que « le pire qu'un titulaire puisse obtenir en forçant
cette porte est de récupérer son propre argent ». **C'est faux**, et voici
pourquoi : la clé du débit **est l'identifiant de la course**
(`CreateDeliveryHandler.cs:275-276`), et cet identifiant est rendu au partenaire
dans la réponse de création. `ReverseDebitHandler` ne vérifie que l'appartenance
du mouvement au compte — Billing ne connaît pas les courses et ne peut pas savoir
que celle-ci existe et sera livrée.

Un partenaire qui appelle `ReverseDebit(owner = lui, debitKey = <id de sa course
en cours>)` est remboursé **et garde sa livraison**.

Mon raisonnement tenait pour un débit dont la contrepartie a échoué ; il ne tient
pas pour un débit dont la contrepartie existe. Billing n'a pas l'information qui
permettrait de les distinguer, et c'est le cœur du problème.

Doute honnête sur la portée : Billing n'est exposé par aucune route de la
passerelle, il faut atteindre le gRPC interne. Mais c'est exactement le modèle de
menace que ce fichier revendique — « ce service n'est pas protégé par le fait de
n'être appelé que par Delivery ». La phrase du code et celle de la doc sont à
corriger dans tous les cas.

### P4 — Le commerçant désigné n'est jamais vérifié

Même constat que **C5**, côté partenaire : `CreateDeliveryHandler.cs:453` prend
`MerchantId` dans le corps. Un partenaire poste une course en désignant un
commerçant qui ne lui appartient pas ; le commerçant la voit — avec le point
d'enlèvement, le destinataire, les deux téléphones, le tarif et
l'`ExternalOrderId` du partenaire — et peut l'**annuler**. Le partenaire est
débité, un tiers annule, et personne ne rembourse (voir P5).

### P5 — Une course B2B annulée, échouée ou sans livreur n'est jamais remboursée

`ClosureHandlers.cs` ne touche pas à Billing — vérifié, pas une occurrence.
`ReverseDebitAsync` n'a **qu'un seul appelant** dans tout le dépôt :
`CreateDeliveryHandler.cs:334`, le rattrapage d'un échec de création. Rien ne
consomme `DeliveryCancelled`, `NoDriverFound` ou `DeliveryFailed` pour rendre
l'argent.

Le point 3 note qu'« aucun code ne rend d'argent » à propos de FedaPay. Ce qui
est nouveau depuis aujourd'hui, c'est que le débit B2B, lui, existe et aboutit :
l'argent est réellement pris, et la course peut réellement ne pas avoir lieu.

### P6 — Le retour d'information vers le partenaire n'a aucun producteur

`WebhookSender` est enregistré dans `Program.cs:80` et n'est **injecté nulle
part** : `IWebhookSender` n'a aucun appelant. Aucun consommateur de
`hba.delivery.events.v1` ne fabrique de webhook. Le partenaire n'apprend
l'affectation, la collecte, la remise ou l'échec **que** s'il interroge
`GET /api/v1/deliveries/{id}` en boucle.

C'est assumé et expliqué dans `src/Gateways/Hba.Gateway/Webhooks/README.md` — je
le signale comme le bout manquant le plus visible du parcours, pas comme une
chose cachée.

## Rapporté

- **P7 — Un partenaire compromis ne peut pas être coupé.** `PartnerClient.Disable()`
  n'est appelé nulle part. La passerelle n'expose que création et rotation du
  secret, et la rotation annonce elle-même que « les jetons déjà émis restent
  valides jusqu'à expiration », soit quinze minutes. En cas de fuite du secret,
  il faut écrire `enabled = false` à la main en base.
- **P8 — Les portées OAuth2 sont vérifiées à l'émission puis oubliées.**
  `IssuePartnerTokenHandler` appelle `ResolveScopes` puis construit le principal
  **sans les portées**, et le jeton ne porte aucun claim `scope`. Aucun
  consommateur de `scope` hors de l'émission. Un partenaire enrôlé pour les seuls
  devis crée des courses : la route n'exige que le rôle. La « clé scopée » du
  référentiel n'existe pas.
- **P9 — Le quota par partenaire n'est jamais appliqué.**
  `PartnerClient.RateLimitPerMinute` est stocké, exposé, et lu par personne. Les
  seules limitations sont par adresse IP et ne couvrent que la route de jeton.
- **P10 — `OpenAccount` n'impose pas le couple de types connus.** `OwnerType` est
  passé tel quel et n'est contrôlé que « non vide ». Un compte ouvert avec
  `"Partner"` serait invisible et indébitable pour son titulaire, avec un
  `NotFound` incompréhensible. Non atteignable aujourd'hui, faute de porte (P1).
- **P11 — Rien ne distingue une course interne d'une course de partenaire tiers.**
  `PartnerClient.Source` (`HBA_EXPRESS`, `HBA_FOOD`, `PARTNER_API`) est
  enregistré à la création puis jamais relu : il n'est pas dans le jeton, et la
  passerelle code en dur `Source = PartnerApi` pour toutes les créations
  partenaire. Une course de HBA Express est donc étiquetée `PARTNER_API` dans
  l'agrégat, dans l'événement et dans la statistique.
- **P12 — Le commentaire d'en-tête de `Hba.Billing.Api/Program.cs:12` réaffirme
  le mythe corrigé ailleurs** : « l'appelant est Delivery, avec un jeton de
  service ». C'est la phrase qui fera rouvrir la porte le jour où quelqu'un
  trouvera `BillingAccess` trop strict.

## Déjà connu

Points 33, 32, 6 (`hba.platform.order.events.v1` déclaré, créé, sans producteur
ni consommateur), 3, 13.

---

# Ce qui est solide — à ne pas défaire

Les trois audits convergent sur les mêmes points forts.

1. **`DeliveryAccess` + `DeliveryViewMapper`.** La matrice de visibilité est
   réellement appliquée, champ par champ, et il n'existe aucun DTO « complet » de
   secours. Un seul chemin lui échappe : le rejeu d'idempotence (C4).
2. **`TitulaireDuCompte`** distingue par le **partenaire** et non par le
   commerçant : la confusion la plus grave du périmètre est évitée à l'endroit
   exact où elle coûterait de l'argent.
3. **La chaîne de paiement client** : webhook signé comme seul déclencheur,
   relecture systématique chez le fournisseur, `EnsureCallerMayPayFor` côté
   service, retour navigateur qui ne décide de rien.
4. **La table des transitions et `Actor`** : aucun état ne change sans passer par
   `EnsureAllowed`, et les acteurs système y sont traités comme les acteurs
   humains.
5. **Le grand livre du livreur** : montant positif avec le sens séparé, index
   uniques filtrés, débit et passage à « versée » dans le même `SaveChangesAsync`,
   cumuls en une seule requête.
6. **La remise** : OTP jamais envoyé au livreur, comparaison à temps constant,
   `DELIVERED` interdit à l'administration.
7. **Les unicités qui comptent sont en base**, pas seulement dans le code :
   `account_movements.idempotency_key`, et `(PartnerId, ExternalOrderId)` filtré.
8. **La file d'actions hors ligne du livreur** : clé figée à la mise en file,
   estampille du propriétaire, arrêt au premier échec réseau, et refus délibéré
   de mettre en file « en ligne », « accepter » et « position ».
