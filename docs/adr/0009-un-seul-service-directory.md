# 0009 — Un seul service Directory, pas deux

Statut : acceptée — septembre 2026

## Contexte

Le référentiel des acteurs attribue le profil client et les adresses favorites à
« Customer/Directory », et le commerçant et ses points de collecte à
« Directory ». La barre oblique n'est pas tranchée : un service ou deux ?

Le premier réflexe serait d'en faire deux, parce qu'un particulier et une
entreprise ne se ressemblent pas. Mais ils partagent l'essentiel de ce que ce
service détient : une adresse au sens béninois — point GPS, repère écrit,
téléphone — et rien d'autre de sensible.

## Décision

Un seul service **Directory**, avec deux agrégats : `Customer` et `Merchant`.
Une base, un contrat, un déployable.

## Conséquences

- Une livraison créée depuis un commerce n'a qu'un seul service à interroger
  pour retrouver l'adresse de collecte.
- Le code de l'adresse — la règle « pas de repère, pas d'adresse » — n'est écrit
  qu'une fois dans ce service.
- Sept services au MVP deviennent huit, pas neuf.
- Le risque est connu : si le profil client se met à porter des préférences, un
  historique, des moyens de paiement, l'agrégat `Customer` grossira seul et
  méritera son propre service. La séparation restera possible : les deux
  agrégats ne partagent aucune table et aucun invariant.
- Le nom `Directory` entre en collision avec `System.IO.Directory` dans le
  code C#. Aucun fichier du service ne manipule le système de fichiers, et les
  espaces de noms sont explicites ; le désagrément est réel mais localisé.
