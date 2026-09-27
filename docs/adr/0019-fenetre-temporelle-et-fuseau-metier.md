# 0019 — Fenêtre temporelle demi-ouverte, découpage dans le fuseau métier

Statut : **acceptée** — septembre 2026
Prépare les lectures agrégées du tableau de bord.

## Contexte

Le tableau de bord additionnait une page de courses dans le navigateur. Le
rendre dynamique veut dire déplacer le calcul vers les services — et quatre
services vont devoir répondre à la même question : « combien, entre telle date
et telle date ? »

Sans convention commune, chacun répondra à sa façon. Les deux pièges sont
connus et silencieux : ils ne font pas tomber le service, ils décalent des
chiffres que personne ne pense à vérifier.

Le premier est le fuseau. Le Bénin est à UTC+1 : découper les journées en UTC
coupe la journée à 01 h du matin locale, et une course créée à 00 h 30 à
Cotonou est comptée dans la veille. Sur un histogramme, la barre est fausse des
deux côtés à la fois.

Le second est la borne. Deux fenêtres à bornes incluses se recouvrent d'un
instant : une course créée exactement à minuit appartient à la veille **et** au
lendemain. La somme de douze mois dépasse alors l'année, d'un écart trop petit
pour être remarqué et trop constant pour être un hasard.

## Décision

**Le contrat ne transporte que des instants absolus.** `hba.common.v1.TimeWindow`
porte deux `Timestamp`. Une date locale sur le fil ne dit pas de quel minuit
elle parle : celui du serveur, celui du navigateur, ou celui du pays.

**La borne basse est incluse, la borne haute est exclue.** Deux fenêtres
consécutives pavent alors le temps sans trou ni recouvrement, et
`TimeWindow.Previous()` — la période précédente, celle des écarts — se déduit
sans ambiguïté.

**Le découpage se fait dans le fuseau métier**, pas en UTC. Le fuseau est un
réglage, `Time:ZoneId`, et non une constante : le Bénin est à UTC+1 toute
l'année, mais le Mali et le Sénégal sont à UTC+0, et HBA vise l'UEMOA.

**Le service calcule la clé locale et la renvoie.** Un point de série porte sa
clé — `2026-09-26`, `2026-09` — en plus de son instant. Si le client la
recalculait depuis l'instant, il la décalerait d'un jour une fois sur deux, et
l'erreur n'apparaîtrait qu'en comparant deux écrans.

**Les journées vides figurent dans la série.** `ITimeCalendar.Keys` énumère
toutes les clés que la fenêtre traverse. Un `GROUP BY` ne rend aucune ligne
pour un jour sans course : sans cette liste, le graphique resserre ses barres
et un creux disparaît au lieu de s'afficher.

**Une fenêtre absente vaut trente jours ; une fenêtre de plus de 366 jours est
refusée.** Le défaut est dans le mapper partagé, pour que tous les écrans
s'accordent. Le plafond est un garde-fou, pas une règle métier : sans lui, un
« depuis 1970 » envoyé par erreur balaie la table et ralentit la base pour tout
le monde.

**Le service refuse de démarrer si le fuseau est introuvable.** Sans ce refus,
`TimeZoneInfo` retomberait sur un fuseau par défaut : toutes les journées
seraient décalées d'une heure, tous les totaux resteraient plausibles, et rien
ne le signalerait.

## Conséquences

- Le regroupement se fait en base, pas en mémoire. Le motif est toujours le
  même, et l'identifiant du fuseau est validé au démarrage précisément parce
  qu'il finit dans du SQL :

  ```sql
  SELECT to_char(created_at AT TIME ZONE @zone, 'YYYY-MM-DD') AS cle,
         count(*)                                             AS valeur
    FROM delivery.deliveries
   WHERE created_at >= @from AND created_at < @to
   GROUP BY 1
  ```

  `created_at` est un `timestamptz` : `AT TIME ZONE` rend l'heure locale
  correspondante, et le `>=` / `<` reproduit le demi-ouvert.

- Une plage de dates réclame un index sur la colonne d'horodatage seule.
  Delivery n'a aujourd'hui que `Status` et `(CustomerId, CreatedAt)` : l'étape
  suivante ajoute la migration.

- `tzdata` doit être présent dans l'image d'exécution. Les images Debian de
  .NET l'embarquent ; si une base change, le service le dira au démarrage
  plutôt que de compter faux.

## Alternatives écartées

**Laisser le client envoyer une date locale et le service l'interpréter.** Plus
simple à écrire, et c'est exactement la source du décalage d'un jour : le
service devrait deviner de quel fuseau parle la date, et le navigateur d'un
opérateur en déplacement n'est pas dans celui du pays.

**Tout regrouper en UTC et laisser le client recaler.** Déplace le problème
chez chaque client — la console, une application mobile, un export — et le
résout donc trois fois, différemment.

**Figer le décalage à +1 heure.** Exact pour le Bénin aujourd'hui, faux pour la
moitié de l'UEMOA, et invisible le jour où un pays change d'heure.
