# client_app — application client (Flutter)

## Ce que l'application fait

Demander un devis, créer et payer une livraison, la suivre, appeler le livreur,
annuler selon la politique d'annulation, consulter son historique.

Authentification : téléphone + OTP SMS, puis JWT. Rôle `customer`.

## Ce à quoi il faut faire attention

- **Le code de remise s'affiche ici.** Le client peut le lire et le transmettre
  au destinataire. Il disparaît dès que la course est close.
- **Le livreur n'apparaît qu'à partir de `DRIVER_ASSIGNED`** — nom, véhicule,
  téléphone — et jusqu'à la clôture. Le BFF renvoie des champs nuls avant et
  après : l'interface doit le gérer sans supposer leur présence.
- Un devis expire. L'écran doit le redemander plutôt que de créer une livraison
  avec un devis périmé, que le service refusera.

## Authentification

```
POST /api/client/v1/auth/otp/request   { phone, deviceId }
POST /api/client/v1/auth/otp/verify    { challengeId, code, deviceId, displayName }
POST /api/client/v1/auth/refresh       { refreshToken, deviceId }
POST /api/client/v1/auth/logout        { refreshToken }
```

`otp/request` répond la même chose quel que soit le numéro — inconnu, connu,
suspendu. C'est volontaire : sans cela, la route dirait à n'importe qui quels
numéros ont un compte chez HBA. L'application demande donc le nom APRÈS
vérification, à tout le monde, et l'ignore si le compte en a déjà un.

Le jeton d'accès dure quinze minutes, le jeton de rafraîchissement trente jours.
Ce dernier **ne sert qu'une fois** : chaque rafraîchissement en renvoie un
nouveau, qu'il faut stocker à la place de l'ancien. Rejouer un jeton déjà
consommé révoque toute la session — l'application doit donc écrire le nouveau
jeton avant de considérer l'opération terminée.

En développement, le code SMS est fixé à `000000`.

## Profil et adresses

```
GET    /api/client/v1/me
PUT    /api/client/v1/me              { displayName, email }
GET    /api/client/v1/addresses
POST   /api/client/v1/addresses       { label, latitude, longitude, landmark, phone, contactName, notes, setAsDefault }
PUT    /api/client/v1/addresses/{id}
DELETE /api/client/v1/addresses/{id}
```

La première adresse enregistrée devient automatiquement l'adresse principale.

## Démarrage

```bash
cd apps/client_app
flutter pub get
cp dart_defines.example.json dart_defines.json   # puis adaptez HBA_API_BASE
flutter run --dart-define-from-file=dart_defines.json
```

`dart_defines.json` **n'est pas versionné** : il porte l'adresse de la
passerelle vue depuis le téléphone, c'est-à-dire l'adresse du Mac sur le réseau
local (`ipconfig getifaddr en0`). La partager ferait échouer le lancement chez
tout le monde sauf une personne. `dart_defines.example.json`, lui, est versionné
et sert de modèle.

| Réglage | À quoi il sert | Vide |
|---|---|---|
| `HBA_API_BASE` | La passerelle, vue du téléphone | `localhost`, qui ne désigne le Mac que sur un simulateur |
| `HBA_SUPPORT_TEL` | « Appeler le support », dans le profil et dans l'aide | l'entrée ne s'affiche pas |
| `HBA_SUPPORT_EMAIL` | « Écrire au support », et « Un problème sur cette course ? » dans le suivi | les deux entrées ne s'affichent pas |
| `HBA_CGU`, `HBA_CONFIDENTIALITE` | Lien vers la version publiée des deux documents | les documents s'affichent quand même, hors ligne |

**Les deux contacts vides font afficher, à la place de la carte Assistance, la
raison de son absence.** Ce n'est pas un oubli : un bouton « Appeler le
support » qui ouvre un composeur vide fait croire au client qu'il a un recours
qu'il n'a pas. Jusqu'au 28 septembre 2026, aucun `dart_defines.json` n'existait
pour cette application — le bloc support était donc complet dans le code et
invisible à l'écran.

Une définition ponctuelle se passe toujours en ligne de commande, et elle prime
sur le fichier :

```bash
flutter run --dart-define-from-file=dart_defines.json \
            --dart-define=HBA_API_BASE=https://client.hba.bj/api/client/v1
```

## Publier

La version de publication est signée avec le magasin de clés décrit dans
`android/key.properties`, copié depuis `android/key.properties.example`. **Ni
l'un ni l'autre des deux fichiers sensibles n'est versionné** : `key.properties`
et `*.jks` sont ignorés par git.

Sans ce fichier, `flutter build --release` fonctionne quand même — signé avec les
clés de **débogage**, et Gradle l'écrit en clair dans la console. Le Play Store
refuse ces paquets-là ; l'avertissement est là pour qu'on ne le découvre pas
après le téléversement. Le bloc `release` était resté celui du gabarit Flutter
jusqu'au 30 septembre 2026.

```
flutter build appbundle --release --dart-define-from-file=dart_defines.json
```

**Trois déclarations iOS conditionnent la revue de l'App Store**, et deux
manquaient jusqu'au 30 septembre 2026 : `NSCameraUsageDescription` et
`NSPhotoLibraryUsageDescription`. Sans elles, iOS ne demande pas l'autorisation,
il **arrête l'application** à la première ouverture de l'appareil photo — et la
revue refuse un binaire qui y accède sans dire pourquoi. La troisième,
`NSLocationWhenInUseUsageDescription`, était déjà là.

**IL RESTE UN BLOCAGE, ET IL N'EST PAS CORRIGE ICI.** L'identifiant
d'application est encore celui du gabarit Flutter — `com.example.hba_client`,
dans `namespace`, `applicationId`, le paquet Kotlin de `MainActivity` et
l'identifiant de bundle du projet Xcode. Google Play **refuse** tout
`com.example.*`. Le changer demande de déplacer `MainActivity.kt` et de toucher
au projet Xcode ; il faut le faire avant la première publication, car
l'identifiant ne se change plus ensuite sans perdre les installations. La
convention posée par l'application livreur est `com.hbatechettrade.hba_driver`,
donc `com.hbatechettrade.hba_client` ici. Le nom affiché sous l'icône est aussi
resté `hba_client` (`AndroidManifest.xml`), là où le livreur affiche
« HBA Livreur ».

## Carte

Le point de collecte et le point de livraison se choisissent sur une carte
Google. Aucun géocodage n'est fait : la carte ne produit que deux nombres, et
c'est le **repère écrit** qui parle au livreur.

### Clé d'API

Il en faut une par plateforme, depuis la console Google Cloud, avec la
**facturation activée sur le projet** — c'est obligatoire même quand rien n'est
facturé. L'affichage d'une carte dynamique dans une application mobile n'est
pas facturé à l'affichage : Google écrit que « all mobile usage of the Maps SDK
for Android is unlimited ». Les API facturées sont celles qu'on n'utilise pas
ici : géocodage, itinéraires, autocomplétion.

RESTREIGNEZ CHAQUE CLE. Une clé Android se restreint au nom de paquet et à
l'empreinte SHA-1 du certificat ; une clé iOS, à l'identifiant de bundle. Une
clé sans restriction qui fuite dans un APK est utilisable par n'importe qui, à
vos frais.

Les clés ne sont pas versionnées. Elles se passent au build :

```bash
flutter run --dart-define=MAPS_API_KEY=...
```

### Android

Activer **Maps SDK for Android**, puis dans
`android/app/src/main/AndroidManifest.xml`, à l'intérieur de `<application>` :

```xml
<meta-data android:name="com.google.android.geo.API_KEY"
           android:value="${MAPS_API_KEY}" />
```

et dans `android/app/build.gradle`, sous `defaultConfig` :

```groovy
manifestPlaceholders += [MAPS_API_KEY: project.findProperty("MAPS_API_KEY") ?: ""]
```

`minSdk` doit valoir au moins **24**.

### iOS

Activer **Maps SDK for iOS**, puis dans `ios/Runner/AppDelegate.swift` :

```swift
import GoogleMaps

GMSServices.provideAPIKey(Bundle.main.object(forInfoDictionaryKey: "MapsApiKey") as! String)
```

avec `MapsApiKey` renseigné dans `Info.plist` depuis une configuration de build.
La cible de déploiement doit être **iOS 14** au minimum.

### Localisation

`geolocator` centre la carte sur la position du téléphone à l'ouverture. Sans
permission, elle s'ouvre sur le centre de Cotonou plutôt que sur l'océan. Les
déclarations à ajouter :

- Android : `ACCESS_FINE_LOCATION` dans le manifeste ;
- iOS : `NSLocationWhenInUseUsageDescription` dans `Info.plist`, avec une phrase
  qui dit pourquoi — « pour placer votre adresse de collecte sur la carte ».
