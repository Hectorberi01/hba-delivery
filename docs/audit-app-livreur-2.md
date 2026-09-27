# Deuxième audit de l'application livreur — 27 septembre 2026

Le premier audit (`audit-app-livreur.md`) reste valable : ce document ne le
remplace pas, il le complète. Cinq de ses points ont été corrigés depuis, un
sixième l'a été le jour même. Ce qui suit est une relecture complète du code à
son état actuel — **9 412 lignes de Dart dans `apps/driver_app/lib`** — et
s'attache à ce que le premier audit n'avait pas vu.

**Ce que cet audit est, et ce qu'il n'est pas.** Tout ce qui suit vient de la
lecture du code. Je n'ai pas de chaîne Dart de mon côté : je n'ai lancé ni
`flutter analyze`, ni `flutter test`, ni l'application. Les nombres présentés
comme des comptages sont exacts (ils viennent d'un balayage des fichiers) ; les
estimations de données et de batterie sont annoncées comme telles, avec leur
calcul. Rien ici n'a été vérifié sur appareil.

---

## 1. Les trois défauts qui n'étaient pas dans le premier audit

### 1.1 La file d'actions hors ligne survit à la déconnexion — et change de propriétaire — CORRIGÉ le 27 septembre 2026

C'est le plus grave de ce document, parce qu'il touche la **preuve de remise**.

Les faits :

- `FileDActions` écrit sous une clé unique, `hba.driver.file_actions`
  (`lib/core/file_actions.dart`). **Elle ne porte pas l'identifiant du
  livreur.**
- `SessionController.signOut()` appelle `AuthRepository.signOut()`, qui appelle
  `TokenStore.clear()`. Celui-ci efface `access`, `refresh` et rien d'autre.
  **La file n'est jamais vidée.**
- `HomeScreen.initState` appelle `_viderLaFile()` à chaque première ouverture.

Le scénario qui en découle : le livreur A confirme une remise sans réseau ;
l'action part en file, **avec le code OTP dicté par le destinataire**. A se
déconnecte. Le livreur B se connecte sur le même téléphone — un téléphone
partagé, un livreur qui remplace un autre, un appareil de test qui passe de
main en main. À la première ouverture, la session de B rejoue l'action de A
**avec le jeton de B**.

Deux issues, toutes deux mauvaises : soit le serveur accepte, et la remise de A
est confirmée par B — la preuve de livraison est fausse et personne ne le sait ;
soit il refuse, et B reçoit une erreur qu'il ne peut pas comprendre.

Et même sans second livreur : le code de remise, qui est la preuve qu'un colis a
été remis, **reste dans le coffre du téléphone après la déconnexion**, sans
limite de durée.

**Corrigé en trois gestes**, et non deux — le troisième est celui qui manquait
au diagnostic initial :

1. `FileDActions.purger()` efface la file **et** la clé de propriétaire. Les
   actions encore en attente au moment du départ sont signalées à `Plantages`
   avant d'être perdues : se déconnecter en laissant une remise non partie est
   un vrai incident, la course restera ouverte côté serveur.
2. `SessionController.signOut()` purge **avant** d'effacer les jetons. Si la
   purge échoue, la déconnexion se fait quand même — rester connecté de force
   serait pire.
3. **`onSessionLost` purge aussi.** C'est l'autre porte de sortie de
   l'application — un jeton de rafraîchissement révoqué, une session fermée
   ailleurs — et elle ne passe pas par `signOut()`. Sans elle, la moitié du
   correctif ne servait à rien.

Et, en ceinture et bretelles, chaque entrée porte désormais l'identifiant du
livreur qui a fait le geste (estampillé **à la mise en file**, jamais au rejeu).
`vider()` écarte et signale ce qui ne lui appartient pas. Ce filtre rattrape ce
que la purge n'a pas pu faire : application tuée au milieu de la déconnexion,
coffre qui refuse l'écriture. Rejouer l'action d'un autre falsifierait une
preuve de livraison ; la perdre ne fait qu'un incident visible.

**Pas de clé par livreur**, et c'était une fausse bonne idée : l'identifiant
vient de `/me`, qui ne répond pas hors ligne — c'est-à-dire exactement quand la
file sert. Une clé indexée dessus aurait été vide au pire moment. Le
propriétaire est donc écrit dans le coffre dès que `/me` répond, et relu au
démarrage même sans réseau.

**Aucun test.** Il vient au point 6 du plan.

### 1.2 Un échec du trousseau laisse l'application sur l'écran de démarrage, pour toujours — CORRIGÉ le 27 septembre 2026

`TokenStore.load()` fait `await _storage.read(...)` sans `try`.
`SessionController.restore()` l'appelle sans `try` non plus, depuis un
`Future.microtask` de `main.dart`. Si cette lecture lève, l'état reste
`SessionUnknown` — et le routeur renvoie alors **tout** vers `/demarrage`, dont
l'écran est un `CircularProgressIndicator` seul, sans texte et sans bouton.

Le mécanisme est certain : il se lit dans le code. Ce qui l'est moins, c'est la
fréquence du déclencheur — `flutter_secure_storage` sur Android lève quand le
keystore est invalidé (mise à jour du système, restauration depuis une
sauvegarde, changement du verrouillage d'écran). **Je ne l'ai pas reproduit**,
et il faudrait le faire avant de dimensionner le correctif.

Ce qui est sûr, c'est qu'aucun chemin ne sortait de cet écran : ni message, ni
« réessayer », ni déconnexion. Le livreur voyait un rond qui tourne, relançait,
et revoyait un rond qui tourne.

**Corrigé à la source, pas au symptôme.** `TokenStore` a été réécrit : **aucune
de ses méthodes ne lève plus**. Chacune retombe sur le comportement le moins
coûteux pour le livreur — repartir déconnecté plutôt que bloqué, garder le jeton
en mémoire vive quand l'écriture échoue pour que la journée en cours se termine,
produire un identifiant d'appareil éphémère plutôt que refuser la connexion. La
classe expose `coffreIndisponible`, qui dit que la session ne survivra pas au
prochain démarrage, et un rappel `onErreur` que l'application branche sur
`Plantages` — sans quoi une panne du coffre disparaîtrait désormais sans bruit,
ce qui est le défaut inverse.

`SessionController.restore()` est enveloppé à son tour : il est lancé depuis un
`Future.microtask` que personne n'attrape, et il doit aboutir quoi qu'il arrive.

**L'écran de démarrage parle au bout de huit secondes** et propose « Réessayer ».
C'est une assurance, pas le correctif : le blocage connu est traité au-dessus.
Huit secondes et pas deux, parce que `restore()` enchaîne sur `/me`, dont le
délai d'attente est de quinze secondes — parler trop tôt ferait clignoter une
alerte à chaque démarrage un peu lent, c'est-à-dire souvent.

**`TokenStore` est partagé avec l'application client**, qui hérite du même
durcissement sans rien changer chez elle : le rappel `onErreur` est optionnel.

**Ce qui reste non vérifié :** je n'ai pas reproduit l'invalidation du keystore.
Le blocage est corrigé par construction — plus aucune exception ne peut
s'échapper de ce chemin —, mais le comportement réel d'un téléphone dont le
keystore vient d'être invalidé reste à observer.

### 1.3 Toute l'interface est en français sans accents — CORRIGÉ le 27 septembre 2026

Un balayage des 9 412 lignes rend **zéro caractère accentué dans les chaînes
Dart** — et environ **71 chaînes visiblement désaccentuées** à l'écran :

- « Course livree », « 6 Livrees », « Affichees », « Deposee. Non modifiable. »
- « Votre remuneration », « Immatriculation », « Pas de reseau. Reessayez. »
- « Vous pourrez passer en ligne des que vos pieces auront ete verifiees. »

La convention du dépôt — pas d'accents dans les commentaires — a débordé sur
**le texte que lit le livreur**. Ce n'est pas un détail de style : un texte
français sans accents se lit comme une sortie de machine ou un problème
d'encodage. C'est la première impression de sérieux que donne une application à
laquelle un livreur va confier ses revenus.

**Corrigé : 239 chaînes accentuées là où il n'y en avait aucune, dans 20 fichiers.** La passe a été faite
par un script, mais pas aveuglément — et c'est ce qui l'a rendue sûre :

- Les **régions d'interpolation** (`${...}`, `$nom`) sont découpées et
  laissées intactes. Sans cette précaution, `${entree.chemin}` serait devenu
  `${entrée.chemin}` et le code n'aurait plus compilé.
- Les **clés de dictionnaire et d'index** (`'cle': x`, `json['cle']`) sont
  épargnées : ce sont des noms de champs dans un fichier déjà écrit sur les
  téléphones, pas du texte pour l'humain. Les accentuer aurait rendu illisible
  la file d'actions de tout livreur en cours de mise à jour.
- Les identifiants en **PascalCase ou camelCase** (`'NationalId'`,
  `'PendingVerification'`, `'MOTORCYCLE'`) sont exclus : ce sont des codes
  d'API comparés au serveur.
- Les mots dont **l'accent dépend de la phrase** — `declare`, `refuse`,
  `traite`, `retire`, `trouve`, `examine` — ont été laissés au script et
  traités à la main ensuite, avec une relecture de chaque occurrence.

Le script a quand même produit **deux fautes**, rattrapées par cette relecture :
« HBA verifie votre compte » était devenu « HBA vérifié » (participe au lieu du
présent), de même que « une course livrée crédité votre compte ». C'est
exactement le genre d'erreur qu'une passe automatique non relue laisse passer.

Le cas le plus délicat était le `a` seul : préposition dans « un code a six
chiffres », verbe dans « HBA Livreur a besoin de votre position ». Aucune règle
mécanique ne les sépare ; les soixante-neuf occurrences ont été listées et
décidées une par une.

### 1.4 Aucune localisation n'est déclarée — CORRIGÉ le 27 septembre 2026

`MaterialApp.router` n'a ni `localizationsDelegates` ni `supportedLocales`. Les
textes que Flutter fournit lui-même restent donc **en anglais** : le menu de
sélection de texte (« Paste », au moment précis où le livreur colle son code
OTP), l'info-bulle du bouton retour, les libellés d'accessibilité des barres de
progression.

**Corrigé.** `flutter_localizations` est déclaré, et les trois délégués posés
dans `MaterialApp.router`.

**Le français est imposé, pas déduit du téléphone** : `locale: Locale('fr')`.
Tous les textes de l'application sont écrits en français ; laisser le cadre
suivre la langue de l'appareil donnerait un écran à moitié français et à moitié
anglais chez un livreur dont le téléphone est en anglais — ce qui est courant
sur un appareil d'occasion.

`flutter pub get` est nécessaire avant le prochain build.

---

## 2. Ce que l'attente coûte au livreur

### 2.1 Neuf cents requêtes par heure pour ne rien faire

Les deux minuteries de l'écran d'accueil, quand le livreur est en ligne :

| Minuterie | Intervalle | Requêtes par heure |
|---|---|---|
| Sondage d'offre (`_pollInterval`) | 5 s | 720 |
| Battement de position (`_positionInterval`) | 20 s | 180 |
| | | **900** |

Les deux nombres sont exacts, ils se lisent dans le code. **L'estimation qui
suit n'en est qu'une** : avec un jeton JWT dans chaque en-tête et une réponse
vide côté offre, une requête HTTPS coûte de l'ordre du kilo-octet et demi aller
et retour, soit **environ 1,3 Mo par heure d'attente**. Sur une journée de dix
heures passée en ligne sans recevoir une seule course : une dizaine de
mégaoctets, et un point GPS toutes les vingt secondes pendant dix heures.

Le dépôt invoque partout, à juste titre, que « le livreur paie ses données à la
recharge » — la compression des pièces du dossier a été écrite pour cette
raison. C'est ici que la facture se paie vraiment, et personne ne l'a regardée.

Ce que je ne tranche pas, parce que cela relève du produit :

1. **Espacer le sondage quand rien n'arrive** — 5 s les deux premières minutes,
   puis 15 s, puis 30 s. Le coût baisse d'un facteur quatre ; le livreur voit
   l'offre jusqu'à trente secondes plus tard, et le dispatch a déjà un délai
   d'expiration par vague.
2. **Se taire quand l'écran est éteint.** Aujourd'hui les minuteries ne sont pas
   arrêtées en arrière-plan, et c'est délibéré (commentaire de `_reprendre`) —
   la raison donnée vaut pendant une course, pas pendant l'attente.
3. **La vraie réponse reste la notification poussée** (point 1.2 du premier
   audit) : elle supprime le sondage entier.

**L'instrument existe maintenant : `make mesure-attente`.**

Je n'ai pas pu faire la mesure moi-même — elle demande la passerelle, qui tourne
sur votre Mac et qu'aucun de mes deux shells n'atteint. Ce qui a été écrit à la
place, c'est de quoi la faire en une commande : le script crée un livreur, le
valide, le met en ligne, puis appelle vingt fois chacune des deux routes de
l'attente et demande **à curl** combien d'octets sont passés — requête,
en-têtes de réponse et corps. Le jeton est un vrai JWT de livreur, donc
l'en-tête `Authorization` pèse ce qu'il pèse ; le `POST /position` porte sa clé
d'idempotence, trente-six octets à chaque battement que l'estimation oubliait.

**Les deux intervalles sont lus dans `home_screen.dart`, pas recopiés.** Un
chiffre recopié diverge : le jour où le sondage passe à quinze secondes, la
mesure doit suivre seule plutôt que de continuer à décrire une version qui
n'existe plus.

**Le résultat sera un minorant, et le script le dit lui-même.** La passerelle de
développement parle en clair : ni TLS, ni poignées de main, ni reprises sur
réseau instable. Lancé contre la production (`GATEWAY=https://…`), il donne le
vrai chiffre. Et il ne mesure toujours pas la batterie — un point GPS toutes les
vingt secondes pendant dix heures est l'autre moitié de la facture, et elle se
mesure sur un téléphone.

Tant que ce script n'a pas tourné, **le 1,3 Mo reste une estimation** et ne doit
pas servir à trancher.

### 2.2 Les pièces du dossier se re-téléchargent à chaque ouverture

`GET /application` re-signe les URL de lecture à chaque appel. L'adresse change
donc à chaque fois, et le cache d'images de Flutter — qui indexe par URL — ne
sert jamais. Les cinq vignettes de pièces et la photo de profil repartent du
réseau à chaque visite de l'écran Dossier.

Les deux `Image.network` de l'application ont, en revanche, un `errorBuilder` et
un repli propres : ce point-là est bien traité.

---

## 3. Ce qui reste ouvert du premier audit

| Point | État |
|---|---|
| 1.1 Le mensonge sur l'état en ligne | **Ouvert.** Le palliatif (fenêtre de fraîcheur de 120 s) est en place ; le service de premier plan ne l'est pas. |
| 1.2 Notification poussée | **Ouvert.** Rien n'arrive au livreur écran verrouillé. |
| 2.2 Un seul fichier de test | **Entamé le 27 septembre 2026.** Trois fichiers : 73 lignes de modèles, 220 sur la file d'actions, 154 sur le rail de confirmation. Le reste de l'application n'a toujours rien. |
| 2.3 Historique plafonné à 25 courses | **Ouvert.** |
| 2.4 Deux commentaires devenus faux | **Corrigé le 27 septembre 2026.** Ils étaient trois, plus un nom de classe (`_CeQuiManque` → `_LienDossier`). |

Sur les tests, le détail vaut d'être dit. `test/models_test.dart` couvrait le
parsage des modèles, et rien d'autre.

**Deux fichiers ont été ajoutés le 27 septembre**, sur les deux endroits que le
plan désignait :

- `driver_app/test/file_actions_test.dart` — l'appartenance des actions, la
  déduplication par clé d'idempotence, le plafond, l'aller-retour dans le
  coffre, la purge de déconnexion, et un coffre qui tombe. Un test vérifie
  nommément que **le code de remise ne survit pas à la déconnexion**, en
  cherchant le code en clair dans tout le contenu du coffre après la purge.
  Un autre écrit la clé de stockage en toutes lettres : la renommer viderait
  la file de tous les livreurs déjà équipés, silencieusement, au moment de la
  mise à jour — ce test échouera d'abord.
- `hba_ui/test/hba_glissiere_test.dart` — le seuil dans les deux sens, la
  détente rapide, le rail désactivé, le rail occupé, et **l'activation par
  lecteur d'écran**, qui est le seul chemin d'acceptation pour un livreur
  malvoyant.

Pour rendre le premier possible, `FileDActions` ne dépend plus de
`FlutterSecureStorage` mais d'une interface de trois méthodes (`Coffre`). La
seule alternative aurait été de simuler à la main le canal de plateforme du
greffon, c'est-à-dire d'écrire en dur un nom de canal interne qui peut changer
à la version suivante sans prévenir. Le tri des actions par propriétaire est
devenu une fonction pure pour la même raison : c'est la règle la plus
dangereuse du fichier — elle décide ce qui part sous le nom du livreur connecté
— et elle s'éprouve maintenant sans coffre, sans réseau et sans téléphone.

**Ces deux fichiers n'ont jamais été exécutés de mon côté**, faute de chaîne
Dart. Les tests de widget sont la partie la plus fragile : les distances de
glissement tiennent compte du « touch slop » de Flutter, et l'activation par
lecteur d'écran passe par l'API des sémantiques. C'est `flutter test` qui
tranche.

**Restent sans test** : la fenêtre de fraîcheur, le parcours de remise avec
code, et tous les écrans.

---

## 4. Ce qui est neuf, et qui n'a aucun filet

Tout ce qui a été écrit dans la journée du 27 septembre est en production sans
un seul test :

- **`HbaGlissiere`** — le seuil à 70 %, la détente rapide, et surtout le retour
  différé de 900 ms qui rattrape les actions ne passant pas par `busy`. Ce
  dernier est typiquement ce qui casse en silence.
- **`FileDActions`** — plafond de 50, déduplication par clé d'idempotence,
  arrêt au premier échec réseau.
- **`Signal`** — le plafond de 120 s d'insistance, et le fait que `silence()`
  soit appelé depuis trois endroits différents.
- **`SonDOffre`** — le défaut « actif » rendu avant la lecture du coffre.

---

## 5. Petites choses, vraies quand même

- **Trois formateurs de date recopiés**, dans `courses_screen`, `gains_screen`
  et `profil_screen`, avec trois formats différents (`27/09 a 21h13`,
  `27/09/2026`). Ils divergeront.
- **Cinq `Semantics` dans 9 412 lignes.** L'application n'est pas utilisable au
  lecteur d'écran, et ce n'est pas qu'une question de conformité : un livreur
  malvoyant existe.
- **Vingt tailles de police écrites en dur** et aucune prise en compte du
  facteur d'agrandissement du système. **Non vérifié** : il faut un appareil
  réglé à 200 % pour savoir si la feuille d'offre déborde.
- **`Signal.liberer()` est appelé dans le `dispose` du widget racine** — qui
  n'est jamais appelé quand Android tue le processus. Le filet ne sert qu'au
  redémarrage à chaud.
- **Aucun contrôle de version minimale.** Le jour où une route change de forme,
  les applications déjà installées cassent sans rien dire à personne.
- **Quatre-vingt-quinze `catch (on Object)`** dans l'application. La plupart
  sont justifiés et commentés ; il faudrait relire ceux qui n'écrivent ni dans
  `Plantages` ni à l'écran, parce qu'un échec qu'on ne voit nulle part est un
  échec qu'on ne corrigera jamais.

---

## Ce que je ferais dans l'ordre

1. **1.1** — vider et cloisonner la file d'actions. C'est une demi-heure, et
   c'est la preuve de remise.
2. **1.2** — attraper l'échec du trousseau. Quelques lignes contre un blocage
   total.
3. **1.3 et 1.4** — la passe d'accents et les deux lignes de localisation.
   C'est ce que le livreur voit en premier.
4. **2.4 du premier audit** — les trois commentaires faux, avant qu'ils
   n'induisent quelqu'un en erreur.
5. **Mesurer** la consommation d'une heure en ligne, avant de toucher aux
   intervalles.
6. **Des tests sur la file d'actions et sur le glissement**, dans cet ordre.

Le reste — service de premier plan, notification poussée, historique paginé —
reste ce que disait le premier audit, et n'a pas bougé.
