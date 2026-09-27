# driver_app — application livreur (Flutter)

## Lancer l'application

```
flutter run --dart-define-from-file=dart_defines.json
```

`dart_defines.json` se copie depuis `dart_defines.example.json` et **n'est pas
versionné** : il porte l'adresse de la passerelle vue depuis le téléphone,
c'est-à-dire l'adresse du Mac sur le réseau local (`ipconfig getifaddr en0`).
La partager ferait échouer le lancement chez tout le monde sauf une personne.

| Réglage | À quoi il sert | Vide |
|---|---|---|
| `HBA_API_BASE` | La passerelle, vue du téléphone | `localhost`, qui ne marche que sur un simulateur |
| `HBA_SUPPORT_TEL` | « Appeler le support », dans le profil | l'entrée ne s'affiche pas |
| `HBA_SUPPORT_EMAIL` | « Écrire au support », dans le profil | l'entrée ne s'affiche pas |
| `HBA_CGU`, `HBA_CONFIDENTIALITE` | Lien vers la version publiée des deux documents | les documents s'affichent quand même, hors ligne |

**Les deux contacts vides font disparaître la carte Assistance entière.** Ce
n'est pas un oubli : un bouton « Appeler le support » qui ouvre un composeur
vide fait croire au livreur qu'il a un recours qu'il n'a pas.

## Publier

La version de publication est signee avec le magasin de cles decrit dans
`android/key.properties`, copie depuis `android/key.properties.example`. **Ni
l'un ni l'autre des deux fichiers sensibles n'est versionne** : `key.properties`
et `*.jks` sont ignores par git.

Sans ce fichier, `flutter build --release` fonctionne quand meme — signe avec
les cles de **debogage**, et Gradle l'ecrit en clair dans la console. Le Play
Store refuse ces paquets-la ; l'avertissement est la pour qu'on ne le decouvre
pas apres le televersement.

```
flutter build appbundle --release --dart-define-from-file=dart_defines.json
```

Le nom affiche sous l'icone est **HBA Livreur** (`AndroidManifest.xml` et
`Info.plist`), l'identifiant d'application `com.hbatechettrade.hba_driver` — il
ne se change plus apres la premiere publication sans perdre les installations.

L'icone est l'eclair de la marque sur l'orange `#F2690F`, en trois jeux :
l'icone classique, le premier plan de l'icone adaptative d'Android 8 et au-dela,
et le jeu iOS sans canal alpha — l'App Store refuse la transparence.

## Rapport de plantage

Firebase Crashlytics. Sans lui, un defaut qui ferme l'application chez un
livreur peut durer des mois : un livreur n'appelle pas pour dire « ca a
ferme », il rouvre et reessaie.

Mise en place, une fois :

1. Creer le projet Firebase et y ajouter l'application Android
   `com.hbatechettrade.hba_driver`, puis l'application iOS.
2. Poser `google-services.json` dans `android/app/` et
   `GoogleService-Info.plist` dans `ios/Runner/` (a ajouter au projet depuis
   Xcode, cible Runner).
3. **Versionner les deux fichiers.** Ils ne sont pas secrets — on les extrait
   de n'importe quel APK publie — et sans eux un depot fraichement clone
   compile sans rapport de plantage, en silence.
4. iOS seulement : ajouter dans Xcode la phase de construction qui televerse
   les symboles, sans laquelle les piles d'appels arrivent illisibles.

**Tant que `google-services.json` est absent, l'application se construit et
tourne normalement, sans rapport de plantage**, et Gradle l'ecrit dans la
console. C'est volontaire : un outil de diagnostic ne doit pas empecher de
compiler.

Rien ne part en debogage — les plantages d'un developpeur noieraient ceux des
livreurs, qui sont les seuls a compter.

Tout passe par `lib/core/plantages.dart` : `trace()` pose une miette de chemin,
`noter()` signale une erreur rattrapee, `livreur()` rattache les rapports a un
identifiant. Aucun ecran n'importe Firebase directement.

## Le son d'offre

`assets/sons/offre.wav` est **genere**, pas telecharge : deux notes montantes
de synthese, 1,16 s, 44,1 kHz mono. Le produire, ou le modifier :

```
python3 tool/generer_son.py
```

Aucune dependance — `wave` et `struct` sont dans la bibliotheque standard. Un
binaire dont personne ne sait d'ou il vient est un binaire qu'on n'ose plus
toucher : ici le son EST ce script.

Le signal part par **deux canaux**, parce qu'aucun ne suffit seul : le son ne
passe pas dans une rue a midi, la vibration ne se sent pas quand le telephone
est sur une table. Chacun tombe sans emporter l'autre.

**Les deux ont ete corriges apres un essai sur telephone ou ni l'un ni l'autre
ne se produisait :**

- Le son jouait dans le canal des **notifications**, celui que la sourdine
  coupe en premier. Il passe par le canal des **alarmes**, qui y survit et ne
  se met pas en pause pendant un guidage vocal.
- La vibration passait par `HapticFeedback`, qui n'est PAS le moteur de
  vibration mais le retour tactile de l'interface — soumis au reglage systeme
  « vibration au toucher », eteint par defaut sur beaucoup de Samsung. Elle
  passe par le greffon `vibration` (permission `VIBRATE`, accordee a
  l'installation), avec repli sur `HapticFeedback` si le greffon manque.

Ce greffon est une dependance a resoudre comme les autres : `flutter pub add
vibration` si `flutter pub get` refuse la borne du `pubspec.yaml`.

**Le signal se repete toutes les trois secondes** jusqu'a l'acceptation, le
refus, ou l'expiration du compte a rebours — comme un telephone qui sonne, pas
comme une notification. Un coup unique passait inapercu chez un livreur qui
avait la tete ailleurs, et rien ne le rappelait ensuite. Trois arrets possibles :
`Signal.silence()` a l'appui, `Signal.silence()` a zero du compte a rebours, et
un plafond de deux minutes si aucun des deux n'arrive — un telephone qui vibre
sans fin dans une poche est un defaut qu'on ne rattrape pas a distance.

**Le livreur peut couper le son depuis Profil**, pas la vibration : couper les
deux reviendrait a se rendre injoignable sans le savoir. Le reglage vit sur le
telephone, dans le coffre (`hba.driver.son_offre`) ; changer d'appareil le remet
a « actif ». Point 18 des points a trancher.

**Ce qui reste hors de portee de l'application :** le volume des alarmes regle a
zero, et le mode « Ne pas deranger » configure pour bloquer AUSSI les alarmes.
Aucune application ne peut passer outre — c'est une regle du systeme, pas un
manque de code. La vibration, elle, part dans les deux cas.

**Ce n'est pas un remplacant de la notification poussee.** Tout ceci ne joue
que si l'application est au premier plan : c'est une minuterie de l'ecran
d'accueil qui va chercher les offres. Un livreur dont le telephone est
verrouille ne recoit rien du tout — ni son, ni offre.

## Les gestes qui engagent se glissent

Accepter une course, annoncer qu'on est sur place, declarer le colis recupere,
confirmer la remise : ces gestes partent vers le serveur et changent l'etat
d'une course. Ils se font desormais par **glissement de la gauche vers la
droite** (`HbaGlissiere`, dans `hba_ui`), pas par un appui.

Un appui se declenche par accident — telephone dans la poche, gant humide,
secousse sur une piste, pouce pose au hasard sur une feuille d'offre qui vient
de s'ouvrir toute seule. Un glissement de trois centimetres ne se produit pas
tout seul.

Le seuil est a **70 % de la course**, pas au bout : demander le rail entier
oblige a un geste parfait, souvent a une main, et un livreur qui s'arrete a
quatre-vingt-dix pour cent voit le rail revenir en arriere — ce qui se lit comme
une panne. Une detente rapide confirme aussi, plus tot. Un lecteur d'ecran
active le rail par un double appui : exiger le geste fermerait l'application a
qui ne peut pas le faire.

**Tout ce qui est reversible reste un bouton ordinaire** : Refuser, Terminer,
Reessayer. La friction est reservee a ce qui engage.

## Rester en ligne quand l'ecran s'eteint

Un livreur en ligne qui range son telephone CESSE D'EXISTER POUR LE DISPATCH au
bout de 120 s : le service Driver ecarte des recherches toute position plus
vieille que `DriverLocations:FreshnessSeconds`, et les minuteries de Flutter
s'arretent des que le processus redescend en priorite « arriere-plan ».

`core/service_en_ligne.dart` demarre un **service Android de premier plan** au
passage en ligne, et l'arrete au passage hors ligne. **Il n'execute rien.** Son
role est de maintenir le processus en priorite « premier plan » pour que les
minuteries de l'isolat principal — position, sondage d'offre — continuent de
battre. Le reseau reste ou il a toujours ete.

**POURQUOI L'ISOLAT DU SERVICE NE PARLE PAS AU RESEAU :** le jeton d'acces dure
quinze minutes et le jeton de rafraichissement NE SERT QU'UNE FOIS — le rejouer
revoque la session. Deux isolats qui rafraichissent chacun de leur cote
deconnecteraient le livreur au milieu d'une course.

**Ce que cela ne couvre pas :** balayer l'application hors des recentes tue
l'isolat principal. Le service s'arrete avec lui (`android:stopWithTask`)
plutot que de laisser une notification qui promet une presence disparue.

**Pas d'ACCESS_BACKGROUND_LOCATION**, et ce n'est pas un oubli : un service de
premier plan de type `location` demarre pendant que l'application est au premier
plan y accede sans cette permission, ce qui evite la revue manuelle la plus
lourde du Play Store. Voir le point 21 des points a trancher.

## Ce que l'application fait

S'inscrire et déposer ses documents KYC, passer en ligne ou hors ligne, recevoir
des offres, accepter ou refuser, signaler l'arrivée, confirmer la collecte,
confirmer la remise avec l'OTP, consulter ses gains.

Authentification : téléphone + OTP, JWT. Rôle `driver`, actif seulement après
validation du KYC.

## La contrainte qui structure tout : la connexion

L'application doit fonctionner avec une connexion instable. Concrètement :

- **File d'actions locale** (`lib/core/file_actions.dart`). Une action se met
  en file, s'horodate côté client et se rejoue au retour du réseau.

  **Elle ne prend que trois actions : arrivée, collecte, remise.** Une file
  universelle serait nuisible — rejouer « je passe en ligne » vingt minutes
  plus tard mettrait en ligne un livreur rentré chez lui, et rejouer une
  position périmée le placerait là où il n'est plus. Ces trois-là sont les
  seules où le livreur a fait quelque chose **dans le monde physique**, qui
  reste vrai quel que soit le temps écoulé.

  Elle est **ordonnée** (les trois étapes forment une machine à états), elle
  **s'arrête au premier échec réseau** au lieu de sauter l'entrée bloquante,
  elle **déduplique par clé d'idempotence** — appuyer trois fois sur le même
  bouton sans réseau ne produit qu'une entrée —, et elle vit dans le **coffre
  chiffré**, parce qu'une remise en attente porte le code de remise.

  Elle se vide au démarrage, au retour au premier plan, et à chaque battement
  de position réussi — le battement est la meilleure preuve que le réseau est
  revenu, et il arrive toutes les vingt secondes sans rien coûter de plus.
- **Idempotency-Key sur chaque action mutante.** Le rejeu ne doit jamais
  produire deux effets. Le BFF transmet l'en-tête `Idempotency-Key` tel quel.
- **L'horodatage client est borné côté service** : jamais dans le futur, pas
  plus de 24 h dans le passé. Un téléphone mal réglé ne réécrit pas l'histoire
  d'une course.

## Ce à quoi il faut faire attention

- **L'OTP ne s'affiche jamais.** Le livreur le saisit, dicté par le
  destinataire. Aucun écran ne doit le montrer, ni le journaliser.
- **L'adresse exacte de destination n'arrive qu'après acceptation.** Avant, le
  livreur voit le repère de collecte, la distance et sa rémunération — c'est
  tout ce que contient l'offre.
- **Le prix client n'apparaît jamais.** Seule sa rémunération est renvoyée.
- **Une offre perdue n'est pas une erreur.** `409` avec `rejectionCode` est le
  fonctionnement normal d'une vague : un autre livreur a été plus rapide.
  L'écran doit l'annoncer calmement.
- Un livreur ne porte **qu'une seule offre à la fois**. Au retour de
  l'application, `GET /offers/current` resynchronise.

## Authentification

```
POST /api/driver/v1/auth/otp/request   { phone, deviceId }
POST /api/driver/v1/auth/otp/verify    { challengeId, code, deviceId, displayName }
POST /api/driver/v1/auth/refresh       { refreshToken, deviceId }
POST /api/driver/v1/auth/logout        { refreshToken }
```

Le compte naît à la première vérification, avec le rôle `driver`. Il reste
inactif tant que le KYC n'est pas validé : l'application doit prévoir cet état
intermédiaire, entre « inscrit » et « peut travailler ».

`otp/request` répond la même chose quel que soit le numéro : le nom se demande
après vérification, pas avant.

Le jeton de rafraîchissement **ne sert qu'une fois**. Avec une file d'actions
hors connexion, c'est le point le plus délicat : deux rafraîchissements
concurrents avec le même jeton révoquent la session. Un seul appel de
rafraîchissement à la fois, sérialisé, et le nouveau jeton écrit avant tout
autre appel.

## Démarrage

```bash
cd apps/driver_app
flutter create --org com.hbatechettrade --project-name hba_driver .
```

Base d'API en développement : `http://localhost:5100/api/driver/v1`.
