# 0004 — Le prix est recopié dans la livraison, pas référencé

Statut : acceptée — septembre 2026

## Contexte

Le référentiel est explicite : une modification de tarif ne change jamais une
livraison déjà confirmée. Référencer le devis par son identifiant aurait suffi
tant que les devis sont immuables — mais une grille tarifaire, elle, change, et
un devis recalculé à la lecture recalculerait un prix déjà payé.

## Décision

`PricingSnapshot` est un objet-valeur **recopié** dans l'agrégat Delivery au
moment de la création : total, détail, rémunération du livreur, version de la
grille, distance, durée. Aucune méthode publique ne le remplace.

## Conséquences

- Une livraison de 2024 se relit avec le prix de 2024, même si la grille a
  changé trois fois depuis.
- La version de grille est conservée : on sait toujours quelle règle a produit
  quel montant, ce qui rend les litiges instruisibles.
- C'est de la dénormalisation assumée. Le devis reste chez Pricing, qui le
  marque consommé ; Delivery n'a plus besoin de lui ensuite.
- Un test d'unité vérifie par réflexion que le setter de `Pricing` n'est pas
  public. La règle ne dépend donc pas de la vigilance en revue.
