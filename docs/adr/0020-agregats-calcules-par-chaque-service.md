# 0020 — Chaque service agrège ce qu'il possède, la passerelle compose

Statut : **acceptée** — septembre 2026
S'appuie sur l'[ADR 0019](0019-fenetre-temporelle-et-fuseau-metier.md).

## Contexte

Le tableau de bord additionnait, dans le navigateur, une page de deux cents
courses. Les chiffres étaient donc justes pour cette page et faux pour
l'entreprise — et l'écran le disait, faute de mieux.

Trois façons de le corriger se présentaient.

## Décision

**Chaque service agrège sa propre base, et la passerelle compose.** Delivery
sait ce qu'il a facturé, Payment ce qu'il a encaissé, Dispatch ce qu'il a
affecté, Driver qui est en ligne. Aucun service ne lit la base d'un autre :
c'est la même règle qui vaut partout ailleurs dans ce dépôt, et une lecture ne
l'assouplit pas.

**L'agrégat se calcule en SQL, pas en mémoire.** Une lecture agrégée passe par
un port distinct du dépôt — `IDeliveryStatsReader`, `IPaymentStatsReader` — et
non par l'agrégat. Charger cent mille agrégats pour en tirer six nombres est
une façon coûteuse de compter.

**Le SQL est écrit à la main.** Le découpage par journée locale (ADR 0019)
passe par `AT TIME ZONE`, qu'aucun fournisseur EF ne traduit, et les délais
moyens se lisent d'une passe avec des agrégats filtrés. En LINQ, tout cela
serait soit faux — journées coupées en UTC — soit multiplié en requêtes.

**Une moyenne ne sort jamais seule.** Chaque délai moyen est accompagné de son
effectif. Sans lui, « dix-huit minutes jusqu'à l'affectation » se lit comme une
mesure alors qu'au lancement il reposera sur trois trajets.

**La fenêtre appliquée est renvoyée dans la réponse.** L'appelant qui n'en a
demandé aucune doit pouvoir écrire à l'écran sur quoi portent ses chiffres.

**L'autorisation est vérifiée dans chaque handler**, pas à la passerelle
(ADR 0007). Ces lectures agrègent toute l'activité de la plateforme ; un jeton
de service, qui ne porte aucun rôle (ADR 0018), est refusé par la même ligne.

## Le piège nommé : la cohorte n'est pas la recette

Payment répond deux fois à la même fenêtre, et les deux réponses diffèrent.

Les champs `created_` comptent les intentions **ouvertes** dans la fenêtre :
c'est l'entonnoir, celui qui dit combien de tentatives aboutissent. Les champs
`collected_` comptent l'argent **reçu** dans la fenêtre, quelle que soit la
date d'ouverture.

Une intention ouverte le 31 août et payée le 1er septembre appartient à la
cohorte d'août et à la recette de septembre. Les confondre donne une recette
mensuelle qui ne tombe jamais juste, d'un écart trop petit pour être remarqué
avant un rapprochement comptable. Les deux séries portent donc des noms
distincts, et chacune son index : `created_at` d'un côté, `succeeded_at` de
l'autre.

De la même façon, `billed_xof` chez Delivery est la somme des prix **figés**
dans les courses — ce qu'on a demandé. Ce n'est pas de l'argent reçu. Sur
l'argent, Payment fait foi.

## Conséquences

- Toute plage de dates réclame un index sur la colonne d'horodatage seule.
  Delivery en a reçu un sur `CreatedAt`, Payment deux : `created_at` et
  `succeeded_at`.
- La passerelle expose `/api/admin/v1/kpi/<service>` par service, et les
  compose en une seule réponse quand l'écran les lira ensemble. Un service
  indisponible rendra alors son bloc nul : un tableau de bord qui tombe entier
  parce qu'un service est en maintenance n'est pas un tableau de bord.
- Les périodes vides sont recollées dans le handler, à partir de
  `ITimeCalendar.Keys`. Un `GROUP BY` ne rend aucune ligne pour un jour sans
  activité, et la courbe sauterait le creux au lieu de descendre à zéro.

## Vérification

Un agrégat faux ne lève aucune exception : il s'affiche. Une borne inclusive
de trop, un fuseau qui dérive d'un service à l'autre, un statut oublié dans
une somme — la page reste jolie et le chiffre est faux.

`make etape7` compte deux fois. Une fois par la chaîne complète, en appelant
`/api/admin/v1/kpi` comme le navigateur. Une fois directement dans Postgres,
avec un SQL écrit à part. Puis il compare, chiffre par chiffre.

Trois choix comptent dans ce script :

- **La fenêtre est lue dans la réponse, jamais recalculée.** Recalculer les
  dates de son côté reviendrait à vérifier un calcul avec le même calcul.
  C'est la fenêtre réellement appliquée qui part dans le `WHERE`.
- **Une erreur SQL n'est pas un zéro.** Une table renommée ou une migration
  oubliée fait rendre une chaîne vide à `psql` ; sans garde, le script
  comparerait « 0 » contre « 0 » et annoncerait que tout va bien.
- **Zéro partout n'est pas un succès.** Sur une base vide, une requête fausse
  rend zéro comme une juste. Le script sort alors en 2, pas en 0, et le dit :
  il faut d'abord produire des données avec `make etape4`.

La seule comparaison qui attrape une dérive de fuseau est celle de la journée
la plus chargée. Les totaux y sont insensibles — c'est le découpage en
journées qui ne l'est pas. Un service qui découperait en UTC au lieu
d'`Africa/Porto-Novo` garderait tous ses totaux justes et déplacerait dans la
veille toutes les courses créées avant 1 h du matin.

## Alternatives écartées

**Un service de reporting** qui consomme les événements Kafka dans un modèle de
lecture. C'est la réponse correcte à terme — elle survit à la croissance et
permet des agrégats qu'aucun service ne peut calculer seul. Disproportionnée
pour un pilote à Cotonou : elle demande un service de plus, sa base, ses
consommateurs et sa reprise, avant d'avoir la première course.

**La console qui appelle quatre routes et recolle.** Moins de code côté
serveur, quatre allers-retours, et une logique métier dans le navigateur —
c'est exactement ce dont on sortait.

**Élargir la page lue par la console.** Deux mille courses au lieu de deux
cents ne rend pas le chiffre juste : il le rend seulement plus lent à être
faux.
