# Audit de l'application livreur — ce qui manque pour une première version

Établi le 27 septembre 2026, en lisant le code de `apps/driver_app`, les routes
livreur de la passerelle, et le manifeste Android / l'Info.plist iOS.

Il complète [`audit-reste-a-faire.md`](audit-reste-a-faire.md), qui porte sur la
plateforme entière ; celui-ci ne parle que de l'application du livreur.

**Méthode, et ses limites.** Tout ce qui suit vient de la lecture du dépôt :
chaque affirmation porte le fichier et la ligne qui la soutient. Je n'ai pas pu
exécuter `flutter analyze`, ni lancer l'application, ni mesurer quoi que ce soit
sur un téléphone — aucun outil Dart n'est disponible là où cet audit a été
écrit. Les prédictions de comportement sont donc des **prédictions
falsifiables**, pas des observations : chacune est accompagnée du test qui la
confirmerait ou la démentirait.

---

## Ce qui fonctionne déjà, et qu'il ne faut pas reconstruire

La surface d'API est **entièrement consommée** : les vingt-deux routes livreur
de la passerelle sont toutes appelées par l'application. Il ne manque aucun
branchement.

Le parcours complet tient debout : inscription par OTP, dépôt des pièces,
déclaration du véhicule, passage en ligne, réception d'une offre avec compte à
rebours, acceptation, arrivée, collecte, remise contre code, historique, gains,
demande de versement, conditions et confidentialité, suppression de compte.

L'idempotence est posée sur chaque action mutante, côté client comme côté
service. La carte est habillée, les épingles sont dessinées, la bascule flotte.

---

## 1. Ce qui empêche de publier

### 1.1 L'application ment sur l'état « en ligne » dès que le téléphone est en poche

> **Palliatif posé le 27 septembre 2026.** L'écran distingue désormais
> l'INTENTION (la bascule) du FAIT (le serveur a-t-il une position récente ?) et
> affiche trois états au lieu de deux : hors ligne, en ligne, **hors de portée**.
> Au retour au premier plan, une position part immédiatement et le livreur est
> averti de la durée pendant laquelle il était invisible. **La cause n'est pas
> corrigée** — il faut toujours un service de premier plan et les permissions
> d'arrière-plan ; ce qui change, c'est que l'application n'affirme plus le
> contraire de ce qui est.

**C'est le défaut le plus grave, et de loin.** Le battement de position et le
sondage des offres sont deux `Timer` qui vivent dans l'état de l'écran d'accueil
(`home_screen.dart:105-107`). Rien ne les protège de la mise en arrière-plan :
`didChangeAppLifecycleState` ne traite que `resumed` (`home_screen.dart:72`).

Le manifeste Android ne déclare que `ACCESS_FINE_LOCATION` et
`ACCESS_COARSE_LOCATION` (`AndroidManifest.xml:14-15`) — **ni
`ACCESS_BACKGROUND_LOCATION`, ni `FOREGROUND_SERVICE`**. Sur Android 8 et
au-delà, une application en arrière-plan sans ces déclarations reçoit quelques
positions par heure, pas une toutes les vingt secondes.

Or le service Driver écarte des recherches toute position plus vieille que
`DriverLocations:FreshnessSeconds`, **120 secondes par défaut**. Donc :

> Un livreur passe en ligne, verrouille son téléphone, le met dans sa poche.
> Deux minutes plus tard il a disparu de toutes les vagues. Son application
> affiche toujours « En ligne », en vert.

**Le test qui tranche** : passe en ligne, verrouille l'écran cinq minutes, puis
lance `make offre` en visant ce livreur. S'il ne reçoit rien alors que son
téléphone dit « En ligne », c'est confirmé.

**Ce que ça demande** — et ce n'est pas qu'une permission :

- un service de premier plan avec sa notification persistante (Android l'exige
  pour la position continue) ;
- `ACCESS_BACKGROUND_LOCATION` et, sur iOS,
  `NSLocationAlwaysAndWhenInUseUsageDescription` plus
  `UIBackgroundModes/location` — l'Info.plist actuel note déjà, à la ligne 86,
  que ce point **n'est pas tranché dans le référentiel** ;
- une décision : que fait l'application quand le livreur quitte l'écran
  d'accueil sans se mettre hors ligne ? Le passer hors ligne d'office est
  brutal ; ne rien faire est ce qui produit le mensonge ci-dessus.

**Le palliatif honnête, en attendant**, tient en quelques heures : détecter
`AppLifecycleState.paused`, et à ce moment-là soit passer hors ligne
explicitement en le disant au livreur, soit afficher au retour « vous étiez
hors de portée pendant N minutes ». Mieux vaut une application qui avoue qu'une
application qui affiche du vert.

### 1.2 Aucune notification poussée

Les offres sont sondées toutes les cinq secondes tant que l'écran d'accueil est
vivant (`home_screen.dart:31`). Le commentaire le dit lui-même : « une
notification poussée prendra le relais ; tant qu'elle n'existe pas, sonder reste
le moyen le plus simple ».

C'est la face visible du 1.1 : sans notification, une offre n'atteint le livreur
que s'il a l'application ouverte, à l'écran, éveillée. Aucune dépendance de
messagerie n'est déclarée dans `pubspec.yaml`.

Les deux se règlent ensemble ou ne se règlent pas : un livreur qu'on peut
notifier est un livreur qu'on peut réveiller.

### 1.3 L'application est signée avec les clés de debug — CORRIGÉ le 27 septembre 2026

> `android/key.properties` (ignoré par git) porte désormais le magasin de clés,
> et la construction de publication l'utilise. Absent, on retombe sur le
> débogage **et Gradle l'écrit en clair dans la console** au lieu de le taire.
> Reste à créer le magasin de clés lui-même : `android/key.properties.example`
> donne la commande `keytool`.

`android/app/build.gradle.kts:50` porte encore le `TODO` de Flutter :

```
// TODO: Add your own signing config for the release build.
signingConfig = signingConfigs.getByName("debug")
```

En l'état, **aucune publication n'est possible** sur le Play Store. Il faut un
keystore, hors du dépôt, et un `key.properties` ignoré par git.

### 1.4 Le nom et l'icône sont ceux du gabarit — CORRIGÉ le 27 septembre 2026

> **HBA Livreur** sous l'icône, sur Android comme sur iOS. L'icône est l'éclair
> de la marque sur l'orange `#F2690F`, en trois jeux : classique, premier plan
> adaptatif pour Android 8 et au-delà, et le jeu iOS **sans canal alpha** —
> l'App Store refuse la transparence.

Sous l'icône, le téléphone affiche `hba_driver` (`AndroidManifest.xml:40`) et
`Hba Driver` sur iOS (`Info.plist:10`). L'icône est celle de Flutter.

C'est une demi-journée, et c'est la première chose que voit un livreur.

### 1.5 La file d'actions hors ligne n'existe pas — CORRIGÉ le 27 septembre 2026

> `lib/core/file_actions.dart`. **Sélective, pas universelle** : trois actions
> seulement — arrivée, collecte, remise —, parce que rejouer « je passe en
> ligne » ou une position périmée serait actif­ement nuisible. Ordonnée, arrêt
> au premier échec réseau, déduplication par clé d'idempotence, plafond de 50,
> stockage dans le coffre chiffré (une remise en attente porte le code de
> remise). Vidée au démarrage, au retour au premier plan et à chaque battement
> de position réussi.
>
> **Arrivée et collecte avancent en optimiste, la remise non.** Les deux
> premières sont des constats que le serveur ne peut pas contredire ; la remise
> fait valider un code, donc il peut refuser — annoncer « course livrée » et
> afficher la rémunération promettrait quelque chose qui n'est peut-être pas
> arrivé.
>
> Reste ouvert : une action que le serveur **refuse définitivement** au rejeu
> est abandonnée pour ne pas bloquer la file, et signalée à Crashlytics. On ne
> peut pas distinguer ici le refus bénin (la course avait déjà avancé) du refus
> grave (code de remise invalide). C'est le seul endroit de ce mécanisme qui
> mérite encore une décision.

Le README de l'application en fait **la contrainte qui structure tout** :

> « File d'actions locale. Une action se met en file, s'horodate côté client et
> se rejoue au retour du réseau. »

Rien de tel n'existe dans `lib/`. Les clés d'idempotence sont bien posées — donc
le **serveur** est prêt à recevoir un rejeu sans dommage — mais le client ne met
rien en file : une action qui échoue affiche un bandeau et disparaît.

Concrètement : un livreur qui confirme une remise dans un sous-sol sans réseau
doit y repenser lui-même. C'est le genre de défaut qui se paie en litiges, pas
en tickets.

**L'écart entre le README et le code est en soi un problème** : la prochaine
personne qui lira ce README croira que le mécanisme existe.

---

## 2. Ce qui manque sans bloquer

### 2.1 Aucun rapport de plantage — CORRIGÉ le 27 septembre 2026

> Firebase Crashlytics, choisi parce que les notifications poussées (1.2)
> passeront de toute façon par FCM : une installation au lieu de deux. Tout
> passe par `lib/core/plantages.dart`, jamais par Firebase en direct — l'outil
> se remplace, et surtout, sans projet Firebase configuré l'application doit
> continuer de tourner. Les deux robinets sont branchés (`FlutterError.onError`
> **et** `PlatformDispatcher.onError` — n'en brancher qu'un laisse passer la
> catégorie la plus fréquente, l'exception dans un `Future` sans `catch`).
> Miettes de chemin aux cinq moments qui comptent, identifiant du livreur
> rattaché, rien en débogage. **La politique de confidentialité a été mise à
> jour** : c'est le seul endroit où une donnée sort des serveurs de HBA.
>
> Reste à créer le projet Firebase et à poser les deux fichiers de
> configuration ; sans eux la construction passe, avec un avertissement.

Ni Sentry, ni Crashlytics, ni journal distant. Quand l'application se fermera
toute seule chez un livreur à Cotonou, personne ne le saura — et le livreur, lui,
n'appellera pas pour dire « ça a fermé », il réessaiera. C'est le moins cher des
points de cet audit et l'un des plus rentables.

### 2.2 Un seul fichier de test

`test/models_test.dart`, et rien d'autre. Les endroits qui méritent un test ne
sont pas les écrans, ce sont les **lectures de réponses** : `models.dart`,
`gains/models.dart`, `dossier_repository.dart` acceptent tous deux formes de
sérialisation, et c'est précisément là qu'une panne devient silencieuse — un
statut inconnu n'explose pas, il efface un bouton.

### 2.3 L'historique s'arrête à vingt-cinq courses

L'écran le dit franchement (`courses_screen.dart:59`), ce qui est bien, mais un
livreur à temps plein dépasse vingt-cinq courses en une semaine. La passerelle
n'expose aucun jeton de page : c'est une route à compléter, pas un écran.

Même limite sur les gains : les cumuls portent sur tout le compte, la liste des
mouvements est plafonnée.

### 2.4 Deux commentaires devenus faux — CORRIGÉ le 27 septembre 2026

`profil_repository.dart:59` et `profil_screen.dart:33` affirmaient qu'« aucune
route ne permet de déclarer le véhicule ». C'était vrai ; `PUT /vehicle` existe
désormais et l'écran du dossier l'appelle. Un commentaire faux coûte plus cher
qu'un commentaire absent.

Ils étaient **trois** et non deux — `profil_repository.dart:87` affirmait en
plus qu'« aucune route du BFF ne modifie le profil », ce qui était faux deux
fois : `PUT /vehicle` et `POST /profile-photo` existent tous les deux. Les trois
sont réécrits, et chacun conserve une ligne disant ce qu'il affirmait avant :
un commentaire qui a menti une fois mérite qu'on se souvienne qu'il l'a fait.

Un quatrième mensonge, non listé, a été trouvé au passage : la classe
`_CeQuiManque` — « ce qui manque » — est le lien vers l'écran Dossier, c'est-à-
dire vers l'écran qui fait précisément ce qu'elle annonçait comme manquant.
Renommée `_LienDossier`.

### 2.5 Deux décisions déjà consignées qui touchent cette application

- **Point 16** — le contrat ne connaît ni vélo ni tricycle. L'épingle porte le
  pictogramme des trois types qui existent ; les deux autres attendent une
  décision qui touche le tarif, les pièces du dossier et le plafond de poids.
- **Point 17** — rien n'enregistre qu'un livreur a accepté les conditions. Les
  deux documents existent, numérotés ; personne n'accepte rien.

---

## 3. Ce qu'on peut ajouter, et qui se voit

Par rapport coût / effet, dans l'ordre.

1. ~~**Un son et une vibration à l'arrivée d'une offre.**~~ **Fait le 27
   septembre 2026.** Deux canaux, parce qu'aucun ne suffit seul. Le son est
   généré par `tool/generer_son.py`, pas téléchargé. **Il ne remplace pas la
   notification poussée** : il ne joue que si l'application est au premier
   plan, puisque c'est une minuterie de l'écran d'accueil qui va chercher les
   offres.
2. ~~**Le récapitulatif de fin de course.**~~ **Fait le 27 septembre 2026.** Il
   ne dit pas « ajouté à vos gains » mais « cette course vous rapporte », et
   annonce le délai : le crédit arrive par un événement, quelques instants plus
   tard, et un livreur qui ouvrirait ses gains dans la seconde pourrait n'y
   rien voir encore.
3. **Le total du jour sur l'accueil.** Un chiffre, en haut : courses faites
   aujourd'hui et gains du jour. C'est ce que regardent les livreurs de toutes
   les plateformes, plusieurs fois par jour.
4. **Un état vide qui explique, sur l'accueil.** Quand le livreur est en ligne
   et qu'aucune offre n'arrive, l'écran ne dit rien. Une phrase — « en ligne
   depuis 12 min, aucune offre dans votre zone » — change une attente muette en
   information.
5. **La photo du vélo… pardon, du destinataire.** Rien ne montre au livreur à
   qui il remet le colis, hors le nom et le code. À trancher : c'est une donnée
   personnelle du destinataire.
6. **Le numéro du client en un appui pendant la course.** — CORRIGÉ. La route
   rendait déjà le téléphone du destinataire ; l'écran de mission l'affichait
   en texte mort. `appeler(numero)` dans `core/navigation_externe.dart` ouvre
   le composeur (« tel: » ouvre, il n'appelle pas : la plateforme l'interdit à
   une application), et `_BoutonAppel` le pose sur DEUX numéros, pas un : le
   destinataire, et le contact sur place de chaque étape — celui du retrait
   compte davantage, c'est lui qu'on appelle devant une porte close. Le numéro
   reste lisible sous le libellé. Le `<queries>` du manifeste déclare `tel`,
   `mailto`, `https` et `geo` : sans cela Android 11 et au-delà cachent le
   composeur à url_launcher et le bouton échoue en silence — ce qui valait
   aussi, jusqu'ici, pour « Appeler le support » et « Y aller ».

---

## Ce que je ferais dans l'ordre

1. **Le mensonge sur l'état en ligne** (1.1), au moins son palliatif : détecter
   la mise en arrière-plan et cesser d'afficher du vert. Une journée.
2. **Le nom, l'icône, la signature** (1.3, 1.4). Une demi-journée, et sans cela
   rien ne se publie.
3. **Le rapport de plantage** (2.1). Deux heures.
4. **Le son à l'offre et le récapitulatif de fin de course** (3.1, 3.2). Une
   journée, et c'est ce que les livreurs remarqueront.
5. **La notification poussée et le service de premier plan** (1.1, 1.2). C'est
   le vrai chantier — plusieurs jours, une décision produit, et des réglages par
   constructeur de téléphone.
6. **La file d'actions hors ligne** (1.5). Ou, à défaut, corriger le README pour
   qu'il cesse de décrire un mécanisme absent.

---

## Ce que cet audit ne dit pas

Il ne dit rien de la **qualité perçue** — je n'ai pas vu l'application tourner.
Il ne dit rien des **performances réelles** sur un téléphone d'entrée de gamme,
ni de la consommation de batterie du battement à vingt secondes, qui mérite une
mesure et pas une opinion. Et il ne couvre ni l'application client, ni la
console : `audit-reste-a-faire.md` s'en charge.
