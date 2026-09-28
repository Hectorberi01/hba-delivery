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
