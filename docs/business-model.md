# Le modèle économique de HBA Delivery

Ce document décrit **comment ce produit gagne de l'argent**, en partant de ce
que le code fait réellement. En cas d'écart entre ce document et le code, c'est
le code qui a raison.

**Ce document n'invente aucun chiffre de marché.** Le dépôt ne contient ni étude
de marché, ni prévision de volume, ni coût réel d'infrastructure. Tout ce qui
est chiffré ci-dessous vient d'un fichier du dépôt et porte sa source. Tout le
reste est écrit comme une formule à remplir, pas comme un résultat.

---

## 1. Ce qui est vendu, et à qui

HBA Delivery vend **l'exécution d'une livraison physique**. Il ne vend ni le
colis, ni le repas, ni la commande commerciale : le README le dit en deux
lignes, et toute l'architecture en découle. C'est un domaine logistique commun à
**HBA Express** (marketplace), **HBA Food** (restauration) et, à terme, à des
partenaires externes.

Marché : Bénin, pilote à Cotonou, zone UEMOA. Monnaie XOF, montants entiers.

Trois clientèles, et une seule est servie aujourd'hui :

| Donneur d'ordre | Qui paie | État du code |
|---|---|---|
| **Client particulier** (`customer`) | lui-même, par mobile money à chaque course | **en service** |
| **Commerçant** (`merchant_owner`) | non tranché | **création refusée** — `MERCHANT_SETTLEMENT_UNDECIDED` |
| **Partenaire** (`partner`) | non tranché | créée, mais **jamais payée ni dispatchée** |

Ce n'est pas un oubli : le point 1 et le point 2 des points à trancher exposent
les options, et `CreateDeliveryHandler` refuse plutôt que de choisir. Le premier
partenaire branché rendra le sujet visible immédiatement plutôt que six mois
plus tard.

---

## 2. Comment le prix est fait

Le calcul vit dans `QuoteCalculator.Compute`, une fonction pure. Il est le même
pour tout le monde :

```
variable    = prix_au_km × distance_km  +  prix_à_la_minute × durée_min
sous_total  = max(part_fixe + variable, plancher)
majoration  = sous_total × (surge_bp − 10 000) / 10 000
TOTAL       = arrondi_supérieur_à_10(part_fixe + variable + majoration)
```

Quatre choses à en retenir, parce qu'elles ont chacune une conséquence
commerciale.

**Le prix est figé au devis, pas à la livraison** (ADR 0004). Le devis vit
quinze minutes, il est *consommé* à la création de la course, et une
modification de grille ne change jamais une course déjà confirmée. Une grille
n'est pas modifiée : on en crée une nouvelle version et on clôt la précédente.

**Le plancher protège la course courte.** Sous le plancher, c'est la part fixe
qui absorbe la différence — les compartiments continuent de faire le total, ce
qui rend le devis relisible des mois plus tard.

**La majoration existe mais ne s'applique jamais.** `SurgeBasisPoints` vaut
10 000 — soit ×1,00 — et le commentaire du domaine est explicite : *les règles
de déclenchement d'une majoration ne sont pas tranchées*. Le levier est câblé,
il n'est pas armé.

**L'arrondi penche du côté de la plateforme.** Le total est arrondi **au
dizaine supérieure**, la part du livreur **à la dizaine inférieure**. L'écart
— zéro à dix-neuf francs par course — reste chez HBA. Ce n'est pas une source
de revenu, c'est une conséquence qu'il vaut mieux connaître que découvrir.

---

## 3. Comment l'argent se partage

C'est la grille qui porte le partage, dans `DriverShareBasisPoints` :

```
part_livreur  = arrondi_inférieur_à_10(TOTAL × driver_share_bp / 10 000)
marge_brute   = TOTAL − part_livreur − frais d'encaissement
```

**La commission de HBA est donc le complément de la part livreur**, et elle est
un paramètre de grille — par zone, par type de véhicule, par version. Elle peut
différer d'un quartier à l'autre sans une ligne de code.

Le circuit de l'argent, tel qu'il est implémenté :

1. Le client paie la course entière à HBA, par mobile money (FedaPay).
2. Le paiement n'est réel que sur **webhook signé, relu chez le fournisseur**
   (ADR 0017). Aucun autre événement ne confirme un paiement.
3. À la livraison, la part du livreur est inscrite à son **registre**
   (`DriverLedgerEntry`) — c'est le montant du devis, jamais un calcul refait
   après coup.
4. Le livreur **demande** son reversement. Un plancher de retrait et une
   vérification de solde existent (`PAYOUT_BELOW_MINIMUM`,
   `PAYOUT_EXCEEDS_BALANCE`), et une seule demande peut être en cours
   (`PAYOUT_ALREADY_PENDING`).

HBA encaisse donc **la totalité** de chaque course et porte la trésorerie du
livreur entre la livraison et le reversement. C'est un avantage de flux, et un
engagement : ce solde n'appartient pas à HBA.

---

## 4. La marge unitaire, avec les seuls chiffres que le dépôt porte

La grille présente dans la configuration est **une grille de développement**,
et son propre commentaire le dit. Elle n'a pas été arrêtée commercialement.

| Paramètre | Valeur de développement |
|---|---|
| Part fixe | 0 XOF |
| Prix au km | 200 XOF |
| Prix à la minute | 0 XOF |
| Plancher | 500 XOF |
| Majoration | ×1,00 (inactive) |
| Part livreur | 75 % |

**La commission du prestataire de paiement est connue depuis le 29 septembre
2026** : `4 %` du montant, sans part fixe. Relevée sur une transaction réelle du
bac à sable (`id 514996`, 500 XOF) : `commission: 0.04`, `fixed_commission: 0`,
`fees: 20`, `amount_transferred: 480`. À confirmer sur le compte de production,
où la grille peut différer.

Deux courses, bout en bout :

| | Course de 4 km | Course au plancher |
|---|---|---|
| Total encaissé | 800 | 500 |
| Part du livreur (75 %, arrondie en dessous) | −600 | −370 |
| Frais FedaPay (4 %) | −32 | −20 |
| **Marge nette HBA** | **168** | **110** |
| en part du total | 21 % | 22 % |

La commission du prestataire mange donc **un sixième de la marge brute** sur une
course de 4 km, et un septième sur une course au plancher.

**Conséquence à regarder en face** : avec une part livreur à 75 % et un prix
adossé à la seule distance, la marge est de l'ordre de **cent à deux cents
francs par course**. Ce modèle ne tient que par le volume et par un coût de
service proche de zéro à la course. Il faut environ **six courses pour couvrir
un millier de francs** de frais fixes mensuels.

Les deux leviers qui ne demandent aucun code nouveau sont **le taux de partage**
et **le plancher** ; le troisième, la majoration aux heures de pointe, est câblé
mais non décidé.

---

## 5. Ce que la plateforme coûte

Aucun montant n'est dans le dépôt. Ce qui suit est la liste des postes, établie
depuis ce qui tourne réellement, pour qu'aucun ne soit oublié au moment de
chiffrer.

**À la course** — commission FedaPay, **4 % sans part fixe** ; SMS transactionnels (les modèles sont
délibérément sans accents, GSM-7, pour tenir en 160 caractères au lieu de 70) ;
courriel de reçu (Resend) ; appels de cartographie.

**Au mois** — hébergement de neuf services et de leurs bases ; Kafka ; Redis ;
stockage objet des pièces et photos ; observabilité ; support humain.

**Le poste que l'architecture réduit volontairement** : le devis est calculé en
interne, les positions des livreurs sont servies sans identité, et la carte
n'est interrogée qu'au repos du doigt — *chaque requête coûte au client des
données qu'il paie*. Ce souci du coût de données du client est écrit dans le
code ; il vaut aussi pour la facture de l'entreprise.

---

## 6. Les sources de revenu, et leur état

| Source | Mécanisme | État |
|---|---|---|
| **Commission sur course B2C** | complément de `DriverShareBasisPoints` | **en service** |
| **Écart d'arrondi** | total arrondi au-dessus, part livreur en dessous | en service, marginal |
| **Majoration horaire** | `SurgeBasisPoints` par grille | câblé, **non décidé** |
| **Courses B2B (commerçants)** | compte prépayé ou postpayé (ADR 0013) | **conçu, non implémenté** |
| **Courses B2B (partenaires)** | idem | **conçu, non implémenté** |
| **Frais d'annulation** | politique d'annulation | **non décidée** (point 3) |

Le service `Billing` est conçu en détail — un compte, un mode de règlement, un
plafond de crédit, et une règle de débit unique : `solde + plafond ≥ montant`.
Rien n'en est écrit. C'est le chantier qui ouvre les deux revenus B2B.

---

## 7. Ce qui bloque le modèle aujourd'hui

Par ordre de gravité économique.

1. **Aucun revenu B2B n'est encaissable.** Les points 1 et 2 ne sont pas
   tranchés, et le code refuse plutôt que de deviner. Or HBA Express et HBA Food
   sont les deux premiers donneurs d'ordre du produit : tant que ce point tient,
   la plateforme ne peut servir que des particuliers.
2. **Le paiement mobile money ne fonctionne pas encore de bout en bout.** Sans
   lui, il n'y a aucun revenu du tout.
3. **La politique d'annulation n'existe pas.** Une course annulée après paiement
   laisse aujourd'hui un encaissement sans règle, et l'écran promet au client
   que « les conditions de remboursement seront confirmées par le service » —
   une promesse que personne ne tient.
4. **Le code de remise n'est envoyé à personne.** Le modèle `delivery_otp`
   existe et n'a aucun producteur. L'ADR 0005 en fait la preuve de livraison :
   sans lui, rien ne distingue « le colis a été remis » de « quelqu'un a
   cliqué » — et c'est cette preuve qui rend la commission défendable.
5. **La majoration est inactive.** Le seul levier de prix dynamique du produit
   n'a pas de règle.

---

## 8. Ce qu'il faudrait mesurer pour que ce document cesse d'être une hypothèse

Le dépôt sait déjà compter : les lectures agrégées existent par service, le
total facturé à un client est calculé en base, et les registres de gains sont
tenus par livreur. Ce qui manque n'est pas l'outillage, c'est le chiffre.

- ~~La commission réelle de FedaPay~~ — **relevée : 4 %, sans part fixe.** Reste
  à confirmer sur le compte de production.
- La distance médiane d'une course à Cotonou — elle décide si le plancher est
  une exception ou la règle.
- Le taux de courses sans livreur trouvé : une course non servie ne rapporte
  rien et coûte un client.
- Le coût mensuel d'infrastructure divisé par le nombre de courses livrées.
- Le délai entre la livraison et le reversement, qui mesure la trésorerie
  portée.

Tant que les deux premiers manquent, la marge unitaire de la section 4 reste
une formule, pas un résultat.
