# Troisième audit de l'application livreur — 27 septembre 2026, fin de journée

Les deux audits précédents tiennent. Celui-ci ne les répète pas : **il audite ce
que la journée a changé**, parce que c'est là que le risque s'est déplacé.

En une journée, l'application est passée de 9 412 à **11 393 lignes de Dart**
(avec `hba_ui`), les tests de 73 à **481 lignes**, et six dépendances sont
entrées : `firebase_core`, `firebase_crashlytics`, `audioplayers`, `vibration`,
`flutter_localizations`, `flutter_foreground_task`.

**Je n'ai exécuté aucune de ces lignes.** Pas de chaîne Dart de mon côté : ni
`flutter analyze`, ni `flutter test`, ni l'application. Tout ce qui suit est lu
dans le code. Les comptages sont exacts ; les appréciations de risque sont des
jugements.

---

## 1. Le risque le plus grave n'est plus dans le code

**Rien n'est commité depuis le 23 septembre.**

```
dernier commit : 2026-09-23 14:38  « config »
git status      : 470 entrées, dont 240 fichiers non suivis
git diff --stat : 205 fichiers, 8 818 insertions, 3 869 suppressions
```

Quatre jours de travail — la console des versements, tout le restyle, la file
d'actions hors ligne, le rail de confirmation, le service de premier plan, les
deux audits, les sept points à trancher — **n'existent que dans le répertoire de
travail**. Un `git checkout .` malheureux, une fusion ratée, un disque qui
lâche : tout part.

La seconde conséquence est plus insidieuse. Quand l'application se mettra à mal
se comporter la semaine prochaine, **il n'y aura rien à bissecter**. Pas de
« ça marchait à ce commit ». Or la journée vient d'ajouter deux mille lignes et
six greffons : c'est exactement le moment où l'on a besoin de pouvoir revenir en
arrière par morceaux.

C'est l'action la moins chère et la plus rentable de toute la liste, et elle ne
demande aucun téléphone.

---

## 2. Ce qui n'a jamais été exécuté

| Fichier | Lignes | Ce qu'il porte |
|---|---:|---|
| `core/file_actions.dart` | 356 | La file hors ligne, l'estampille de propriétaire, la purge |
| `hba_ui/widgets/hba_glissiere.dart` | 268 | Le rail de confirmation, seuil et détente |
| `core/signal.dart` | 202 | Le signal d'offre répété, canal alarme, plafond |
| `core/service_en_ligne.dart` | 171 | Le service de premier plan |
| `core/preferences.dart` | 76 | Le réglage du son |
| `core/coffre.dart` | 39 | L'interface qui rend la file testable |
| `test/file_actions_test.dart` | 220 | **Les tests eux-mêmes n'ont jamais tourné** |
| `hba_ui/test/hba_glissiere_test.dart` | 157 | Idem |

S'y ajoutent **239 chaînes réaccentuées** dans 20 fichiers, et **six feuilles
modales** passées au navigateur racine.

Le service de premier plan a déjà livré deux erreurs de compilation que
l'analyseur a trouvées en quatre secondes — API du greffon, pas logique. C'est
le rappel utile : `flutter analyze` attrape une classe de fautes, il n'en
attrape qu'une.

---

## 3. Les régressions que la journée a pu introduire

Par ordre de ce que je vérifierais.

### 3.1 Six feuilles ont changé de navigateur

`useRootNavigator: true` sur l'offre, le code de remise, le récapitulatif, la
demande de versement et les deux sélecteurs d'image. Elles couvrent maintenant
tout l'écran, barre d'onglets comprise.

**Conséquence voulue** : le bas de la feuille redevient atteignable.
**Conséquence non voulue possible** : pendant une offre, le livreur ne peut plus
changer d'onglet. C'est défendable — l'offre est modale et expire — mais ce
n'est pas ce qui était là hier.

**À vérifier** : que le code de remise **rend toujours sa valeur** au
`_confirmDelivery` qui l'attend, et que les deux sélecteurs d'image rendent
toujours leur `ImageSource`. Une feuille qui change de navigateur change aussi
de `Navigator.pop` — si un `pop` visait l'ancien, la valeur se perd et l'écran
reste muet.

### 3.2 Les 239 accents

J'ai vérifié après coup, et c'est vérifiable : **aucune chaîne accentuée n'est
utilisée dans une comparaison** (`==`) ni à gauche d'un `=>` de filtrage, et les
clés de dictionnaire ont été épargnées. Le risque de casser une correspondance
serveur est donc écarté par la mesure, pas par la confiance.

**Ce qui reste, et que je ne peux pas vérifier** : un accent qui change le sens.
Deux ont déjà été attrapés à la relecture (« HBA vérifié » pour « vérifie »,
« crédité » pour « crédite »). Il peut en rester. Une relecture humaine des
écrans est le seul filet.

### 3.3 Le signal insiste jusqu'à deux minutes

`Signal.offre()` répète son et vibration toutes les trois secondes. Trois
endroits l'arrêtent — l'appui, l'expiration du compte à rebours, la destruction
de la feuille — et un plafond de 120 s rattrape le reste.

**À vérifier sur le téléphone** : qu'il se tait bien à l'acceptation, et qu'il
se tait quand l'offre expire sans que personne n'y touche. Un téléphone qui
vibre quarante fois dans une poche est un défaut qu'on ne rattrape pas à
distance.

### 3.4 `TokenStore` est partagé, et l'application client n'a pas été regardée

La réécriture profite aux deux applications. Mais `client_app` construit son
`TokenStore` **sans `onErreur`** : elle a gagné la résilience et **pas** la
visibilité. Une panne du coffre y est désormais silencieuse — ce qui est
strictement mieux qu'avant, où elle plantait, mais mérite la même ligne de
branchement que côté livreur.

**Et `flutter analyze` n'a pas été lancé sur `client_app`.** C'est le seul
paquet touché aujourd'hui qui n'a reçu aucune vérification.

### 3.5 La file dépend maintenant d'une interface

`FileDActions(Coffre)` au lieu de `FileDActions(FlutterSecureStorage)`. Un seul
point de câblage, dans `providers.dart`. Si ce point est juste, tout l'est ; le
test le prouvera dès qu'il tournera.

### 3.6 Vingt secondes sans offres après une acceptation

`_attendLaCourse` bloque le sondage pendant la relecture de la course. C'est
voulu — on ne propose pas une seconde course à quelqu'un qui vient d'en accepter
une — mais si le serveur ne rend jamais la course, le livreur reste vingt
secondes sans rien, puis reçoit un message. À vérifier que ce cas se termine
proprement.

---

## 4. Ce que la journée a réellement corrigé

Audit 1 : signature (1.3), nom et icône (1.4), file hors ligne (1.5), rapport de
plantage (2.1), commentaires faux (2.4).
Audit 2 : file qui change de propriétaire (1.1), blocage au démarrage (1.2),
accents (1.3), localisation (1.4).
Hors audit : le numéro appelable, l'alignement des boutons, le modal recouvert,
l'écran qui restait ouvert après la livraison, le son et la vibration, le rail
de confirmation, la sortie de secours d'un dossier figé, le service de premier
plan.
Points tranchés : 18 (son d'offre), 21 (écran éteint).

---

## 5. Ce qui reste ouvert

| Origine | Point | État |
|---|---|---|
| Audit 1 | 1.1 Le mensonge sur l'état en ligne | **Partiellement traité.** Le service de premier plan devrait le régler ; à prouver sur appareil. |
| Audit 1 | 1.2 Aucune notification poussée | Ouvert. Chemin B, différé. |
| Audit 1 | 2.2 Un seul fichier de test | Entamé : 481 lignes, dont aucune exécutée. |
| Audit 1 | 2.3 Historique plafonné à 25 courses | Ouvert. |
| Audit 2 | 2.1 900 requêtes par heure | Ouvert. `make mesure-attente` écrit, jamais lancé. |
| Audit 2 | 2.2 Pièces re-téléchargées à chaque ouverture | Ouvert. |
| — | Accessibilité : 5 `Semantics` dans 11 393 lignes | Ouvert. |
| — | 20 tailles de police en dur, facteur d'agrandissement non testé | Ouvert. |
| — | Aucun contrôle de version minimale | Ouvert. |
| Trancher | 15, 16, 17, 19, 20 | Ouverts. 20 a une sortie de secours, pas de parcours. |
| Trancher | 21, points 1 à 4 | Le référentiel doit encore acter le suivi en arrière-plan. |

**Côté vous, en attente** : le keystore de signature, le projet Firebase et
`google-services.json`, la phase d'envoi des dSYM iOS, la reconstruction des
images Payment et Gateway, `make migrate`.

---

## 6. L'ordre dans lequel je vérifierais, avec un téléphone en main

1. **`flutter analyze`** sur `driver_app`, `hba_ui` **et `client_app`** — le
   troisième n'a jamais été regardé.
2. **`flutter test`** sur les deux paquets. Les tests de widget sont la partie
   la plus fragile : distances de glissement et API des sémantiques.
3. **Commiter.** Avant de toucher au téléphone, pas après. C'est le point 1.
4. **En ligne, écran éteint, dix minutes.** La notification apparaît ; la carte
   du back-office doit continuer de voir le livreur. C'est le test du service
   de premier plan, et il ne coûte que dix minutes d'attente.
5. **Une offre**, téléphone en sourdine : le son passe, la vibration se sent,
   le signal se répète, le rail accepte, tout se tait.
6. **Une course entière** : les deux « Appeler », le rail à chaque étape, le
   récapitulatif, l'écran qui se referme.
7. **Mode avion en pleine course** : l'action part en file ; se déconnecter ;
   vérifier que la file est vide. Le test l'affirme, le téléphone le prouve.
8. **`make mesure-attente`**, pour remplacer l'estimation par un chiffre.

---

## Ce que cet audit ne dit pas

Aucune exécution, aucun appareil, aucune mesure. Les comptages de lignes, de
fichiers et l'état de `git` sont exacts. Le reste est de la lecture de code et
du jugement — y compris, et surtout, sur du code que j'ai écrit aujourd'hui,
ce qui est la position la moins confortable pour un auditeur.
