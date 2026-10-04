# Ce qui reste à implémenter dans les deux applications — 1er octobre 2026

Périmètre : `apps/client_app` et `apps/driver_app`, confrontées aux contrats
`contracts/proto/` et aux routes de `src/Gateways/Hba.Gateway` dans l'état du
1er octobre 2026, après les phases 4 (tarification par nature), 8 (temps réel) et
9 (service Location).

**Méthode.** Lecture du code seulement. Il n'y a ni `dotnet`, ni `flutter`, ni
`docker` sur la machine où cette vérification a tourné : rien n'a été compilé ni
exécuté. Chaque constat porte le fichier et la ligne qui le fondent, pour qu'il
soit contestable.

**UNE ERREUR DE CET AUDIT, CORRIGÉE LE 1er OCTOBRE AU SOIR.** La première
version affirmait que le message `Offer` de Dispatch ne portait pas la nature de
la mission. **C'est faux** : il porte `service_type = 13` et `vehicle_type = 14`,
et le mappeur gRPC les remplit déjà. La lecture qui a produit ce constat s'était
arrêtée au champ 12 et en avait conclu que le message finissait là. Ce qui
manquait était ailleurs, et seulement dans l'application livreur, qui ne lisait
pas ces deux champs. Le texte du point 2 ci-dessous est corrigé ; l'erreur reste
mentionnée ici, parce qu'une décision avait été demandée sur une question qui ne
se posait pas.

**ÉTAT AU SOIR DU 1er OCTOBRE 2026 : LES SIX POINTS SONT FAITS.** Ils
restent écrits ci-dessous, avec leur constat d'origine et la mention de ce qui a
été livré — savoir ce qui manquait vaut autant que savoir ce qui existe, le jour
où quelqu'un se demande pourquoi le code a cette forme. Le point 2, qui était une
phase à lui seul, a été mené le même soir ; ce qui en reste dehors est écrit dans
son paragraphe, et **rien de tout cela n'a été compilé ni essayé contre un
serveur**.

**Ce que ce document n'est pas.** Ce n'est pas une liste de bugs. C'est la liste
des fonctionnalités qui existent côté serveur et qu'aucune des deux applications
ne sait encore atteindre — plus les quelques endroits où l'application appelle
déjà quelque chose de travers. Les dettes antérieures sont rappelées en fin de
document, sans être redétaillées.

---

## Synthèse

| Fonctionnalité | Domaine et service | Contrat | Passerelle | client_app | driver_app |
|---|---|---|---|---|---|
| Suivi en direct (phase 8) | fait | fait | fait | **fait** | **fait** |
| Position : cap, vitesse, précision | fait | fait | fait | sans objet | **fait** |
| Poids du colis dans le devis | fait | fait | fait | **fait** | sans objet |
| Tarif par **nature** (colis / course) | fait | fait | fait | **fait le 01/10** | sans objet |
| Tarif par **type de véhicule** | fait | fait | fait | **fait le 01/10** | sans objet |
| Plages horaires du tarif | fait | fait | fait | sans objet | sans objet |
| Habilitations du livreur | fait | fait | **fait le 01/10** | sans objet | **fait le 01/10** |
| Places passager du véhicule | fait | fait | **fait le 01/10** | sans objet | **fait le 01/10** |
| Capacité de charge du véhicule | fait | fait | fait | sans objet | **fait le 01/10** |
| Marque, modèle, couleur | fait | fait | **fait le 01/10** | sans objet | **fait le 01/10** |
| Nature d'une offre, vue du livreur | fait | **déjà au contrat** | fait | sans objet | **fait le 01/10** |
| Courses de personnes (Ride) | fait | fait | fait | **fait le 01/10** | **fait le 01/10** |

« Sans objet » veut dire que cette application n'a pas à porter la
fonctionnalité, pas qu'elle reste à faire ailleurs.

---

## 1. Les habilitations et les places passager — bloquées à la passerelle

**C'est le chantier le plus petit et celui qui débloque le plus.**

Le domaine porte les habilitations (`DriverAggregate.DeclareCapabilities`), la
couche applicative porte la commande
(`Hba.Driver.Application/Features/Drivers/Commands/BuildApplication.cs:54`), le
contrat porte le RPC (`driver_service.proto:57`, `DeclareCapabilitiesRequest`
ligne 396) et les places passager (`DeclareVehicleRequest.passenger_capacity`,
ligne 393). La migration `20260930233700_DriverCapabilities` est en base.

**La passerelle n'expose ni l'un ni l'autre.** Il n'y a aucune occurrence de
« capabilit » dans tout `src/Gateways`, et le corps de la déclaration de véhicule
est `VehiculeDto(string Type, string? Plate, int? CapacityGrams)`
(`Endpoints/Driver/ApplicationEndpoints.cs:163`) — le handler ligne 41 n'envoie
donc jamais `PassengerCapacity`.

**La conséquence s'enchaîne jusqu'au métier.** `passenger_capacity` reste à zéro
pour tout le monde — la valeur que la migration a posée, et c'est la bonne : aucun
livreur n'a déclaré de place. Mais rien ne permet de la changer. Donc
`Vehicle.PeutTransporterDesPersonnes` est faux partout, donc **aucun livreur ne
peut recevoir de course de personnes**, quand bien même le tarif course existerait.
Le métier course est fermé par cette seule absence de route.

**L'exploitation n'a pas de route non plus.** `Endpoints/Web/BackOfficeEndpoints.cs`
porte le KYC, la suspension et la réintégration — pas les habilitations. Or le
domaine dit que l'habilitation appartient à la personne et qu'« ops l'accorde
après examen » (`DriverAggregate.Habilite`). Il manque donc une route là aussi,
selon la décision prise au point À TRANCHER ci-dessous.

### FAIT LE 1er OCTOBRE 2026

**Décision : le livreur déclare, ops valide par le KYC** — point 38 des points à
trancher, qui porte les trois options écartées. Une seule route neuve, côté
livreur ; le service garde `EnsureSelfOrBackOffice`, de sorte qu'ouvrir la route
à ops un jour ne demandera pas de toucher au domaine.

Livré :

1. **Contrat.** `DriverApplication` porte `capabilities` (12) et
   `allowed_capabilities` (13). Le second est calculé par le service, pour la
   même raison que `required_documents` : la table véhicule vers habilitations
   est une règle métier, et une application qui l'aurait codée en dur
   proposerait « transport de personnes » à un cycliste.
2. **Service.** `DriverApplicationView` porte les deux listes et les places
   passager ; `GetDriverApplication` les remplit depuis `Vehicle.CapacitesDe` ;
   le mappeur gRPC les rend.
3. **Passerelle.** `PUT /api/driver/v1/capabilities` — l'ensemble complet, un
   corps vide ramène au colis, et un nom inconnu est refusé par
   `UNKNOWN_CAPABILITY` au lieu d'être ignoré en silence. `VehiculeDto` accepte
   désormais `PassengerCapacity`, `Brand`, `Model` et `Color` : **les quatre
   champs que cette route jetait**, dont les trois déclaratifs que le profil
   public rend au client pour qu'il reconnaisse « la voiture blanche » et qui
   restaient vides pour toujours. `Traduire` rend tout cela au dossier.
4. **Application livreur.** `Dossier` porte `capacites`, `capacitesPossibles`,
   `placesPassager`, `capaciteGrammes`, `marque`, `modele`, `couleur` ; une carte
   « Mes activités » coche dans la liste du service et ne propose pas le colis,
   qui est le socle ; le formulaire véhicule a un champ de places passager, qui
   disparaît pour une camionnette ou un vélo comme la plaque disparaît pour un
   vélo.
5. **Console.** Le dossier montre les activités et les places à ops, qui sinon
   validerait à l'aveugle ce que le point 38 lui demande d'examiner.

### Complété le soir du 1er octobre 2026

**Les trois champs déclaratifs ont leur écran.** Marque, modèle et couleur se
saisissent dans la carte « Mon véhicule », et le livreur les retrouve remplis au
retour. C'est la couleur qui compte le plus : un client qui attend au bord d'une
rue cherche « la voiture blanche », et c'est elle, pas la plaque, qui lui fait
lever la main au bon moment. Le profil public les rendait déjà ; ils étaient
simplement toujours vides.

---

## 2. Les courses de personnes — FAIT LE 1er OCTOBRE 2026

Le service Ride existe, la passerelle expose les deux parcours
(`Endpoints/Client/ClientRideEndpoints.cs` : création, course active, détail,
historique, annulation ; `Endpoints/Driver/DriverRideEndpoints.cs` : en route,
arrivé, départ, fin, annulation). **Aucune des deux applications n'appelle une
seule de ces routes.** Un balayage de `apps/client_app/lib` et
`apps/driver_app/lib` ne trouve aucun appel à `/rides`.

Côté client, il n'y a pas d'écran passager, et le fournisseur de suivi en direct
d'une course est écrit mais mort : `suiviCourseProvider`
(`features/deliveries/suivi_temps_reel.dart:212`) n'est référencé par aucun écran.

**IL N'Y A PAS D'OBSTACLE DE CONTRAT — la première version de cet audit s'est
trompée.** Le message `Offer`
(`contracts/proto/hba/dispatch/v1/dispatch_service.proto:58`) porte
`service_type = 13` et `vehicle_type = 14`, `OfferView` porte les deux depuis le
début, et `DispatchGrpcService.ToProto` les remplit. Ce qui manquait tenait
entièrement dans l'application livreur, qui ne lisait pas ces champs — **corrigé
le 1er octobre 2026** : `Offer` porte `nature` et `vehiculeAttendu`, et la feuille
d'offre titre « Nouveau passager » au lieu de « Nouvelle course ».

**Et elle refuse d'accepter une course, exprès.** Aucun écran de mission ne sait
encore suivre un passager : accepter laisserait le livreur avec quelqu'un dans la
rue et rien à l'écran, et le client avec un livreur qui ne vient pas. La glissière
est donc inerte sur une offre de course, avec la raison écrite en clair, et le
refus reste possible — l'offre repart à la vague suivante. **Ce garde-fou
disparaît avec la phase**, et pas avant.

### Ce qui a été livré le 1er octobre 2026

**Côté passager**, six fichiers dans `features/rides/` : le modèle (états, étapes,
chauffeur, course), le dépôt (`POST /rides`, `/rides/active`, `/rides/{id}`,
l'historique, l'annulation), les fournisseurs Riverpod, l'écran de demande, l'écran
de suivi, et la carte d'accueil. Le devis passe par la route des colis avec
`serviceType: "course"` — le prix garde UN chemin — et l'écran refuse d'afficher un
prix qui reviendrait en livraison plutôt que de laisser la création échouer sur
`QUOTE_SERVICE_MISMATCH`. Le paiement réutilise la webview des colis : le prix est
encaissé avant la recherche (point 35), et sans adresse de paiement l'écran le dit
au lieu d'envoyer le passager attendre devant un suivi qui n'avancera jamais.

**La carte du livreur a été généralisée** au lieu d'être recopiée :
`CarteDuLivreur` prenait un `Delivery` dont elle ne lisait que deux points et un
type de véhicule, elle prend maintenant ces trois choses. Les deux suivis
partagent donc une seule carte, et `suiviCourseProvider` — écrit la veille sans
écran — a enfin le sien.

**Côté chauffeur**, `course_models.dart` et `course_screen.dart`, plus les actions
de course dans le dépôt. L'écran n'affiche qu'un geste à la fois, celui que l'état
du serveur autorise : « Je suis en route », « Je suis arrivé », « Démarrer »,
« Terminer ». Les deux derniers se **glissent** comme l'acceptation d'une offre —
démarrer engage le prix et terminer clôture la course, et un téléphone secoué dans
une poche ne doit pas pouvoir les déclencher. L'accueil porte une carte
« PASSAGER » distincte de celle des livraisons, relit la course active au réveil
(`GET /missions` ne rendra jamais un passager), et le sondage d'offres se tait tant
qu'une course est en cours. Le garde-fou de la feuille d'offre est levé.

**`acceptOffer` sait désormais ce qu'il attend.** Il interrogeait `/missions` en
boucle pendant quatre secondes après l'acceptation ; pour une course, cette liste
ne contiendra jamais rien. Il attend maintenant dans la bonne liste, selon la
nature de l'offre.

### Ce qui reste dehors, et pourquoi

- **Pas d'historique des courses de personnes.** La route existe
  (`GET /rides`), le dépôt sait la lire, aucun écran ne l'affiche : l'onglet
  « Courses » de l'application cliente liste des colis, et le mot « course » y a
  déjà un autre sens. C'est le **point 39**, non tranché — fusionner les deux
  listes, renommer l'onglet, ou donner un autre mot au transport de personnes
  n'est pas une décision d'écran.
- **Aucune preuve de fin de course.** Pas d'OTP, pas de photo, pas de signature :
  le point 35 n'en a posé aucune, et en inventer une l'inscrirait dans
  l'interface avant que quiconque l'ait décidée.
- **Pas d'incident de course.** La liste des motifs n'est pas tranchée
  (point 34) et ne l'est pas davantage pour les courses. Le chauffeur peut
  annuler tant que le passager n'est pas monté ; après, il n'a rien.
- **Le gain du chauffeur n'est pas affiché.** Ride porte ce que le PASSAGER
  paie ; la part du chauffeur est calculée par Pricing puis Billing. L'écrire sur
  l'écran de course serait un mensonge, donc l'écran se tait et les gains restent
  dans leur onglet.
- **Un passager, une course à la fois à l'écran.** Le point 35 en autorise
  plusieurs ; la carte d'accueil montre la première et annonce le nombre. Choisir
  demandera un écran.
- **Le poids envoyé au devis vaut zéro.** La grille des courses n'a pas de
  supplément au kilo ; envoyer un poids de colis par habitude ferait payer un
  passager au gramme. Si la grille des courses en acquiert un, cette ligne devra
  changer.
- **Une étape mise en file est datée par le serveur, pas par le chauffeur.** Les
  routes de Ride n'acceptent aucun horodatage client, contrairement à celles des
  livraisons. Une étape déclarée dans un sous-sol et partie vingt minutes plus
  tard sera datée de l'arrivée au serveur. Y remédier demande un champ au
  contrat.

**Et deux préalables à tout essai, aucun n'étant du code applicatif** : les
montants du tarif course, sans lesquels chaque devis échoue sur `NO_TARIFF` ; et
au moins un livreur habilité au transport de personnes avec une place passager
déclarée, ce qui est devenu possible le même soir.

---

## 3. Le choix du véhicule côté client — FAIT LE 1er OCTOBRE 2026

La passerelle accepte `VehicleType` sur le devis depuis le 1er octobre
(`Endpoints/Client/ClientDeliveryEndpoints.cs:280`, champ facultatif valant la
moto en son absence). L'application ne l'envoie pas :
`delivery_repository.dart:13-33` ne transmet que les quatre coordonnées et le
poids.

**Conséquence.** Tout devis client est un devis moto. Les quatre autres grilles
tarifaires — voiture, camionnette, vélo, tricycle — sont inatteignables depuis le
téléphone, alors qu'elles existent et que le seeder les crée.

### FAIT LE 1er OCTOBRE 2026

`DeliveryRepository.quote` prend `vehicleType`, et le formulaire d'envoi porte un
sélecteur des cinq véhicules, en français — la route accepte les deux
orthographes, et envoyer les mots que le client lit évite une table de traduction
de plus. Le changement de véhicule appelle `_invalidateQuote`, sans quoi le client
verrait le tarif d'une moto sous un choix de camionnette. La moto reste le défaut,
comme le zemidjan à Cotonou : c'est le mode du marché et la grille la moins
chère.

---

## 4. La nature du devis côté client — FAIT LE 1er OCTOBRE 2026

`Quote.fromJson` (`features/deliveries/models.dart:123`) ne lit pas le champ
`nature` que la passerelle renvoie désormais (`ClientDeliveryEndpoints.cs:54`).

Inoffensif alors : Pricing replie l'absence de nature sur le colis, ce qui est
exactement ce que faisait l'application. Mais le champ est renvoyé au client pour
une raison — vérifier au devis ce que Pricing contrôlera à la consommation.

**`Quote` porte désormais `nature`**, l'écran d'envoi demande explicitement
`serviceType: 'colis'` même si c'est le défaut, et un devis qui revient avec une
autre nature n'est pas affiché : le dire tout de suite vaut mieux qu'un
`QUOTE_SERVICE_MISMATCH` à la création, que le client ne pourrait pas
interpréter.

---

## 5. `deliveryId` qui ne vaut jamais rien, côté livreur — FAIT LE 1er OCTOBRE 2026

`Offer.fromJson` lit `json['deliveryId']`
(`apps/driver_app/.../missions/models.dart:218`). La passerelle, elle, renvoie le
message protobuf tel quel (`Results.Ok(offer)` dans `DriverEndpoints.cs`), dont le
champ est `reference_id`, sérialisé `referenceId`. **La valeur vaut donc toujours
la chaîne vide.**

Rien ne cassait, et pour une seule raison : aucun écran ne consommait
`deliveryId` — le champ n'était que déclaré et affecté. C'était une panne en
attente de son premier usage.

**Renommé `referenceId`**, champ et clé JSON, avec le pourquoi écrit au-dessus de
la déclaration. La référence désigne une livraison tant que le point 2 n'est pas
tranché : l'offre ne porte toujours pas la nature de la mission.

---

## 6. La capacité de charge jamais saisie — FAIT LE 1er OCTOBRE 2026

`DossierRepository.declarerVehicule` accepte `capaciteGrammes`
(`dossier_repository.dart:161`) et la passerelle l'attend (`CapacityGrams`), mais
`dossier_screen.dart` appelait la méthode **sans** ce paramètre. Le champ
n'existait donc dans aucun écran, et la capacité de charge restait à zéro pour
tous les véhicules.

**Un champ « Charge utile (kg) » a été ajouté au formulaire véhicule**, saisi en
kilos et converti en grammes à l'envoi : le service compte en grammes — l'unité
entière de tout le dépôt — mais personne ne déclare « 50 000 » pour cinquante
kilos.

---

## Ce qui est à jour — ne pas refaire

- **Le suivi en direct.** Client : `suivi_temps_reel.dart` ouvre le hub,
  `carte_du_livreur.dart` affiche la position, `tracking_screen.dart` garde un
  sondage de 30 s en filet. Livreur : le hub s'ouvre et se ferme aux quatre
  points de démarrage et d'arrêt du sondage, lui-même passé de 5 s à 30 s.
- **La position enrichie.** `home_screen.dart:676-678` capte cap, vitesse et
  précision et les borne aux limites de `Motion.Create` ; le dépôt les transmet
  (`mission_repository.dart:85-93`). Le client n'a pas à les recevoir : le
  point 37 les exclut de la trame de mission.
- **Le poids du colis.** Envoyé par le client, et depuis la phase 4 il arrive
  jusqu'à Pricing au lieu d'être jeté à la frontière gRPC.
- **Les plages horaires du tarif.** Entièrement serveur : rien à écrire dans les
  applications.

---

## Dettes antérieures qui touchent encore les applications

Rappelées ici parce qu'elles concernent les mêmes fichiers, pas redétaillées.

- **`com.example` dans l'identité de l'application cliente**, et seulement
  elle : `client_app/android/app/build.gradle.kts:45` et `:56`,
  `client_app/ios/Runner.xcodeproj/project.pbxproj` (sept occurrences). Bloque
  toute publication sur les deux magasins. L'application livreur est propre.
- **Pas de reprise de paiement** : une livraison dont le paiement a été
  interrompu n'a aucune route pour reprendre la main.
- **OSRM sans carte du Bénin** (point 36) : les distances et durées affichées
  dans les deux applications viennent d'un moteur qui ne connaît pas le pays.
- **Le zonage tarifaire est fictif** : `ConfiguredZoneLocator` ignore la
  géométrie, tout point tombe dans la zone de repli. Le prix affiché est donc
  celui d'une seule zone, quelle que soit la course.
- **`GoOnlineRequest.position`** (`driver_service.proto:196`) est au contrat et
  ignoré par le service depuis que les positions ont quitté Driver. L'application
  ne l'envoie pas ; le champ est à retirer ou à documenter comme réservé.
- **`FindNearbyDrivers` rend des identifiants de livreurs à un jeton client** :
  la route des livreurs proches expose plus que ce que l'écran montre.

---

## À trancher avant d'écrire du code

Conformément au référentiel des acteurs, ces points ne sont pas implémentés et
aucune option n'est choisie ici.

1. ~~**La nature de la mission dans `Offer`.**~~ **SANS OBJET : la question était
   mal posée.** `Offer` porte `service_type` et `vehicle_type` depuis le
   1er octobre 2026, et le mappeur les remplit. Rien n'était à trancher ; il
   fallait lire les deux champs dans l'application livreur, ce qui est fait.
2. ~~**Qui déclare les habilitations.**~~ **TRANCHÉ le 1er octobre 2026** : le
   livreur déclare dans son dossier, ops valide par le KYC. Voir le point 38 des
   points à trancher pour les options écartées, et la raison qui a commandé la
   décision — un dossier validé se fige, donc les habilitations ne se déclarent
   que sur un dossier qui n'est pas encore validé.
3. **Les montants du tarif course.** Plancher, prix au kilomètre et part du
   chauffeur. Tant qu'ils manquent, tout devis de course échoue sur `NO_TARIFF`,
   et aucune copie du tarif colis ne sera faite à leur place.

---

## Ordre proposé

1. ~~Le point 1~~ — **fait le 1er octobre 2026**, décision comprise (point 38).
2. ~~Les points 5 et 6~~ — **faits le 1er octobre 2026**.
3. ~~Les points 3 et 4~~ — **faits le 1er octobre 2026** : l'application cliente
   choisit son véhicule et vérifie la nature du devis.
4. ~~Les trois champs déclaratifs du véhicule~~ — **faits le 1er octobre 2026**.
5. ~~La nature de l'offre côté livreur~~ — **faite le 1er octobre 2026**, et il
   n'y avait rien à trancher.

**Les six points sont faits.** Ce qui reste est écrit à la fin du point 2 : pas
d'historique des courses (point 39, non tranché), pas de preuve de fin de course
ni d'incident (points 35 et 34), et deux préalables à tout essai — les montants du
tarif course, et un livreur habilité au transport de personnes avec une place
déclarée.

**Rien n'a été compilé.** Ni `flutter analyze`, ni `flutter test`, ni les
applications : il n'y a pas de chaîne Dart sur la machine où ce travail a été
écrit. Les 1 664 lignes ajoutées aux deux applications sont relues, équilibrées et
cohérentes avec les contrats lus ligne par ligne — elles ne sont pas éprouvées.
