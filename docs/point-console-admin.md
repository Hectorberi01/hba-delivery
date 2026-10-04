# Point sur la console d'administration — 27 septembre 2026

`apps/web` : **6 195 lignes** de TypeScript/TSX, Next.js 16, React 19, Tailwind 4.
Six pages : tableau de bord, commandes, livreurs, clients, versements, paramètres.

**`npx tsc --noEmit` passe proprement.** C'est la seule vérification que j'aie pu
exécuter moi-même dans cette session — les types tiennent. Tout le reste
ci-dessous est de la lecture de code.

---

## Ce qui est bien fait, et qui mérite d'être dit avant les défauts

**Le relais `/api/hba/[...chemin]`.** Le jeton ne quitte jamais le serveur ; les
préfixes relayés sont en liste blanche (`admin/v1`, `web/v1`, `merchant/v1`) ;
un 401 déclenche **un seul** rafraîchissement puis rejoue. Le commentaire nomme
le risque évité — « un jeton d'administrateur sur n'importe quel chemin est un
député confus ». C'est exactement la bonne façon de tenir un BFF.

**Le 403 est traité là où il compte.** Sur la fiche client, le cumul facturé et
le journal des accès sont réservés à `admin` : la console les fait disparaître
**en silence** pour ops et support, au lieu d'afficher une erreur. Le
commentaire l'assume : « inutile d'annoncer à ops ce qu'il n'a pas le droit de
voir ».

**Les politiques côté passerelle sont pensées.** `AnnuaireClients` exclut
délibérément `finance`, avec la raison écrite : rattacher un paiement se fait
par identifiant, parcourir un annuaire de personnes n'entre pas dans son métier.

---

## 1. Les rôles sont décoratifs dans la navigation

L'en-tête affiche `principal.roles.join(', ')`. **C'est le seul endroit où les
rôles servent.** Les six onglets sont montrés à tout le monde.

Conséquences concrètes :

- Un utilisateur **`finance`** voit l'onglet **Clients**, qui lui répondra 403
  en bloc : `AnnuaireClients` l'exclut.
- Un utilisateur **`support`** ou **`ops`** voit l'onglet **Versements** et ses
  boutons *Approuver* / *Refuser* / *Virement consigné*, que le service Payment
  refusera : `PayoutReview.EnsureReviewer` n'accepte que `finance` et `admin`.

Le serveur tient. L'interface, elle, propose des portes fermées — et c'est le
défaut que cette session a nommé une dizaine de fois : un refus qui arrive après
le geste, au lieu d'un geste qu'on ne propose pas.

---

## 2. La passerelle est plus permissive que le référentiel sur les versements

`AdminPayoutEndpoints` protège `/api/admin/v1/payouts` par
`HbaPolicies.BackOffice`, c'est-à-dire **les quatre rôles internes**. Le
référentiel dit : « **`finance` et `admin` instruisent, pas `ops` ni
`support`** ».

Et `HbaPolicies.Finance` **existe** — défini dans `SecurityExtensions`, avec
exactement la bonne règle — **et n'est utilisé nulle part**.

**Ce n'est pas une faille**, et il faut le dire clairement : le service Payment
revérifie (`PayoutReview.EnsureReviewer`), conformément à la règle du
référentiel selon laquelle toute autorisation se vérifie côté service. La
défense en profondeur fonctionne.

Mais la première ligne est plus large que la règle, et le jour où quelqu'un
ajoutera une route de versement en oubliant la revérification côté service, la
passerelle ne le rattrapera pas. Le correctif est **une ligne**.

---

## 3. Quatorze routes exposées, aucun écran pour les appeler

| Route | Ce qui manque |
|---|---|
| `GET /merchants`, `GET /merchants/{id}`, `POST /merchants`, `POST /accounts/merchant` | **Tout le côté commerçant.** Aucun écran, aucun lien, rien. |
| `POST /deliveries/{id}/reassign` | Réaffecter une course à un autre livreur — une capacité d'ops invisible. La console sait clore et proposer à la main, pas réaffecter. |
| `POST /accounts/{id}/suspend`, `/reactivate` | La suspension au niveau du **compte** (Identity). Celle du **livreur** (Driver) a son bouton ; celle du compte n'en a pas. |
| `POST /partners/{id}/rotate-secret` | La page Paramètres crée des partenaires mais ne fait pas tourner leur secret. |
| `GET /kpi/deliveries`, `/kpi/payments`, `/kpi/dispatch` | Trois vues détaillées que le serveur calcule et que le tableau de bord n'affiche pas. Il n'utilise que `/kpi` et `/kpi/drivers`. |

**Le trou fonctionnel, c'est le commerçant.** HBA Food et les partenaires B2B
en dépendent, le service Directory le gère, la passerelle l'expose, et personne
ne peut en créer un depuis la console.

---

## 4. Zéro test

6 195 lignes, **aucun fichier de test** — ni unitaire, ni de rendu, ni de bout
en bout. `tsc` couvre les types, ce qui est réel mais étroit : il garantit qu'un
nombre est un nombre, pas que ce soit le bon.

Le fichier qui le mérite le plus est `src/lib/agregats.ts` — **216 lignes de
calculs** dont les résultats s'affichent comme des chiffres de pilotage. C'est
le genre d'endroit où une erreur ne se voit jamais : un total faux reste un
nombre plausible, et personne ne recompte à la main un tableau de bord.

---

## 5. Petites choses

- **`expliquer()` ne connaît que le 404.** Un 403 sans corps rend « Erreur 403 »,
  ce qui ne dit ni « vous n'avez pas le droit », ni lequel de vos rôles manque.
  C'est le message que verront ops et support en cliquant sur Versements tant
  que le point 1 n'est pas fait.
- **Le message du 404 est excellent** et vaut d'être copié ailleurs : il nomme la
  route et dit quoi faire (« son image est probablement antérieure à cet écran :
  reconstruisez-la »). Il a été écrit après un vrai incident.

---

## Ce que je ferais, dans cet ordre

1. **`HbaPolicies.Finance` sur le groupe des versements.** Une ligne, et la
   passerelle cesse d'être plus large que la règle.
2. **Les onglets selon les rôles**, et un message honnête à la place du 403 nu.
   Une demi-journée, et la console cesse de proposer ce qu'elle refusera.
3. **L'écran commerçants.** C'est le vrai manque fonctionnel, et il est déjà
   servi par la passerelle.
4. **Des tests sur `agregats.ts`**, avant qu'un chiffre faux ne serve à décider
   quelque chose.

Le reste — réaffectation, rotation de secret, KPI détaillés — est du confort
d'exploitation, à prendre quand le besoin se fera sentir.

---

# Addendum — 1er octobre 2026 : l'écran des tarifs

**`apps/web/src/app/(console)/tarifs/page.tsx`**, 653 lignes. `npx tsc --noEmit`
passe. Il n'y a pas de script `lint` dans ce paquet, et toujours aucun test.

## Ce que l'écran fait

La liste des grilles, filtrée par zone, nature, véhicule, et avec ou sans les
versions closes. La création d'une grille. La clôture d'une grille en vigueur,
en deux appuis — « Clore » puis « Confirmer », sans boîte de dialogue du
navigateur. Et « Nouvelle version », qui reprend une grille existante dans le
formulaire : c'est l'opération ordinaire, puisqu'**on ne modifie jamais une
grille, on en crée une version** (ADR 0004) — un devis émis hier se réclame de
la grille d'hier.

**Ce que l'écran débloque.** Jusqu'ici la seule grille du système était semée au
démarrage depuis la configuration, pour un couple (zone, véhicule) : changer un
prix demandait d'éditer une variable d'environnement et de recréer le conteneur,
et **aucune grille de course n'existait**, donc chaque devis de course échouait
sur `NO_TARIFF`. Elle se crée maintenant depuis la console.

**Aucun montant n'est pré-rempli pour une nature neuve, et c'est délibéré.** Les
prix d'une course de personnes ne sont ni ceux d'un colis, ni une variation de
ceux-là — ni le plancher, ni la part du chauffeur. Proposer les seconds comme
point de départ donnerait des chiffres faux qui ont l'air justes.

## Deux conversions, et pourquoi elles existent

**Les pourcentages — et il y en a DEUX, de sens opposés.** Le contrat compte en
**points de base** dans les deux cas, et c'est le piège qui a fait refuser les dix
premières grilles créées depuis cet écran.

- **La part du livreur est une fraction** : 10000 vaut la totalité, donc 80 %
  s'écrit 8000. Le domaine la veut dans ]0, 10000].
- **La majoration est un multiplicateur** : 10000 vaut 1,00, c'est-à-dire *aucune*
  majoration, et le domaine refuse en dessous — « une majoration ne peut pas
  réduire le prix ». Envoyer 200 pour « 2 % » revenait à demander de multiplier le
  prix par 0,02.

L'écran demande une hausse dans les deux cas — « 80 » et « 2 » — et convertit
chacune selon sa règle. La constante `NEUTRE` existe pour que les deux ne se
confondent pas une seconde fois.

**Les plages horaires.** Le contrat compte en **minutes depuis minuit**, heure
locale : 1320 et 360 écrivent 22 h → 6 h, qui enjambe minuit. L'écran demande
deux heures en `HH:MM`. Les deux bornes vides valent la journée entière, et c'est
Pricing qui le traduit — en JSON comme en protobuf3, l'absence et le zéro sont la
même chose.

## Un défaut trouvé en chemin : la clé d'idempotence n'arrivait pas

`ressource.ts` posait l'en-tête **`hba-idempotency-key`** — le nom de la
métadonnée **gRPC**, celle qui circule entre les services. La passerelle, elle,
lit l'en-tête HTTP **`Idempotency-Key`**, comme les deux applications mobiles le
font depuis toujours. La clé partait du navigateur, traversait la procuration, et
n'était lue par personne.

**Personne ne s'en était aperçu parce qu'aucun écran n'avait encore passé de
clé** : les sept écritures de la console appellent `envoyer` sans l'option. La
création d'une grille est la première à en avoir besoin — et la route la refuse
sans elle, par un 400 explicite. Corrigé des deux côtés : le navigateur envoie le
nom standard, la procuration accepte les deux orthographes en entrée et n'en
émet qu'une.

**La clé est tirée par montage du formulaire**, et c'est la bonne portée : un
double appui rejoue la même clé et Pricing rend la grille déjà créée au lieu
d'en poser une seconde qui clorait la première à la même seconde. Une clé de
portée page, elle, aurait fait rendre silencieusement la première grille à la
seconde demande.

## Le point 1 de ce document est entamé, pas réglé

L'onglet « Tarifs » n'apparaît que pour `admin` et `finance`, parce que la
passerelle exige `HbaPolicies.Finance` et que Pricing le revérifie (ADR 0007).
`ONGLETS` porte donc un champ `roles` facultatif, et l'en-tête filtre.

**Les six autres onglets restent montrés à tout le monde**, dont « Clients » à
`finance`, qui recevra 403 en bloc. Le mécanisme existe maintenant ; l'appliquer
aux autres demande de décider, onglet par onglet, quelle population le voit — et
cette décision n'a pas été prise ici.

## Ce qui reste du point 3

Les grilles tarifaires quittent la liste des routes sans écran. **Le trou
fonctionnel reste le commerçant** — `GET /merchants`, `POST /merchants`,
`POST /accounts/merchant` : HBA Food et les partenaires B2B en dépendent, le
service le gère, la passerelle l'expose, et personne ne peut en créer un depuis
la console.

---

# Addendum — 1er octobre 2026, suite : la carte des zones

**`apps/web/src/app/(console)/zones/page.tsx`.** `npx tsc --noEmit` passe. Toujours
aucun test.

## Ce que l'écran fait

La carte de Cotonou, les zones en polygones colorés — les quatre couleurs de série
de `globals.css`, reprises en hexadécimal parce que Google Maps ne lit pas une
variable CSS. Une zone désactivée garde son trait et perd son remplissage : elle
reste visible, puisque c'est justement elle qu'on vient vérifier. **La priorité
décide de l'empilement**, exactement comme en base : la zone qui gagne un
chevauchement est dessinée au-dessus de celle qu'elle recouvre.

**On dessine en cliquant.** Chaque clic pose un sommet, qui s'affiche ; le tracé se
referme dès le troisième. « Annuler le dernier point » et « Abandonner le tracé »
sont là, et « Redessiner » reprend une zone existante en gardant son code — c'est
lui qui lie la zone à ses grilles, et le changer créerait une seconde zone en
laissant les grilles de la première sans territoire.

**La bibliothèque « drawing » de Google n'est pas chargée**, et c'est un choix :
son `DrawingManager` apporterait un script et une dizaine de types pour un geste
que trois clics font aussi bien. Le prix de ce choix est qu'un sommet ne se
déplace pas à la poignée — on reprend le tracé. C'est le premier endroit à
améliorer si le découpage devient fin.

## Les grilles se démultiplient

Le formulaire des grilles accepte désormais **« Les deux natures »** et **« Tous
les véhicules »**, et crée alors jusqu'à dix grilles en une fois. Trois choses à
savoir :

- **Une clé d'idempotence par couple**, et surtout pas la même pour tous : avec une
  clé unique, la deuxième grille de la boucle serait un rejeu, Pricing rendrait la
  première sans rien créer, et neuf grilles sur dix manqueraient en silence.
- **Le nombre est annoncé sur le bouton** avant l'appui. Dix grilles d'un seul
  geste est ce qu'on veut, et ce qu'on ne veut pas découvrir après coup.
- **Un échec partiel ne ferme pas le formulaire** : sept créées, trois refusées —
  la liste se recharge et les trois refus sont nommés avec leur couple. Fermer
  effacerait le détail, ne pas recharger cacherait les réussites.

**Et la zone se choisit dans une liste**, alimentée par les zones réellement
dessinées. Une faute de frappe dans un code de zone ne se voyait pas : elle créait
une grille pour un territoire inexistant, que personne ne consulterait jamais,
pendant que la vraie zone continuait d'échouer sur `NO_TARIFF`. Le champ libre
reste le repli tant qu'aucune zone n'est dessinée — sinon un dépôt neuf n'aurait
aucun moyen de créer sa première grille.

## Ce qui reste

- **Pas de déplacement de sommet.** Voir plus haut : on reprend le tracé.
- **Pas de cercles.** Une zone « centre et rayon » se convertirait en polygone à
  l'enregistrement, et la base ne verrait pas la différence (point 40).
- **L'écriture est réservée à `admin` et l'écran ne le devine pas.** Les boutons
  sont affichés à tout le back-office et le refus de la passerelle s'affiche tel
  quel. Masquer demanderait de connaître les rôles dans la page — le principal
  n'est lu que par le layout, pour l'en-tête — et un 403 explicite vaut mieux
  qu'un bouton qui disparaît sans dire pourquoi.
