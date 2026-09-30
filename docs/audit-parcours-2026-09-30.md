# Audit des parcours client et livreur — 30 septembre 2026

Deuxième passage, un jour après l'audit du 29. Périmètre : le parcours client et le
parcours livreur, applications mobiles **et** backend, plus une recherche de failles
de sécurité sur les deux chaînes.

**Méthode.** Lecture du code uniquement. Il n'y a ni `dotnet`, ni `flutter`, ni
`docker` sur la machine où l'audit a tourné : rien n'a été compilé ni exécuté.
Quatre agents de lecture ont balayé le dépôt en parallèle, puis **chaque constat
sérieux a été relu ligne par ligne** avant d'être écrit ici. Les constats des
agents qui n'ont pas survécu à cette relecture sont listés en annexe B, pour que
personne ne les reprenne plus tard.

**Ce que l'audit ne refait pas.** Les constats de `audit-parcours-2026-09-29.md`
et des trois `audit-app-livreur*.md` corrigés depuis ne sont pas répétés. Annexe A
en dresse la liste, vérifiée dans le code.

---

## Résumé

| | Client | Livreur | Commun | Reste |
|---|---|---|---|---|
| Bloquant | 3 | 2 | 2 | **5** |
| Sérieux | 4 | 8 | 3 | 15 |
| Mineur | — | — | ~20 (annexe C) | ~20 |

**Les sept bloquants recensés sont corrigés** (30 septembre 2026, même journée) :
voir annexes E à I.

**MAIS LA PUBLICATION DE L'APPLICATION CLIENT RESTE BLOQUÉE, par un huitième
bloquant que cet audit avait manqué** : l'identifiant d'application est resté
`com.example.hba_client`, que Google Play refuse. Il est décrit en **B8**,
ajouté le 30 septembre 2026 en traitant B7 — les deux vivaient dans le même
fichier. Annoncer « publication débloquée » sans lui aurait été faux.

**Un fait découvert en traitant B4 et qui n'était pas dans l'audit initial : le
dépôt GitHub est public.** `Hectorberi01/hba-delivery`, `"visibility":
"public"`, et `deploy/garage/garage.toml` répondait en HTTP 200 sur
raw.githubusercontent.com. Cela ne change pas la liste des constats, mais cela
change la lecture de tout ce qui est « versionné » : ce n'est plus « lisible par
un prestataire », c'est « lisible par tout le monde ». Voir annexe F.

Deux constats coûtent de l'argent réel (B2, B6). Deux exposent des pièces
d'identité (B3, B4). Un empêche purement et simplement la publication de
l'application client sur iOS (B5) et un autre sur Android (B7).

**Le fil rouge de cet audit : ce qui n'est pas testé casse.** Les deux failles les
plus graves — le compteur d'essais du code de remise et le prix décorrélé du
trajet — vivent exactement là où il n'y a aucun test d'intégration. Pire, le test
qui *devrait* attraper la première passe au vert tout en validant un comportement
faux (voir B1). Et la régression de compilation trouvée sur le disque (annexe D)
a survécu à une journée entière parce qu'aucune chaîne d'intégration continue ne
lance Flutter.

---

# BLOQUANT

## B1. Le compteur d'essais du code de remise n'est jamais enregistré : le code est forçable — CORRIGE

**Fichiers**
- `src/Services/Delivery/Hba.Delivery.Domain/Deliveries/Delivery.cs:458-465`
- `src/Services/Delivery/Hba.Delivery.Application/Commands/DriverActions/DriverActionHandlers.cs:87` et `:99`
- `src/BuildingBlocks/Hba.BuildingBlocks.Application/Messaging/Dispatcher.cs` (intégralité)

**Ce que fait le code.** `ConfirmDelivery` vérifie le code, remplace l'objet OTP
par celui dont le compteur est incrémenté, puis lève :

```csharp
var verification = Otp.Verify(otpCode);
Otp = verification.Otp;              // compteur + 1, EN MEMOIRE

if (!verification.Succeeded)
{
    Raise(new DeliveryOtpAttemptFailed(...));
    throw new DomainException("INVALID_OTP", "Code de remise incorrect.");
}
```

Le handler, lui, n'appelle `SaveChangesAsync` qu'**après** `apply` :

```csharp
apply(delivery, caller.ToActor(), NormalizeTimestamp(clientTimestamp));   // :87  leve ici
...
await unitOfWork.SaveChangesAsync(cancellationToken);                     // :99  jamais atteint
```

Et le `Dispatcher` n'a aucun pipeline transactionnel : il résout le handler,
valide, invoque, et laisse l'exception remonter. Rien ne sauvegarde.

**Conséquence.** `FailedAttempts` revient à zéro à chaque requête. `IsLocked`
(`DeliveryOtp.cs:35`) n'est jamais vrai en production, et `OTP_LOCKED`
(`DeliveryOtp.cs:56`) est du code mort. Le code fait six chiffres.

**Scénario.** Le livreur affecté à une course arrive au point de dépose, le
destinataire est absent ou refuse de donner le code. Il appelle
`POST /api/driver/v1/missions/{id}/deliver` en boucle. Au bout de quelques
milliers d'appels — quelques minutes avec une boucle — il tombe sur le bon code.
La course passe en `Delivered`, ce qui déclenche la facturation du donneur d'ordre
et le crédit de sa propre part. Le colis, lui, est où il veut.

Il n'y a aucune limitation de débit par course sur cette route pour rattraper le
coup.

**Pourquoi ce n'est pas déjà corrigé — et pourquoi le test rassure à tort.**
`DeliveryStateMachineTests` vérifie le verrou sur **une même instance en mémoire** :
cinq appels à `ConfirmDelivery` sur le même objet C# atteignent bien `OTP_LOCKED`,
parce que rien n'est rechargé entre-temps. Le test est vert. Le comportement réel
est faux. C'est le pire cas de figure : un test qui protège une propriété que le
système n'a pas.

## B2. Le prix n'est pas lié au trajet réellement commandé — CORRIGE

**Fichiers**
- `src/Services/Pricing/Hba.Pricing.Domain/Quotes/Quote.cs` — `Consume(Guid deliveryId, DateTimeOffset now)`
- `src/Services/Pricing/Hba.Pricing.Application/Features/Quotes/Commands/ConsumeQuote.cs`
- `src/Services/Delivery/.../CreateDelivery/CreateDeliveryHandler.cs:118`, `:183-184`

**Ce que fait le code.** Le devis **porte** le trajet : `Quote` a bien
`GeoPoint Pickup` et `GeoPoint Dropoff`, renseignés à la création. Mais
`Consume` ne les regarde pas :

```csharp
public void Consume(Guid deliveryId, DateTimeOffset now)
{
    // controle de l'id de livraison, de la double consommation, de l'expiration.
    // AUCUN controle des points.
}
```

Et la livraison est créée avec les points **de la commande**, jamais avec ceux du
devis :

```csharp
await pricing.ConsumeQuoteAsync(command.QuoteId, deliveryId, cancellationToken);   // :118
...
ToLocation(command.Pickup),                                                        // :183
ToLocation(command.Dropoff),                                                       // :184
```

**Scénario.** Le client demande un devis Ganhi → Ganhi, 300 m, 700 XOF. Il reçoit
un `quoteId`. Il appelle ensuite `POST /api/client/v1/deliveries` avec ce
`quoteId` et un `dropoff` à Calavi, 20 km. La livraison est créée, le snapshot
tarifaire figé à 700 XOF (ADR 0004 fait son travail : le prix ne bouge plus), le
paiement encaissé à 700 XOF, et la part livreur calculée sur 700 XOF. Le livreur
fait 20 km pour le prix de 300 m ; HBA porte la différence sur toutes les courses.

Ce n'est pas un scénario d'attaquant sophistiqué : n'importe qui avec le trafic de
l'application peut le reproduire, et un client mécontent le partagera.

**Second angle.** Le devis n'a pas de propriétaire. `PricingGrpcService` porte un
`[Authorize]` de classe (`:24`) sans contrôle d'appelant sur `GetQuoteById` (`:51`)
ni `ConsumeQuote` (`:68`). Un client qui connaît l'identifiant du devis d'un autre
peut le consommer et faire échouer sa commande. L'identifiant est un GUID, donc
l'énumération est difficile — mais la règle du référentiel n'est pas respectée.

**Pourquoi ce n'est pas déjà corrigé.** C4 et C5 du 29/09 ne portaient que sur
l'isolation de la clé d'idempotence et sur le `MerchantId` ignoré côté client. Le
lien devis ↔ trajet n'a jamais été examiné. Le correctif est bon marché : le devis
connaît déjà ses points, il suffit que `Consume` les reçoive et compare, avec une
tolérance de quelques dizaines de mètres pour le bruit GPS.

## B3. Media : lecture, liste et suppression sans aucun contrôle d'appelant — CORRIGE

**Fichier** : `src/Services/Media/Hba.Media.Application/Assets/MediaHandlers.cs`

Quatre handlers sur cinq n'ont pas de garde :

| Handler | Ligne | Appelant reçu | Contrôle |
|---|---|---|---|
| `GetMediaHandler` | 109 | aucun | aucun |
| `GetReadUrlHandler` | 122 | oui, `ICallerContext` | **aucun** — il ne sert qu'à écrire le journal d'accès (`:141`) |
| `ListMediaHandler` | 148 | aucun | aucun |
| `DeleteMediaHandler` | 165 | aucun | aucun |
| `DeleteOwnerMediaHandler` | 192 | oui | oui (`:215`) — celui-là est gardé |

**Scénario.** N'importe quel jeton valide — celui d'un client — qui connaît un
`assetId` obtient une URL signée de 300 s sur la pièce d'identité d'un livreur, ou
peut la supprimer. `ListMediaHandler` prend `ownerType` et `ownerId` depuis la
requête : il suffit de connaître l'identifiant d'un livreur pour lister son
dossier complet.

Le journal d'accès de `GetReadUrlHandler` est d'ailleurs à double tranchant : il
enregistre consciencieusement que la lecture a eu lieu, sans jamais se demander si
elle était permise.

**Atténuation — et une correction de ce constat, écrite le 30 septembre 2026 en
le traitant.** Ce constat annonçait « la pièce d'identité d'un livreur ». C'était
faux : les pièces du dossier livreur **ne sont pas dans Media**. La décision 3 du
point 27 — « les pièces du dossier livreur migrent tout de suite » — n'a pas été
exécutée, et Driver les sert encore par son propre `IObjectStore`, derrière
`GetDriverApplication`, qui **est** gardé (`DriverAuthorization.EnsureSelfOrBackOffice`).

Ce qui était réellement exposé : les **photos de profil des clients**, seul contenu
vivant de Media. Une atteinte à la vie privée réelle, et une suppression possible
par n'importe qui — mais pas une fuite de pièces d'identité. Les identifiants sont
des GUID, l'énumération est donc coûteuse, et la passerelle restreint les routes. Mais le référentiel des acteurs est
explicite : *« Toute autorisation se vérifie côté service, jamais seulement côté
BFF ou application. »* C'est le même trou que `DeleteOwnerMedia` avait la veille,
aux quatre autres handlers près.

## B4. Secrets du stockage d'objets versionnés dans un dépôt PUBLIC — CORRIGE

**Fichier** : `deploy/garage/garage.toml` — `rpc_secret` ligne 25, `admin_token`
ligne 41, `metrics_token` ligne 42. Le fichier est **suivi par git** (`git ls-files`
le confirme) et monté en production par `deploy/compose.infra.yml`.

**Conséquence.** Quiconque lit le dépôt — un prestataire, un futur salarié, une
copie de sauvegarde, un dépôt rendu public par erreur — dispose du jeton
d'administration du stockage S3. Donc de toutes les photos de profil, de toutes
les pièces d'identité des livreurs, et de toutes les preuves de livraison.

**À faire, dans cet ordre** : roter les trois valeurs, sortir le fichier du suivi
git, injecter par variables d'environnement ou fichier monté hors dépôt. Roter
d'abord : désindexer sans roter ne fait que retirer les secrets de la vue, ils
restent dans l'historique.

Le reste des secrets est propre : `ios/Flutter/Secrets.xcconfig`,
`dart_defines.json` et `src/Services/Payment/.env` ne sont pas suivis, et une
recherche `git log -S` n'a trouvé aucune fuite dans l'historique.

## B5. Application client : crash sur iOS au premier appui sur « Prendre une photo » — CORRIGE

**Fichiers**
- `apps/client_app/ios/Runner/Info.plist` — seules `NSLocalNetworkUsageDescription`
  (`:78`) et `NSLocationWhenInUseUsageDescription` (`:87`) sont présentes
- `apps/client_app/lib/features/profil/photo_profil.dart:200` — `ImageSource.camera`

Il manque `NSCameraUsageDescription` et `NSPhotoLibraryUsageDescription`. iOS ne
demande pas la permission : il **termine le processus** à la première demande
d'accès. L'application ferme, sans message.

**À noter** : l'application livreur les a bien (`Info.plist:97` et `:99`). C'est
donc un oubli sur le client seulement, pas une méconnaissance.

Accessoirement, un binaire sans ces clés est refusé à la revue App Store, donc ce
constat bloque la publication indépendamment du crash.

## B6. Un échec réseau pendant le rafraîchissement déconnecte, et détruit la file du livreur — CORRIGE

**Fichiers**
- `apps/hba_core/lib/src/api_client.dart:151-153`
- `apps/driver_app/lib/core/providers.dart:48-63`
- `apps/client_app/lib/core/providers.dart:30-39`

`_refreshOnce` traite **toute** `DioException` comme une session perdue :

```dart
} on DioException {
  await _onSessionLost();
  return false;
}
```

Or `_send` (`:95-99`) distingue soigneusement `connectionError`,
`connectionTimeout` et `receiveTimeout` pour les transformer en
`OfflineException`. Cette distinction n'existe pas dans `_refreshOnce`. Un
timeout, un 502 pendant un redéploiement, un 429, une coupure de réseau :
tout devient « votre session a expiré ».

**Scénario livreur, et c'est le pire.** Le livreur remet un colis dans un parking
souterrain. Pas de réseau : la remise, code inclus, part dans la file hors ligne.
Il remonte, le réseau revient, son jeton d'accès a expiré (15 min). Le premier
appel reçoit 401, le rafraîchissement part sur une 3G faible et expire.
`onSessionLost` s'exécute : `providers.dart:56` **purge la file**, les jetons sont
effacés, il est à l'écran de connexion. La remise n'a jamais été enregistrée, le
code est perdu, la course reste ouverte, et le client attend un colis qu'il a déjà
reçu.

La purge est en soi une bonne décision — le commentaire aux lignes 49-54 explique
correctement pourquoi le code de remise d'un livreur éjecté ne doit pas rester sur
le téléphone. Le défaut n'est pas la purge, c'est de la déclencher sur une
coupure réseau.

**Côté client**, le même chemin (`providers.dart:30-39`) éjecte tous les clients à
chaque redéploiement de la passerelle.

**Correctif** : ne fermer la session que sur un refus explicite du serveur.
Sur réseau, 5xx ou 429, garder la session et laisser `OfflineException` remonter.

**CE PARAGRAPHE DISAIT « 400, 401 ou 403 », ET C'ÉTAIT FAUX** — corrigé le
30 septembre 2026 en écrivant le correctif, après avoir lu ce que le serveur
envoie réellement. Le 400 vient d'un `VALIDATION_FAILED`, c'est-à-dire d'une
requête mal formée par l'application : s'en servir pour fermer la session aurait
fait payer à l'utilisateur, et au livreur sa file, un bug de notre côté. La table
réelle est en annexe G.

## B7. L'APK client de production est signée avec la clé de debug — CORRIGE

**Fichier** : `apps/client_app/android/app/build.gradle.kts:47-51`

```kotlin
release {
    // TODO: Add your own signing config for the release build.
    // Signing with the debug keys for now, so `flutter run --release` works.
    signingConfig = signingConfigs.getByName("debug")
}
```

C'est le gabarit Flutter, jamais remplacé. La clé de debug est publique et
identique sur toutes les machines de développement : n'importe qui peut produire
une APK que le téléphone acceptera comme une mise à jour légitime de HBA Client,
et Play Store refusera l'envoi.

**À noter** : `apps/driver_app/android/app/build.gradle.kts:89-110` est
correctement fait — un `signingConfigs("publication")` lu depuis
`android/key.properties`, avec repli explicite et bruyant sur debug si le fichier
manque. Il suffit de transposer.

## B8. L'identifiant d'application du client est resté celui du gabarit Flutter

**Ajouté le 30 septembre 2026**, en traitant B7 : il vit dans le même fichier, et
cet audit ne l'avait pas vu. Sans lui, corriger B5 et B7 ne débloque pas la
publication — et croire le contraire est pire que de ne rien corriger.

**Fichiers**
- `apps/client_app/android/app/build.gradle.kts:22` — `namespace = "com.example.hba_client"`
- `apps/client_app/android/app/build.gradle.kts:33` — `applicationId = "com.example.hba_client"`, sous un `TODO` du gabarit
- `apps/client_app/android/app/src/main/kotlin/com/example/hba_client/MainActivity.kt` — le paquet Kotlin suit le même chemin
- le projet Xcode (`PRODUCT_BUNDLE_IDENTIFIER`), à vérifier
- `apps/client_app/android/app/src/main/AndroidManifest.xml:18` — `android:label="hba_client"`

**Conséquence.** Google Play **refuse** tout `com.example.*` : c'est le domaine
d'exemple réservé, et le refus tombe à l'envoi. L'application livreur porte
`com.hbatechettrade.hba_driver` ; le client est resté sur le gabarit.

**Pourquoi ce n'est pas corrigé dans la même passe.** Changer l'identifiant
demande de déplacer `MainActivity.kt` vers le nouveau paquet — sinon le
`.MainActivity` du manifeste ne résout plus — et de toucher au projet Xcode pour
l'identifiant de bundle. La moitié du travail serait pire que rien : une
application qui ne compile plus. C'est un chantier court mais qui doit être
entier.

**Et il y a une horloge.** L'identifiant ne se change plus après la première
publication sans perdre toutes les installations. Il est donc gratuit
aujourd'hui, et définitif demain. La convention existe déjà :
`com.hbatechettrade.hba_client`.

**Le nom affiché va avec** : `hba_client` sous l'icône, là où le livreur affiche
« HBA Livreur ». C'est le constat M21 de l'annexe C, et il se corrige dans le
même geste.

---

# SÉRIEUX

## S1. Le livreur n'a aucun moyen de déposer une preuve de livraison, et la clé n'est pas vérifiée — CORRIGE

**Fichiers**
- `src/Gateways/Hba.Gateway/Endpoints/Driver/DriverEndpoints.cs:312`, `:323`, `:345`, `:444-446`
- `src/Services/Media/Hba.Media.Api/Endpoints/MediaUploadEndpoints.cs` — `PeutDeposer`
- `src/Services/Media/Hba.Media.Domain/Assets/MediaEnums.cs:19-20`

La passerelle accepte un `ProofObjectKey` dans le corps et le transmet tel quel,
ou `string.Empty` s'il est absent :

```csharp
ProofObjectKey = body.ProofObjectKey ?? string.Empty,
```

Rien ne vérifie que la clé désigne un objet existant, du bon type, appartenant à
cette course. Et il n'existe aucune route par laquelle le livreur pourrait
déposer la photo : `MediaOwnerType.Delivery` existe bien — *« Une preuve de
livraison appartient à la course, pas à quelqu'un »* — mais `PeutDeposer` autorise
un dépôt « pour soi », et l'identifiant d'une course n'est celui de personne.

**Conséquence.** En pratique la preuve est toujours vide. Et la colonne
`DeliveryProofObjectKey` accepte n'importe quelle chaîne que le livreur veut bien
y mettre : une preuve invérifiable n'est pas une preuve.

### Ce qui a été fait le 30 septembre 2026, et ce qui ne pouvait pas l'être

**La moitié qui ne demandait aucune décision est faite.** Une clé de preuve non
vide est désormais **refusée** (`PROOF_NOT_SUPPORTED`), aux deux étapes qui en
acceptaient une. Vérifié avant d'agir : **personne n'envoie ce champ** — ni
l'application livreur, ni la console. Il est mort sur le fil, et la seule chose
qu'il pouvait porter était une chaîne inventée.

Refuser plutôt qu'ignorer, et c'est le cœur : l'ignorer en silence aurait laissé
l'appelant croire qu'une preuve était conservée ; la stocker fabriquait **une
pièce à charge inventée par celui qu'elle doit engager**. Entre les deux, la
seule réponse honnête est de dire non.

**L'autre moitié appartenait à une décision, et elle est prise depuis.**
**Le 30 septembre 2026 : la photo est PROPOSÉE, aux deux étapes.** Le livreur peut
en joindre une à la collecte et à la remise ; sans réseau, l'étape passe sans
elle, et n'y revient pas.

Le fait qui a écarté « exigée » est technique et net : **la file d'actions hors
ligne ne sait pas transporter un fichier.** Elle range du JSON dans le coffre
chiffré, plafonnée à cinquante entrées. Rendre la photo obligatoire imposait soit
de bloquer une remise sans réseau — devant un client déjà servi —, soit de
construire une file binaire avec son stockage, son plafond, sa purge et un état
« remise faite, photo en attente » qui n'existe pas. Le raisonnement complet, y
compris pourquoi « exigée à la collecte seulement » a été écartée, est sous le
point 7.

**La rétention et la visibilité sont tranchées le 30 septembre 2026, et
construites** : la preuve est gardée **un mois**, et **l'`admin` seul** peut la
revoir.

- `MediaAccess.PeutVoirCeGenreDeMedia` refuse `DeliveryProof` à tout le monde
  sauf `admin` et `service` — donc à `ops`, `support` et `finance`, qui lisent
  pourtant tous les autres médias.
- `ListMediaHandler` retire les preuves de l'inventaire rendu à un appelant qui
  n'y a pas droit : la fiche seule apprendrait qu'une photo existe, quand elle a
  été prise et par qui.
- `PurgeDesPreuves` (`Hba.Media.Api/Scheduling`) balaie toutes les six heures et
  efface **l'objet d'abord, la ligne ensuite** — l'inverse exact de l'ordre du
  dépôt. Une ligne sans objet se voit et se rejoue ; un objet sans ligne est un
  fichier que plus rien ne nomme, c'est-à-dire ce que la rétention devait
  empêcher.
- Le `service` passe, et il le faut : c'est par là que Delivery autorisera un
  jour un donneur d'ordre à revoir la preuve de SA course, avec un jeton de
  service, comme l'exige le point 27.

**Conséquence à connaître, et elle a été dite avant d'écrire la règle** : un agent
du `support` qui traite une réclamation ne verra pas la photo et devra passer par
un `admin`. Le référentiel interdit d'élargir de soi-même à `ops` ou `support` ;
l'élargissement se fait en ajoutant un rôle dans `PeutVoirCeGenreDeMedia`, et
nulle part ailleurs. La matrice est consignée dans
`docs/architecture/matrice-visibilite.md`, section 3.

**Le chemin de dépôt est tranché et construit le 30 septembre 2026 : DELIVERY
PORTE LES OCTETS.**

`POST /api/driver/v1/missions/{id}/proof?etape=collecte|remise` → la passerelle
relaie le multipart vers `POST /internal/v1/deliveries/{id}/proof` en reportant
le jeton du livreur → Delivery vérifie **sur son agrégat** que c'est le livreur
affecté et que l'étape a eu lieu → pousse le fichier chez Media **avec un jeton
de service** → rattache l'identifiant rendu à la course.

- **La passerelle ne détient aucun jeton de service**, et n'en détiendra pas :
  lui en donner un ferait d'elle un appelant de confiance pour Media, et la
  moindre faille chez elle ouvrirait tous les médias, pièces d'identité
  comprises.
- **Media s'ouvre en écriture au `service`** pour un seul couple —
  `MediaOwnerType.Delivery` + `MediaKind.DeliveryProof` —, couple qu'aucun jeton
  d'utilisateur ne peut atteindre puisqu'une course n'a pas de compte.
- **Les deux colonnes changent de nature** : `PickupProofObjectKey` et
  `DeliveryProofObjectKey` (chaînes) deviennent `PickupProofMediaId` et
  `DeliveryProofMediaId` (uuid). Le point 27 veut que la clé de stockage ne sorte
  pas de Media. La migration `ProofMedia` **refuse de s'appliquer** si une clé
  est enregistrée quelque part, plutôt que de détruire en silence une donnée
  qu'elle n'attendait pas.
- **L'ordre est : Media d'abord, agrégat ensuite.** Si la sauvegarde échoue après
  le dépôt, il reste un orphelin que la purge à trente jours balaie — il est dans
  l'inventaire. L'ordre inverse écrirait sur la course l'identifiant d'un fichier
  qui pourrait ne jamais exister, c'est-à-dire une preuve invérifiable : ce que
  ce constat refuse par ailleurs.
- **`PROOF_NOT_SUPPORTED` reste en place** sur `MarkPickedUp` et
  `ConfirmDelivery`. Ces deux-là portent encore une chaîne venue du téléphone que
  rien ne peut vérifier ; le jour où plus personne ne l'envoie, c'est le champ du
  contrat qui doit disparaître, pas ce refus.

**L'écran livreur est construit le même jour.** Un bouton « Ajouter une photo
(facultatif) » au-dessus de la glissière, à la collecte et à la remise : la photo
se prend AVANT le geste et part APRÈS, de sorte qu'un envoi raté ne défasse
jamais l'étape. Sans réseau, l'étape part et la photo est abandonnée — et l'écran
le dit, plutôt que de laisser croire qu'elle est jointe.

**La fenêtre de dépôt est tranchée le même jour : trente minutes**
(`Delivery.FenetreDeDepotDeLaPreuve`). Six minutes pour l'envoi dans le pire cas —
trois de délai, doublées par le rejeu corrigé en S7 — et le reste pour absorber
une horloge de téléphone qui retarde, la soustraction mêlant l'horodatage du
téléphone (l'étape) et celui du serveur (le dépôt). Ce mélange est **assumé, pas
ignoré** : la réparation propre coûte deux colonnes et une migration, et si le
refus se voit en exploitation, c'est ce chantier qu'il faut ouvrir plutôt que
d'augmenter le nombre. Le point 7 porte le raisonnement complet.

**Ce qui fait foi en attendant n'a pas bougé** : le code de remise dicté par le
destinataire, dont l'ADR 0005 fait la preuve de livraison. La photo est un
complément, pas un substitut — et le verrou des cinq tentatives de ce code a été
réparé le même jour (B1).

## S2. Annuler une course sans réseau : silence complet — CORRIGE

**Fichier** : `apps/client_app/lib/features/deliveries/annulation.dart:62-64`

```dart
} on ApiException catch (erreur) {
  messager.showSnackBar(SnackBar(content: Text(erreur.message)));
}
```

`OfflineException`, levée par `api_client.dart:95-99`, n'est pas attrapée. Les
appelants (`tracking_screen.dart:79` et `:208`, `home_screen.dart:354`) invoquent
la fonction en `unawaited` : l'exception part dans la zone d'erreur Flutter et
personne ne la voit.

**Scénario.** Le client confirme « Annuler la course » dans une zone sans réseau.
Aucun message, aucune indication d'échec. Il range son téléphone. Le livreur, lui,
est en route et arrive : personne n'a rien annulé.

## S3. Passer hors ligne en échec laisse une interface « en ligne » morte — CORRIGE

**Fichier** : `apps/driver_app/lib/features/home/home_screen.dart:388-405`

L'ordre des opérations coupe tout **avant** l'appel réseau, et `_online` n'est mis
à jour qu'**après** :

```dart
_poller?.cancel();
_heartbeat?.cancel();
_positionAcceptee = null;
await ServiceEnLigne.arreter();
await repository.goOffline();      // leve ici s'il n'y a pas de reseau
...
if (mounted) setState(() => _online = value);   // jamais atteint
```

**Scénario.** Le livreur finit sa journée dans un endroit sans réseau et appuie
sur « hors ligne ». `OfflineException` est attrapée (`:404`), un message
« Pas de réseau. Réessayez. » s'affiche, et la bascule **reste sur « en ligne »**.
Mais plus rien ne tourne : ni sondage, ni battement, ni service de premier plan.
Le serveur le garde disponible pendant la fenêtre de fraîcheur (120 s) et peut lui
envoyer une offre qu'il ne verra jamais. Le client, en face, attend un livreur qui
a arrêté.

## S4. Une offre expirée reste à l'écran et suspend toutes les suivantes — CORRIGE

**Fichiers**
- `apps/driver_app/lib/features/missions/offer_sheet.dart:50-58`
- `apps/driver_app/lib/features/home/home_screen.dart:564`, `:584`, `:686`

Le choix de laisser la feuille ouverte à l'expiration est assumé et documenté —
le livreur doit voir *pourquoi* le bouton est mort. Le problème est ailleurs : le
sondage s'arrête tant que la feuille est ouverte.

```dart
if (_sheetOpen || _mission != null || _attendLaCourse) return;   // :564
```

`_sheetOpen` ne repasse à `false` qu'à la fermeture (`:686`), qui n'a lieu que si
le livreur appuie sur Refuser.

**Scénario.** Le livreur pose son téléphone pendant deux minutes. Une offre
arrive, sonne, expire. La feuille « Offre expirée » reste affichée. Le battement
de position continue, donc le serveur le croit disponible et lui adresse les
vagues suivantes — qu'aucun sondage ne va chercher. Il peut rester une demi-heure
« en ligne, disponible, ne recevant rien », et chaque course concernée perd
30 secondes de recherche.

## S5. Aucune resynchronisation de l'état opérationnel au retour au premier plan — CORRIGE

**Fichier** : `apps/driver_app/lib/features/home/home_screen.dart:117`, `:145`, `:294`

`_reprendreLEtatDuServeur` — la fonction ajoutée hier précisément pour relire
l'état réel — n'a **qu'un seul appelant**, un `Future.microtask` à l'initialisation
de l'écran (`:117`). `didChangeAppLifecycleState` (`:145`) ne l'appelle pas. Elle
échoue aussi en silence (`:323`).

**Deux précisions ajoutées le 30 septembre 2026, en traitant le constat.**
`didChangeAppLifecycleState` **existe** et appelle `_reprendre()`, qui relit la
mission, le profil, la position et vide la file d'attente : ce qui manquait était
la seule lecture de l'état **opérationnel**, pas toute la reprise. Et le défaut
était plus profond que « une fois au démarrage » : la fonction ne savait aller que
dans un sens — rattraper un serveur en ligne face à un écran hors ligne. L'inverse,
un livreur suspendu ou mis hors ligne ailleurs, n'était jamais constaté.

**Scénarios.**
- Démarrage sans réseau : l'appel échoue sans un mot, l'écran se pose sur « hors
  ligne » alors que le serveur tient peut-être le livreur disponible. Rien ne
  rattrape au retour du réseau.
- Le livreur est suspendu par le back-office, ou forcé hors ligne, ou passé en
  `RESERVED` depuis un autre appareil, pendant que l'application est en arrière-plan.
  Au retour, l'écran affiche toujours l'état d'avant.

## S6. Les données d'un compte restent visibles par le suivant sur le même téléphone — CORRIGE

**Fichiers**
- `apps/client_app/lib/features/deliveries/delivery_providers.dart:12`, `:30`, `:33`
- `apps/client_app/lib/core/providers.dart:30-39`
- `apps/client_app/lib/features/profil/…` — `profilProvider`, `photoProvider`,
  `adressesProvider`, `whatsAppProvider`, `suppressionProvider`

Aucun de ces providers n'est `autoDispose`, et ni `onSessionLost` ni
`SessionController.signedOut()` n'en invalide un seul. Les seules invalidations du
dépôt sont locales à un écran, après une modification.

**Scénario.** Deux personnes partagent un téléphone, ou un vendeur en fait la
démonstration. Le compte A se déconnecte, le compte B se connecte. B voit le
profil de A, ses adresses enregistrées, ses courses, et les numéros de téléphone
des destinataires de A. Si `courseEnAttenteProvider` porte encore l'identifiant
d'une course de A, l'accueil de B se bloque sur un chargement sans fin (le
`deliveryProvider` de cette course répond 403 ou 404, et rien ne le rattrape).

### Corrigé le 30 septembre 2026 — une dépendance, pas une liste

**`apiClientProvider` surveille désormais `compteConnecteProvider`** — un booléen
« quelqu'un est-il connecté ». Tout ce qui porte des données du compte passe par
un dépôt, tout dépôt passe par ce client : un changement de compte les reconstruit
tous, y compris ceux qui n'existent pas encore.

**Pourquoi pas une liste de providers à vider à la déconnexion.** Elle aurait été
juste le jour où on l'écrit, et fausse au premier provider ajouté ensuite —
c'est exactement comme cela que ce défaut est né. Une dépendance ne s'oublie pas.

**Pourquoi un booléen et non la session.** `SessionSignedIn` est reconstruit à
chaque renommage : surveiller l'état entier ferait repartir toutes les requêtes
de l'application parce que le client a corrigé son prénom. Un changement de
compte, lui, passe forcément par « déconnecté ».

**Ce que le mécanisme ne couvre pas**, et qui est traité à part dans
`SessionController.signedOut()` — par où passent les deux sorties, le bouton et
la perte de session : `courseEnAttenteProvider`, qui ne vient d'aucune requête.
C'est lui qui provoquait le chargement sans fin chez le compte suivant. Il est
vidé **avant** la bascule d'état, sinon un écran reconstruit entre les deux lirait
encore l'identifiant de l'ancien compte.

Éprouvé par `apps/client_app/test/oubli_du_compte_test.dart` : le client d'API est
bien reconstruit à la déconnexion, les dépôts qui en dépendent aussi, la course en
attente est vidée — et un simple renommage ne jette rien.

### CE QUE CE CONSTAT DISAIT DE FAUX

**« L'application livreur a le même défaut »** — vérification faite, **non**. Tous
ses providers de données (relevé, demandes, profil, courses, dossier) sont
`autoDispose`, le routeur renvoie vers la connexion dès que la session tombe, et
la file d'actions est purgée aux deux sorties. Rien n'y fuyait.

La même ligne y a tout de même été ajoutée, et c'est de la **prévention, pas de
la réparation** : cette sûreté-là tient à une habitude — que chaque provider
ajouté pense à `autoDispose` —, et le premier qui l'oubliera fera réapparaître le
défaut sans que rien ne le signale.

## S7. Un téléversement rejoué après 401 échoue systématiquement — CORRIGE

**Fichiers** : `apps/hba_core/lib/src/api_client.dart:60-79` et `:102-106`

`_send` rejoue la fermeture d'origine après un rafraîchissement réussi :

```dart
if (error.response?.statusCode == 401 && allowRetry) {
  if (await _refreshOnce()) {
    return _send(call, allowRetry: false);   // rejoue la MEME FormData
  }
}
```

Une `FormData` Dio est un flux à usage unique. Le second envoi lève
« FormData already finalized », transformée en « Une erreur inattendue s'est
produite. »

**Scénario.** Le livreur remplit son dossier tranquillement, prend ses photos, et
appuie sur « Envoyer mon dossier » plus de 15 minutes après sa dernière requête.
Le jeton a expiré. Premier envoi : 401. Rafraîchissement : réussi. Rejeu : échec
incompréhensible. Il recommence, et cette fois ça marche — sans qu'il comprenne
pourquoi. Même chose pour la photo de profil côté client
(`profil_providers.dart:105-117`).

### Corrigé le 30 septembre 2026 — une fabrique, pas une instance

**`upload` ne reçoit plus une `FormData`, il reçoit de quoi en faire une** :
`required Future<FormData> Function() formulaire`, appelée **à l'intérieur** de la
closure que `_send` rejoue. Chaque tentative construit donc son propre corps.

`clone()` aurait aussi marché, mais la documentation de Dio dit autre chose, et
elle dit mieux : *« You should make a new FormData or MultipartFile every time in
repeated requests. »* La fabrique est la seule forme où **l'appelant ne peut pas
se tromper** — avec une instance, il fallait y penser à chaque nouvel appelant, et
le prochain ne l'aurait pas su.

Les quatre appelants sont convertis : les deux du dossier livreur, la photo de
profil du client, et le dépôt de preuve écrit le même jour — qui héritait du
défaut sans que personne ne l'ait vu.

Éprouvé par `apps/hba_core/test/rejeu_du_televersement_test.dart` : un adaptateur
HTTP factice répond 401 au premier dépôt, 200 au second, et le test vérifie que la
fabrique a bien été **rappelée**, que le second envoi atteint le serveur, et que
la session n'est pas fermée au passage — côté livreur, fermer la session purge la
file d'actions hors ligne.

## S8. La suspension d'un livreur ne libère pas sa course en cours

**Fichier** : `src/Services/Driver/Hba.Driver.Domain/Drivers/DriverAggregate.cs:388-405`

`Suspend` passe le statut à `Suspended` et appelle `ForceOffline`. Il ne regarde
pas si le livreur est `RESERVED` ou en mission, et ne déclenche rien du côté
Dispatch ni Delivery.

**Scénario.** Le support suspend un livreur signalé, alors qu'il porte un colis.
La course reste affectée à un compte suspendu. Elle ne sera ni reprise par le
dispatch — elle n'est plus en attente — ni terminée par lui. Elle attend une
intervention manuelle que rien ne signale.

La suspension n'est pas non plus propagée à Identity : son jeton reste valable
jusqu'à expiration.

## S9. Une position est acceptée sans regarder l'état du livreur

**Fichier** : `src/Services/Driver/.../Commands/UpdateDriverLocation.cs`

L'autorisation est correcte (`DriverAuthorization.EnsureSelfOrBackOffice`), et le
choix de se fier à l'heure de réception plutôt qu'à l'horodatage du téléphone est
bien raisonné. Mais le handler ne charge jamais le livreur : il écrit dans l'index
GEO Redis quel que soit son statut.

**Conséquence.** Un livreur hors ligne, suspendu ou rejeté qui continue à pousser
sa position se réinscrit dans l'index. Le filtre `Verified + Available` en base
rattrape le dispatch, donc il ne reçoit pas d'offre — mais l'index se remplit de
candidats qui n'en sont pas, et les positions périmées ne sont jamais purgées.
Comme la limite GEO est appliquée **avant** le filtre de fraîcheur
(`RedisDriverLocationStore.cs:86-102`, `FindAvailableNearby.cs:34-53`), dans une
zone dense les 50 candidats peuvent tous être périmés pendant qu'un livreur frais
un peu plus loin n'est jamais considéré.

## S10. L'acceptation d'une offre n'est pas rejouable

**Fichier** : `src/Services/Dispatch/.../Commands/AcceptOffer.cs:19`

```csharp
public sealed record AcceptOfferCommand(Guid OfferId, string? IdempotencyKey) : ICommand<AcceptOfferResult>;
```

`IdempotencyKey` est déclarée et **jamais lue** par le handler. Les trois barrières
contre la concurrence (verrou, garde d'agrégat, jeton optimiste) sont solides, mais
elles répondent à « deux livreurs » et pas à « le même livreur deux fois ».

**Scénario.** Le livreur accepte, le serveur enregistre, la réponse se perd. Il
relance. La seconde requête reçoit `ALREADY_TAKEN` : il croit avoir perdu l'offre
qu'il vient de gagner, et la mission l'attend sans qu'il le sache.

## S11. Le compteur d'essais OTP d'Identity n'est pas atomique

**Fichier** : `src/Services/Identity/.../Services/Otp/RedisOtpStore.cs:35`, `:42`

Le défi est lu (`StringGetAsync`) puis réécrit (`StringSetAsync`) : un cycle
lecture-modification-écriture sans atomicité. Le compteur horaire, lui, utilise
correctement `StringIncrementAsync` (`:116`).

**Conséquence.** Des vérifications parallèles lisent le même compteur et le
`MaxAttempts = 5` d'`OtpChallenge` est dépassé. Le code OTP étant court, une rafale
de requêtes concurrentes rend la force brute réaliste. À corriger par `INCR` ou un
script Lua.

## S12. `FindAvailableNearby` ne vérifie pas son appelant côté service

**Fichiers** : `src/Services/Driver/Hba.Driver.Api/Grpc/DriverGrpcService.cs:29`,
`.../Queries/FindAvailableNearby.cs`

La classe porte un `[Authorize]` nu, sans rôle, et le handler ne contient aucune
mention de l'appelant. Tout jeton valide qui atteint le port gRPC obtient des
identifiants de livreurs proches. La passerelle filtre, mais c'est précisément ce
que le référentiel refuse comme unique garantie.

À distinguer de `GetDriverHandler`, qui **est** correctement gardé (dossier complet
réservé au livreur lui-même ou au back-office), et de
`GetDriverPublicProfileHandler`, dont l'absence de filtre est déjà documentée dans
le code comme un point à trancher.

## S13. Le rôle `service` peut être posé sur un compte de personne — CORRIGE

**Fichier** : `src/Services/Identity/Hba.Identity.Domain/Accounts/Account.cs:281-285`

`SetRoles` refuse explicitement `partner` — *« Le rôle partner appartient à un
client OAuth, pas à un compte de personne »* — mais laisse passer `service`, que
`Normalize` accepte puisqu'il est dans `Roles.All`.

**Conséquence.** Le rôle `service`, introduit hier, contourne par construction
tous les contrôles de propriété : `BillingAccess.EnsureCanReverse`,
`DirectoryAccess`, la suppression de médias. Un compte admin compromis — ou un
administrateur malveillant — s'octroie `service` et n'est plus arrêté par rien.

C'est un effet de bord direct de l'ajout du 30/09 : l'acteur système a été ajouté
à `All` et retiré de `BackOffice`, mais la porte de `SetRoles` est restée ouverte.
Le correctif est symétrique à celui de `partner`, trois lignes.

## S14. La limitation de débit est contournable si la passerelle est joignable hors Traefik

**Fichiers** : `src/Gateways/Hba.Gateway/appsettings.json:62-63`,
`src/BuildingBlocks/Hba.BuildingBlocks.Security/HbaRateLimiting.cs:90-99`

`TrustAllProxies: true` vide `KnownNetworks` et `KnownProxies`, donc
`X-Forwarded-For` est cru sur parole. Toutes les limites sont partitionnées par
adresse client : OTP 10 par 10 min, tentatives d'authentification 30 par 5 min.

Le commentaire du code pose bien la condition — *« ne doit être vrai que lorsque
le BFF n'est joignable QUE par le routeur »*. Le constat est donc conditionnel, et
**deux choses restent à vérifier avant la mise en production** : que Traefik
écrase bien `X-Forwarded-For` au lieu d'y ajouter, et que le port de la passerelle
n'est publié nulle part ailleurs. Si l'une des deux tombe, un attaquant change
d'en-tête à chaque requête et il n'y a plus de limite : force brute sur l'OTP, et
pompage de SMS payants.

Dans le même esprit, `deploy/traefik/dynamic.yml` définit un `partner-rate-limit`
qui n'est appliqué à aucune route.

## S15. La file d'actions traite une panne de service comme un refus — CORRIGE

**Ce constat n'avait pas de numéro jusqu'au 30 septembre 2026**, et c'est une
faute de ce rapport : il était décrit dans l'annexe G et dans les échanges sous
le nom de « S1 », qui désigne déjà tout autre chose. Deux constats sous le même
nom, c'est une correction qui part au mauvais endroit.

**Fichiers**
- `apps/driver_app/lib/core/file_actions.dart` — la branche `on ApiException` de `vider`
- `apps/hba_core/lib/src/api_client.dart` — `_toApiException`

**Le défaut.** La file abandonnait **toute** `ApiException` comme un refus
définitif. Le raisonnement écrit là était juste — un refus ne se rejoue pas, et
rejouer indéfiniment bloquerait la file — mais il reposait sur une prémisse
fausse : que recevoir une réponse du serveur signifie qu'il a *jugé* l'action.
`_toApiException` range dans la même classe les 500, 502, 503, 504 et 429.

**Le scénario.** Le livreur remet un colis hors réseau ; la remise part en file.
Le réseau revient pendant un redéploiement de la passerelle, qui répond 502.
L'entrée est retirée de la file et perdue. **Le client a son colis, la course
reste ouverte, et le livreur lit « une erreur inattendue ».** C'était le dernier
constat ouvert qui détruisait une donnée déjà acquise.

**La correction.** La question n'est pas « y a-t-il eu une erreur » mais **« qui
a décidé »**. `ApiException.estUnRefusDuServeur` tranche : un 4xx est un
jugement, tout le reste ne l'est pas. Une panne se traite alors exactement comme
une absence de réseau — on s'arrête, on ne perd rien, et le battement de position
rappelle `vider` dans les vingt secondes.

**Les cas douteux sont du côté prudent**, c'est-à-dire traités comme des pannes :
401 (qui ne devrait jamais arriver là — `ApiClient` rafraîchit, et une session
réellement perdue purge la file), 408 et 429 qui disent « pas maintenant » et non
« non », et un statut absent. On ne jette pas une preuve de livraison sur une
incertitude.

**Éprouvé** : `apps/hba_core/test/refus_du_serveur_test.dart`, avec un balayage
exhaustif de 100 à 599 qui fixe la frontière, et non quelques cas choisis.

---

# Ce que les tests ne couvrent pas — et pourquoi cela compte ici

Ce n'est pas une remarque d'hygiène. Trois des constats ci-dessus existent
uniquement parce que rien ne les aurait attrapés.

**Backend.** `src/Services/Driver/tests` et `src/Services/Dispatch/tests` existent
et sont **vides** (0 fichier, créés le 21/09). Restent donc sans filet : toute la
machine d'états du livreur (`GoOnline`, `Reserve`, `StartMission`,
`CompleteMission`, `Suspend`, `Reinstate`), la fraîcheur de position et la
recherche GEO, le verrou d'acceptation et l'expiration d'offre, et l'enchaînement
Kafka `OfferSent` / `OfferExpired` / `OfferAccepted` qui pilote la réservation.
S8, S9, S10 auraient tous été attrapés par un test d'intégration simple.

Côté Delivery, B1 montre le cas plus insidieux : le test existe, il est vert, et il
valide une propriété que le système n'a pas, parce qu'il travaille en mémoire là où
la production passe par une base.

**Mobile.** Il n'y a **aucun workflow Flutter** dans `.github/workflows` : rien ne
compile les applications, rien ne lance les tests. C'est ce qui a permis à une
régression de compilation de dormir une journée sur le disque (annexe D). Les
`analysis_options.yaml` se limitent à `flutter_lints` avec `rules:` vide, sans
`unawaited_futures` — la règle qui aurait signalé S2 — et `hba_core` comme `hba_ui`
n'en ont pas du tout.

Ce qui existe : `driver_app/test/models_test.dart` et `file_actions_test.dart`,
`client_app/test/models_test.dart` et un `widget_test.dart` qui ne couvre que
`RechercheEnCours`, plus deux tests dans `hba_ui`. `hba_core` n'a pas de dossier
`test/` : ni `ApiClient` (donc ni le 401, ni le rafraîchissement, ni B6), ni
`TokenStore`, ni `PhonePlan`.

**Le plus rentable, dans l'ordre** : un test d'intégration Delivery sur le code de
remise (attrape B1), un test Pricing sur la cohérence devis-trajet (attrape B2),
un projet de tests Driver avec la machine d'états, et une chaîne d'intégration
continue qui lance `flutter analyze` et `flutter test` sur les quatre paquets.

---

# Annexes

## Annexe A — Corrigé depuis le 29/09, vérifié dans le code, non re-signalé

**Client** : purge des comptes à la suppression (C1), reconnexion pendant le délai
de grâce (C3), isolation de la clé d'idempotence par commanditaire (C4),
`MerchantId` du corps ignoré côté client (C5), sortie de `NO_DRIVER_FOUND` par le
bouton « Compris » (C6), clé d'idempotence régénérée quand le formulaire change
(C8), pré-contrôle du compte de facturation avant consommation du devis.

**Livreur** : réintégration après suspension (L2), libération du livreur resté
`RESERVED` (L1), chemin d'incident, profil recréé sur 404, idempotence des actions
livreur sur le succès seulement, file hors ligne sélective avec estampille de
propriétaire et coffre chiffré, contrôle d'expiration dans `Offer.Accept`, crédit
des gains sur `Delivered` uniquement, refus rejoués remontés à l'écran via
`BilanDeFile`, `try/catch` autour de `onAccept` et `onDecline`, annulation côté
Dispatch qui supersède les offres en attente, localisation FR.

**Facturation** : `ReverseDebit` refuse le titulaire, verrou `FOR UPDATE` sous
transaction réelle, `SELECT *, xmin`, surface d'exploitation et console.

**Sécurité** : `DeleteOwnerMedia` gardé, acteur `service` en place, masquage du
téléphone du destinataire à la clôture, `DeliveryAccess` / `BillingAccess` /
`DriverAuthorization` / `DirectoryAccess` corrects, webhook FedaPay signé,
rotation des jetons de rafraîchissement avec détection de rejeu.

## Annexe B — Constats d'agents écartés après relecture

À ne pas reprendre : ils ont été vérifiés et sont faux.

- *« Le devis ne porte pas les points du trajet »* — faux, `Quote` a bien `Pickup`
  et `Dropoff`. Le défaut réel est que personne ne les compare (B2), ce qui rend
  le correctif beaucoup moins cher qu'annoncé.
- *« `GetDriver` ne vérifie pas son appelant »* — faux, `GetDriverHandler` est
  correctement gardé. Seuls `FindAvailableNearby` (S12) et
  `GetDriverPublicProfile` (déjà documenté comme à trancher) ne le sont pas.
- *« `UpdateDriverLocation` n'a aucune autorisation »* — faux,
  `DriverAuthorization.EnsureSelfOrBackOffice` est appelé. Le défaut est l'absence
  de contrôle d'**état** (S9).
- *« Le nom du livreur reste visible après la clôture »* — exact au sens littéral,
  mais ce n'est pas une fuite : le client garde le nom du livreur dans son
  historique, le téléphone est bien masqué. À trancher si l'on veut le retirer,
  pas à corriger.
- *« Media accepte n'importe quel fichier »* — inexact : taille, liste blanche de
  types et contrainte « images seulement » sont vérifiées, et `PeutDeposer`
  contrôle le propriétaire. Il manque le contrôle des octets d'en-tête, ce qui est
  mineur (annexe C).

## Annexe C — Mineur

Robustesse mobile : `Geolocator.getCurrentPosition()` sans `timeLimit` aux quatre
appels (spinner sans fin en intérieur, et demande de permission répétée toutes les
20 s si elle est révoquée) ; deux feuilles d'offre empilables faute de garde
« déjà en cours » ; `MissionScreen` qui ne relit jamais la course ; onglet Courses
livreur dont le « tirez vers le bas » est impossible sur un contenu non défilable ;
écran du code SMS sans « Renvoyer » ni expiration alors que `expiresAt` et
`retryAfterSeconds` sont déjà lus ; absence de `retrieveLostData` après la caméra ;
polices Google chargées à l'exécution et remontées en plantage fatal ; comparaisons
d'expiration contre `DateTime.now()` sensibles au décalage d'horloge du téléphone ;
poids illisible remplacé en silence par 1000 g ; téléphones non normalisés malgré
`PhonePlan` ; `state.extra!` qui plante à la restauration de route ;
`Image.network` sans `cacheWidth` ; trois `GoogleMap` vivants simultanément ;
base d'API par défaut `http://localhost:5100` dans les deux applications.

Sécurité : octets d'en-tête non vérifiés au téléversement, et pas de
`Content-Disposition` forcé ; secret du webhook partenaire rendu en clair au
back-office ; `TokenForwardingInterceptor` limité aux appels unary ; pas de
`ValidAlgorithms` explicite (risque faible, JWKS RS256) ; pas de `allowBackup=false`
ni de `FLAG_SECURE` sur les écrans OTP et documents ; code de remise copié dans le
presse-papiers, lisible par d'autres applications ; `isMinifyEnabled = false` côté
livreur ; compose de développement qui publie Postgres, Redis, Kafka et l'admin
Garage sur toutes les interfaces ; RPC OTP anonymes d'Identity sans limitation de
débit propre (à confirmer que le port n'est jamais publié).

Backend : `DispatchIntegrationEventPublisher` qui publie `OfferExpired` pour
`Superseded` et `Declined` ; pas de pagination exposée sur « Mes courses » dans les
deux applications ; poids du colis ignoré à la tarification ; devis consommé avant
la création de l'intention de paiement, donc devis perdu si Payment est lent ;
aucun rapprochement des paiements si un webhook FedaPay est perdu ; bandeau
`PENDING_PAYMENT` sans aucune action possible côté client ; message « Aucun montant
n'a été prélevé » qui n'est pas garanti dans le cas d'un débit opérateur tardif.

## Annexe D — Correction faite pendant l'audit

`apps/driver_app/lib/core/file_actions.dart:305` renvoyait `0` dans une fonction
déclarée `Future<BilanDeFile>` : c'était ma propre régression, laissée sur le
disque hier en changeant le type de retour de `vider`. L'application livreur ne
compilait pas, et `file_actions_test.dart` ne pouvait pas tourner non plus. Corrigé
en `BilanDeFile.vide`, avec la documentation de la fonction remise d'accord. Rien
d'autre n'a été modifié pendant cet audit.

C'est la meilleure démonstration du point sur l'intégration continue : une erreur
de compilation franche a survécu vingt-quatre heures.

---

## Annexe E — B1 et B2, corrigés le 30 septembre 2026

**B1 — le verrou du code de remise.** `Delivery.ConfirmDelivery` **rend**
désormais son issue (`DeliveryConfirmation`) au lieu de lever `INVALID_OTP` ; le
socle des actions livreur enregistre dans tous les cas et ne mémorise la clé
d'idempotence qu'au succès ; `ConfirmDeliveryHandler` lève le refus **après** la
sauvegarde. Le livreur reçoit le même code d'erreur qu'avant : rien ne change dans
les applications.

Le filet est neuf et il est au bon endroit : `ConfirmDeliveryPersistenceTests`,
dans `Hba.Delivery.Integration.Tests`, rejoue la séquence sur un vrai PostgreSQL
avec **un contexte neuf par requête** — cinq refus, le verrou, le bon code qui ne
passe plus, le rejeu d'un refus qui reste un refus, le rejeu d'un succès qui reste
un succès. Le test de domaine trompeur a été corrigé et porte maintenant, écrit
dans son propre commentaire, ce qu'il ne prouve pas. Cible : `make test-delivery-integration`.

**B2 — le prix et le trajet.** `Quote.Consume` reçoit les deux points de la
livraison et refuse au-delà de 250 m d'écart à l'un des bouts
(`QUOTE_TRIP_MISMATCH`). Le contrôle est chez Pricing, seul à savoir ce qu'il a
chiffré. La chaîne a été remontée entièrement : `ConsumeQuoteRequest` porte le
trajet, `PricingGrpcService` refuse une requête sans points, `IPricingClient` et
`PricingGrpcClient` transportent les deux `GeoPoint`, et `PricingGrpcClient`
distingue le nouveau code de `QUOTE_NOT_USABLE` — un devis expiré se redemande,
un trajet qui a changé exige de reprendre la saisie.

**Pricing a désormais des tests, et c'étaient les premiers.**
`Hba.Pricing.Domain.Tests` a été créé (dossier `tests/` vide depuis le 21/09) et
inscrit dans la solution : dix cas, dont l'attaque elle-même, la tolérance à
100 m, le refus à 1 km, l'ordre expiration-puis-trajet, et la distance haversine.
Cible : `make test-pricing`.

**Le piège en inscrivant un projet de tests dans la solution.** Chaque service a
DEJA un dossier de solution `tests` déclaré, même quand le répertoire sur disque
est vide — c'est le cas de Pricing, Driver et Dispatch. En créer un second sous le
même parent fait échouer la solution entière sur `MSB5004 : le fichier solution
contient deux projets nommés "tests"`, avant toute compilation. Il faut rattacher
le nouveau projet au dossier existant. Les identifiants, pour les deux projets de
tests que cet audit recommande de créer :

| Service | Dossier de solution `tests` |
|---|---|
| Pricing | `{2328788A-BFCC-5D4C-A94E-58E17F004769}` |
| Driver | `{15A22E95-26FF-5F8D-B8B2-5A78426BD846}` |
| Dispatch | `{B5AFFBBC-B032-5378-AE89-C3FDF9CB9FF9}` |

**Ce que cela ne couvre pas.** Le devis n'a toujours pas de propriétaire : un
client qui connaîtrait l'identifiant du devis d'un autre peut encore le consommer
et faire échouer sa commande. Cela demande une colonne et une migration, et c'est
une nuisance, non une fuite d'argent — c'est resté dehors, volontairement.

## Annexe F — B4, corrigé le 30 septembre 2026

**Ce que la vérification a trouvé de pire que prévu.** Le dépôt est public
(`"private": false`), il a été poussé, et le fichier était servi en HTTP 200 par
raw.githubusercontent.com. Les trois secrets — `rpc_secret`, `admin_token`,
`metrics_token` — étaient donc sur Internet, pas seulement « dans le dépôt ».

**Ce que la vérification a trouvé de meilleur que prévu.** Le compose de
production ne publie que 80 et 443, sur Traefik : l'API d'administration de
Garage n'était joignable depuis Internet à aucun moment. L'exploitation
immédiate était donc fermée. Le compose de développement, lui, publie 3900 et
3903 sur toutes les interfaces (constat S11 de l'annexe C) : sur un réseau
partagé, n'importe qui pouvait lire le jeton sur GitHub et administrer le Garage
de développement.

Le vrai défaut était en aval : `compose.infra.yml` monte ce même fichier, et le
commentaire qui promettait « en production, elle se régénère » ne désignait rien
qui régénérait quoi que ce soit. La production allait démarrer avec un secret
public.

**Ce qui a été fait.** Les trois valeurs sont sorties du fichier et arrivent par
`GARAGE_RPC_SECRET`, `GARAGE_ADMIN_TOKEN` et `GARAGE_METRICS_TOKEN`, lues dans
`deploy/.env` qui n'est pas versionné. Garage accepte les trois formes — valeur,
`*_file`, variable — et la variable prime. Les deux composes les passent sous la
forme `${VAR:?message}`, donc un `.env` incomplet fait échouer `docker compose`
avec l'instruction à suivre, au lieu de démarrer Garage avec un jeton vide et
une API d'administration ouverte.

`scripts/garage-secrets.sh` (et `make garage-secrets`) pose ou fait tourner les
trois valeurs sans jamais en afficher une : il imprime une empreinte SHA-256
tronquée, de quoi comparer deux machines sans faire passer un secret par un
terminal. Les trois ont été rotés le 30 septembre 2026 ; les valeurs publiques
n'ouvrent donc plus rien.

**Le fichier reste suivi par git, et c'est délibéré** — contrairement à ce que
recommandait le constat B4 lui-même. Une fois les secrets partis, `garage.toml`
n'est que de la configuration : le désindexer rendrait la configuration du
stockage invisible à la revue sans rien protéger. Ce qui devait sortir du dépôt,
ce sont les secrets, pas le fichier.

**L'historique n'a pas été réécrit, et n'a pas à l'être pour ces trois valeurs.**
Elles sont rotées, donc sans valeur : les réécrire hors de l'historique ne
protégerait plus rien, et une réécriture sur un dépôt public déjà cloné ne
garantit de toute façon rien. C'est pour cela que la rotation passe en premier.

**Corrigé au passage.** Le commentaire du fichier affirmait que le jeton
d'administration était « utilisé par le script d'initialisation ». C'est faux :
`scripts/garage-init.sh` passe par la CLI dans le conteneur, qui lit le fichier
de configuration et n'appelle jamais l'API HTTP. Rien, dans le dépôt, ne se
servait de ce jeton — il était exposé sans même être utile.

**Ce qui reste ouvert, et qui appartient à une décision, pas à un correctif.**

- **Le dépôt est public.** Pour un projet commercial dont le code porte la
  logique tarifaire, les règles d'autorisation et la liste des points à trancher,
  c'est un choix, pas un défaut — mais il mérite d'être un choix conscient.
- **Les valeurs de développement restent mondialement lisibles** :
  `BOOTSTRAP_ADMIN_PASSWORD`, `SERVICE_SECRET_DELIVERY`, `SERVICE_SECRET_DISPATCH`,
  `DATA_PROTECTION_KEY`, `OTP_PEPPER`, `POSTGRES_PASSWORD`, `REDIS_PASSWORD` dans
  `deploy/.env.example`, et les `ClientSecret` et `AdminPassword` des
  `appsettings.Development.json`. Toutes sont des valeurs de dev manifestes
  (`hba`, `hba-…`, `HbaD…`) et documentées comme telles, donc ce n'est pas une
  fuite. Le risque est de réemploi : si l'une d'elles atteint la production, elle
  y arrive déjà publiée. Le même traitement que Garage leur conviendrait.
- **Aucune clé FedaPay n'est dans le dépôt** : `FEDAPAY_SECRET_KEY` et
  `FEDAPAY_WEBHOOK_SECRET` sont vides dans le gabarit. Vérifié, puisque cela
  aurait primé sur B4.
- **`deploy/.env` porte encore `MINIO_USER` et `MINIO_PASSWORD`**, résidus
  d'avant la bascule vers Garage. Sans effet, mais à retirer.
- **Les jetons « maîtres » de Garage sont découragés depuis la v2** au profit de
  jetons créés à la demande, limités en portée et expirants
  (`garage admin-token create`). C'est un choix d'exploitation ; il est noté dans
  `garage.toml` et n'a pas été pris ici.

## Annexe G — B6, corrigé le 30 septembre 2026

**Ce que le constat annonçait comme « trois lignes » en demandait davantage**, et
la raison vaut d'être notée : le correctif dépendait d'un fait que l'audit n'avait
pas vérifié — ce que le serveur répond réellement quand un jeton de
rafraîchissement n'est plus valable.

**La table réelle.** Les quatre chemins de refus d'Identity — jeton inconnu,
révoqué, expiré, rejoué — lèvent tous une `ForbiddenException`, que
`ExceptionInterceptor` traduit en `PermissionDenied` et `RpcExceptionMiddleware`
en **403**. S'y ajoutent le **404** (le compte a été effacé) et un **409** précis,
celui qui porte le code `REFRESH_TOKEN_REUSED`. Tout le reste — 400 compris, ainsi
que 408, 429, les 5xx et l'inattendu — ne dit rien de la session.

Le 409 ne se traite pas au statut seul : `FailedPrecondition` est la traduction
par défaut de *n'importe quelle* règle métier refusée, donc un 409 nu sur cette
route ne prouve rien. Seul le code métier tranche.

**Le mécanisme.** `_refreshOnce` ne rend plus un booléen mais une
`IssueDeRafraichissement` à trois valeurs : `rafraichi`, `sessionPerdue`,
`indisponible`. La troisième est toute la correction — le code n'en connaissait que
deux et rangeait donc dans « session perdue » tout ce qui n'était pas un succès.

Sur `indisponible`, `_send` lève `OfflineException`. Ce choix a deux effets, et le
second est celui qui compte : les écrans du livreur **mettent en file** ce qui
échoue ainsi. Une remise prise dans ce cas part en file et repartira au retour du
réseau, là où elle était détruite avant.

**Deux défauts trouvés en passant, dans la même méthode.** Les appelants
concurrents attendaient le rafraîchissement en cours puis *devinaient* son
résultat en regardant si un jeton d'accès existait encore — or un échec ne
l'effaçait pas : ils concluaient « c'est bon », rejouaient avec le jeton expiré et
reprenaient un 401. Ils partagent maintenant l'issue réelle. Et un **200 sans
jetons** dans le corps déconnectait : c'est une panne de service, pas une session
morte, et purger la file pour cela était injustifiable.

**Un troisième, adjacent.** `_send` ne traitait pas `sendTimeout` comme une
absence de réseau. Il ne concerne qu'un appelant, `upload`, qui pose son propre
délai d'envoi : un dépôt de pièce interrompu en 3G ressortait en « erreur
inattendue » au lieu d'être mis en file.

**hba_core a maintenant des tests, et c'étaient les premiers.** Le paquet qui
porte la session, le rafraîchissement et le jeton n'avait aucun dossier `test/` —
B6 vivait dans du code que rien n'exerçait. La table de décision est une fonction
pure, `issueDUnRefusDeRafraichissement`, précisément pour être éprouvée sans Dio
ni coffre : `apps/hba_core/test/rafraichissement_test.dart` couvre les quatre
statuts qui ferment une session et les onze qui ne doivent pas la fermer. Le
paquet déclare désormais `flutter` et `flutter_test`, qu'il empruntait en fait
déjà à l'application qui le consomme.

**Le même mal par une autre porte** était le constat **S15** — appelé « S1 » par
erreur dans une première version de cette annexe, jusqu'à ce que la confusion se
voie : la file traitait n'importe quelle `ApiException` comme un refus définitif,
500, 502, 503, 504 et 429 compris. B6 fermait la porte de la déconnexion, S15
celle du rejeu. **Les deux sont corrigées ; ensemble, elles ferment la perte d'une
remise.**

## Annexe H — B3 et S13, corrigés le 30 septembre 2026

### S13 — le rôle `service`

Une garde de trois lignes dans `Account.SetRoles`, symétrique de celle qui existait
déjà pour `partner`. L'oubli était un effet de bord de la création du rôle la
veille : ajouté à `Roles.All` pour que `IssueServiceToken` puisse l'émettre, il
devenait du même coup attribuable par `SetRoles`, qui accepte tout ce qui est dans
`All`.

Ce n'était pas un droit de plus mais le contournement de tous les autres :
`BillingAccess.EnsureCanReverse` accepte `service` là où il refuse le titulaire,
et `DeleteOwnerMedia` lui confie l'effacement de médias.

**Les trois autres portes ont été vérifiées, et étaient déjà fermées.**
`RegisterWithPhone` n'accepte que `SelfServiceByOtp`, `CreateBackOffice` que les
quatre rôles du back-office, `CreateMerchantUser` que les deux du commerçant.
`SetRoles` était la seule ouverte. Quatre tests le tiennent, dont un qui vérifie
que `service` n'est dans aucune de ces listes — la façon naturelle de s'octroyer le
rôle étant de le demander *en plus* d'un rôle légitime, pour que la requête ait
l'air d'une correction anodine.

### B3 — Media

**Ce constat touchait à une décision déjà prise, et c'est ce qui a dicté la forme
du correctif.** Le point 27 est tranché depuis le 28 septembre : « l'autorisation
reste au service propriétaire […] ils demandent ensuite une URL signée à Media,
qui ne discute pas ». Media ne peut donc pas décider si un livreur est en mission
sur une course — cela demanderait le métier d'un autre.

Mais `PeutDeposer`, côté écriture, avait déjà tiré la ligne au bon endroit, et son
commentaire le disait : *« cet appelant peut-il déposer un fichier sous son propre
identifiant ne demande rien à personne »*. Savoir si un média **appartient** à
l'appelant ne demande rien à personne non plus. Le correctif est donc la
symétrique d'une règle déjà décidée, pas une règle nouvelle : **aucune décision
n'a été prise à la place de qui que ce soit.**

**La règle vit en un seul endroit**, `MediaAccess`, dans la couche applicative, et
le dépôt y délègue désormais. Elle était au bord de l'écriture tandis que la
lecture n'en avait aucune ; deux exemplaires de la même question finissent par
répondre différemment. Trois familles passent :

- le **service**, qui a déjà autorisé de son côté — c'est le sens de « Media ne
  discute pas » ;
- le **back-office**, dont c'est le métier ;
- le **titulaire**, et lui seul, pour ce qui est à lui.

**Le refus se présente en absence, sauf pour la liste.** `NotFoundException` sur un
média désigné par son identifiant : « interdit » confirmerait qu'il existe, donc à
qui il appartient si l'appelant a deviné juste. Ce choix tombe d'ailleurs juste
pour Directory, dont `DecrireAsync` attrape déjà `NotFound` et en conclut « ce
média n'est pas une photo à vous ». Pour `ListMedia`, en revanche, l'appelant nomme
un propriétaire qu'il connaît déjà : il n'y a rien à lui cacher, et le refus est
franc.

**Ce qui reste volontairement impossible.** Un jeton d'utilisateur final ne lit
jamais le média d'un autre. Le jour où un livreur devra voir la photo d'un client,
ou un client la preuve de sa livraison — dont le propriétaire est la **course**, et
une course n'a pas de compte —, c'est Delivery qui l'autorisera puis demandera
l'URL avec un jeton de service, comme le prévoit le point 27. Élargir `MediaAccess`
serait précisément l'erreur qu'elle évite.

**Media a maintenant des tests, et c'étaient les premiers**
(`Hba.Media.Application.Tests`, sur le modèle de `Hba.Billing.Application.Tests`) :
dix-sept cas, dont le rôle sans son claim, la comparaison de GUID insensible à la
casse et à la forme, et la preuve de livraison qui n'a pas de titulaire. Cible :
`make test-media`. `make test-identity` existe aussi désormais — les tests
d'Identity n'avaient aucune cible.

**Deux choses notées au passage, qui ne sont pas des failles.** Les quatre projets
Media sont à la **racine** de la solution, sans imbrication, contrairement à tous
les autres services ; le projet de tests y a été posé avec eux plutôt que de créer
une arborescence pour lui seul. Et la **décision 3 du point 27 reste inexécutée** :
les pièces du dossier livreur n'ont pas migré vers Media. Tant qu'elles n'y sont
pas, elles échappent à l'inventaire, donc au journal des consultations et à la
purge par rétention que le point 27 justifiait.

## Annexe I — B5 et B7, corrigés le 30 septembre 2026

**Les deux étaient de la transposition**, comme annoncé : l'application livreur
avait déjà les deux, le client aucun.

**B5 — les clés iOS.** `NSCameraUsageDescription` et
`NSPhotoLibraryUsageDescription` ajoutées à `apps/client_app/ios/Runner/Info.plist`.
**Les deux, et pas une seule** : la feuille de choix propose « Prendre une photo »
et « Choisir dans mes photos » (`photo_profil.dart` appelle `ImageSource.camera`
et `ImageSource.gallery`), donc c'est le geste de l'utilisateur qui décide
laquelle des deux autorisations iOS est réclamée. N'en déclarer qu'une aurait
fait planter l'autre moitié des utilisateurs.

`NSPhotoLibraryAddUsageDescription` n'a **pas** été ajoutée : l'application ne
range jamais rien dans la photothèque, elle y lit seulement, et déclarer une
autorisation dont on ne se sert pas se paie en questions à la revue.

Le fichier a été validé avec `plistlib` plutôt qu'à l'œil : un plist malformé ne
casse pas le build Flutter, il casse la lecture des clés à l'exécution.

**B7 — la signature.** Le bloc `release` était resté celui du gabarit Flutter,
`TODO` compris. Il lit maintenant `android/key.properties`, avec le même repli
bruyant que le livreur : absent, on retombe sur les clés de débogage **et Gradle
le dit en clair**, pour que personne ne le découvre après le téléversement. Un
`key.properties.example` a été ajouté, qui insiste sur un magasin de clés **par
application** — deux applications qui partagent un magasin partagent le risque.

**Une vérification qui aurait pu mal tourner.** Le commentaire du livreur
affirmait que `key.properties` et `*.jks` étaient ignorés par git. Dans un dépôt
public, un magasin de clés versionné condamnerait la ligne de mise à jour. La
vérification est passée par `git check-ignore` et non par le commentaire : les
deux applications les ignorent bien, via leur `android/.gitignore`. Le
commentaire disait vrai.

**Le README du client a gagné une section « Publier »**, que l'avertissement de
Gradle désigne nommément — la faire pointer vers une section inexistante aurait
été une impasse de plus.

## Annexe J — S2 à S5, corrigés le 30 septembre 2026

**S3, S4 et S5 vivent dans le même écran et se contredisent deux à deux** ; les
traiter séparément aurait produit trois correctifs justes et un écran faux. Ce qui
suit décrit surtout leurs points de contact.

### S2 — l'annulation silencieuse

`OfflineException` est attrapée, et le message dit **ce qui n'a pas eu lieu** :
« la course n'est PAS annulée. Elle est toujours en cours ». « Pas de réseau »
seul laisserait croire à un ennui d'affichage, alors que ce qu'il faut comprendre
est qu'un livreur roule toujours vers le client.

Un `on Object` final a été ajouté : les trois appelants invoquent cette fonction
en `unawaited`, donc **ce qui n'est pas attrapé ici n'est attrapé nulle part**.
Son message reste du côté prudent — la course n'a pas été annulée.

### S3 — la bascule hors ligne qui échoue

L'échec de `goOffline()` n'est plus l'échec de la bascule. Le raisonnement :
couper le battement **suffit** à ne plus recevoir de courses, puisque Driver écarte
des recherches toute position périmée. Le livreur est donc réellement hors ligne,
à la fenêtre de fraîcheur près. On honore son geste, on le lui dit dans ces
termes — « vous pouvez encore recevoir une course pendant 2 min » — et on retient
de prévenir le serveur.

`ServiceEnLigne.arreter()` a reçu sa propre garde : c'est un appel au système, et
une notification récalcitrante ne peut pas avoir pour conséquence de laisser
l'interrupteur sur « en ligne ».

### S4 — l'offre expirée

Le code d'origine gardait délibérément la feuille ouverte, « pour que le livreur
voie POURQUOI le bouton est mort ». Cette intention est conservée : la feuille
reste, puis **se ferme au bout de quatre secondes**. Assez pour être lue, assez peu
pour ne pas coûter une vague — la feuille ouverte gardait `_sheetOpen` à vrai et
suspendait le sondage pendant que le battement continuait d'annoncer un livreur
disponible.

Deux portes voisines fermées au passage : `_sheetOpen` se rend maintenant dans un
`finally` — une exception pendant l'affichage le laissait à vrai pour toute la
session — et la garde de `_pollOffer` se relit **après** l'aller-retour réseau, ce
qui empêche deux sondages simultanés d'empiler deux feuilles pour la même offre.

### S5 — l'état opérationnel

La resynchronisation va désormais **dans les deux sens** et a lieu au retour dans
l'application. Le sens descendant — le serveur ne le tient plus pour disponible —
n'existait pas du tout.

**Seul `horsLigne` déclenche la mise hors ligne, jamais `inconnu`.** Un statut que
l'application ne sait pas lire est un défaut d'analyse, pas une décision du
serveur : s'en servir pour couper le travail du livreur ferait payer une faute de
version à celui qui roule.

### Le point de contact, et le défaut qu'il a failli créer

S3 coupe tout localement quand le serveur n'a pas pu être prévenu ; S5 relit
l'état du serveur et adopte ce qu'il dit. **Sans mémoire de l'intention, la seconde
défait la première** : le livreur se met hors ligne, revient dans l'application, et
se retrouve en ligne sans avoir rien demandé. D'où `_horsLigneAConfirmer`, qui
passe avant toute lecture et déclenche un `goOffline()` différé.

Ce drapeau a lui-même failli créer la panne symétrique : laissé posé après un
retour **en ligne** volontaire, il aurait envoyé le `goOffline` différé à la
première resynchronisation, mettant hors ligne côté serveur un livreur dont
l'écran affichait du vert et dont le battement tournait. Il est donc effacé au
passage en ligne. Les trois écritures du drapeau — pose, effacement à la
confirmation, effacement au retour en ligne — sont les trois seules qui existent.

### Ce qui n'est pas couvert

Aucun test. Ces quatre corrections vivent dans des widgets, et l'application
livreur n'a aucun test d'écran — c'est le manque que l'audit relève plus haut, et
il n'est pas comblé ici. Ce sont donc des corrections **relues**, pas éprouvées :
`flutter analyze` et un essai sur un vrai téléphone restent nécessaires,
particulièrement pour S3, dont le scénario demande de couper le réseau au bon
moment.

## Ordre de traitement proposé

1. ~~**B1** et **B2**~~ — faits le 30 septembre 2026, avec les tests qui les
   tiennent (annexe E).
2. ~~**B4**~~ — fait le 30 septembre 2026 : secrets rotés et sortis du dépôt
   (annexe F).
3. ~~**B6**~~ — fait le 30 septembre 2026 (annexe G). Il ne s'agissait pas de
   trois lignes : voir pourquoi.
4. ~~**B3** et **S13**~~ — faits le 30 septembre 2026 (annexe H).
5. ~~**B5** et **B7**~~ — faits le 30 septembre 2026 (annexe I). **B8 reste**, et
   c'est lui qui bloque encore la publication.
6. **S2**, **S3**, **S4**, **S5** — l'application livreur qui ment sur son propre état.
7. **S6**, **S7**, **S1** — puis le reste des sérieux.
8. Chaîne d'intégration continue Flutter et projet de tests Driver, avant d'ajouter des fonctionnalités.

Aucune décision n'a été prise à ta place. Les points marqués « à trancher »
restent ouverts, et `points-a-trancher.md` n'a pas été modifié.
