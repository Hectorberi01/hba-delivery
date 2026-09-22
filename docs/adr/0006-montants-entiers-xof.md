# 0006 — Montants en entiers de francs CFA

Statut : acceptée — septembre 2026

## Contexte

Le XOF n'a pas de sous-unité. Un `decimal` à deux décimales inviterait des
centimes qui n'existent pas, et des arrondis qui s'écartent des prix affichés.

## Décision

`MoneyXof` encapsule un `long` de francs. En base, les montants sont des
`bigint`, jamais des `numeric`. Dans les contrats protobuf, `Money.amount` est
un `int64` et `currency` vaut « XOF ».

L'arrondi métier se fait à la **dizaine de francs supérieure**, conformément à
l'usage local des prix affichés.

## Conséquences

- Aucun arrondi flottant possible.
- Le champ `currency` existe quand même dans le contrat : le jour où une autre
  devise UEMOA ou CEDEAO apparaît, le contrat n'a pas à casser. Les clients
  gRPC rejettent aujourd'hui toute devise autre que XOF, explicitement.
- Un montant négatif reste représentable — remboursements, ajustements — mais
  `MoneyXof.FromNonNegative` est utilisé là où le domaine l'interdit.
