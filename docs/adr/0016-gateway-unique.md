# 0016 — Un seul gateway à la place des quatre BFF

Statut : acceptée — septembre 2026

## Contexte

L'entrée publique comptait quatre déployables : `Hba.Bff.Client`,
`Hba.Bff.Driver`, `Hba.Bff.Web` et `Hba.PartnerApi`. Leurs `Program.cs`
étaient identiques à trois lignes près — mêmes intergiciels, mêmes réglages,
mêmes clients gRPC, seuls les groupes de routes changeaient. Quatre images à
construire, quatre `.env` à tenir en phase, quatre ports à publier, quatre
noms d'hôte à configurer, et une application mobile qui devait connaître
lequel viser.

Le coût se payait à chaque changement transverse : ajouter un plafond de
limitation, corriger la traduction des erreurs gRPC ou changer l'émetteur des
jetons demandait la même modification quatre fois, avec le risque qu'un des
quatre soit oublié.

## Décision

Un seul déployable, `Hba.Gateway`, qui porte les quatre surfaces. Les fichiers
d'endpoints sont **déplacés**, pas réécrits : la traduction REST vers gRPC est
exactement celle d'avant, sous `Endpoints/{Client,Driver,Web,Partner}`.

Les quatre noms d'hôte publics sont conservés et pointent sur le même
conteneur. Un nom d'hôte se change plus difficilement qu'une route : les
applications installées connaissent `api.hba.delivery`, les partenaires ont
signé sur `partners.hba.delivery`.

YARP est présent, et ne porte que le relais pur — aujourd'hui la découverte
JWKS d'Identity. Un reverse proxy relaie du HTTP vers du HTTP : il ne sait ni
fabriquer un message protobuf à partir d'un corps JSON, ni appeler deux
services et fusionner leurs réponses. Les cinq endpoints qui agrègent
plusieurs services restent donc du code écrit à la main, et le resteront.

## Conséquences

- Une image, un `.env`, un port, une page de documentation à quatre
  définitions. Un changement transverse se fait une fois.
- **Les routes d'administration et les routes client vivent dans le même
  processus.** Un défaut dans l'une peut affecter l'autre, et une mise à jour
  les redémarre ensemble. C'est le prix explicite de cette décision.
- La séparation par acteur n'était pas portée par le découpage en quatre
  déployables : elle l'est par les politiques d'autorisation de chaque groupe
  de routes, et surtout par les services eux-mêmes, qui refont la vérification
  pour leur compte (ADR 0007). Ce point ne change pas.
- Le dimensionnement devient commun : on ne peut plus donner plus de ressources
  à la surface client qu'au back-office. Pour un trafic de pilote, cela ne se
  voit pas ; le jour où cela se verra, une surface peut redevenir un déployable
  à part en déplaçant un label Traefik et quelques appels `Map*`.
- La limitation de débit par adresse, la validation du jeton en amont et la
  documentation agrégée sont portées par le gateway. L'autorisation, elle, n'y
  est jamais décidée.

## Alternatives écartées

- **YARP devant les quatre BFF.** Entrée unique sans rien casser, mais une
  couche de plus et les quatre déployables restaient à maintenir.
- **YARP à la place de Traefik.** Let's Encrypt et la découverte Docker
  auraient été à réimplémenter, pour remplacer ce que Traefik fait déjà.
- **REST sur les sept services, puis YARP sans traduction.** Deux surfaces par
  service à garder en phase, et les endpoints d'agrégation restaient sans
  domicile.
