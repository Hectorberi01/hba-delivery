# Architecture — HBA Delivery

## Ce que fait le système, et ce qu'il ne fait pas

HBA Delivery exécute la **livraison physique**. Il ne gère pas la commande
commerciale : c'est HBA Express, HBA Food ou un partenaire externe qui vend, et
qui demande ensuite une livraison.

Marché : Bénin, pilote à Cotonou, zone UEMOA. Monnaie XOF, montants entiers.
L'adressage formel n'est pas fiable : **une adresse est un point GPS, un repère
écrit et un téléphone**. Cette contrainte se retrouve partout, jusque dans le
type `Location` du domaine, qui refuse une adresse sans repère.

## Le référentiel des acteurs

[`referentiel-acteurs.md`](referentiel-acteurs.md) est la **référence unique**.
Le modèle de domaine, les contrats, les tests et les écrans en découlent. Rien
n'est ajouté qui n'y figure ; ce qui y manque est listé dans
[`points-a-trancher.md`](points-a-trancher.md) plutôt que supposé.

## Les services

| Service | Rôle | État |
|---|---|---|
| Identity | Comptes, OTP, mots de passe, JWT RS256, JWKS, clients OAuth des partenaires | **implémenté** |
| Directory | Profils clients et adresses favorites, commerçants et points de collecte | **implémenté** |
| Delivery | Agrégat livraison, machine à états, preuve de remise | **implémenté** |
| Pricing | Zones PostGIS, grilles, devis. Seule autorité sur le prix | coquille |
| Dispatch | Offres par vagues, verrou Redis, expiration | coquille |
| Driver | Profils, KYC (MinIO), positions (Redis GEO) | coquille |
| Payment | Intentions et remboursements, adaptateur FedaPay | coquille |
| Notification | SMS, WhatsApp, push. Presque uniquement des consommateurs | **implémenté** |

Plus tard : Tracking, Support, Rating, Settlement, Reporting.

**Directory ne figure pas tel quel dans le référentiel des acteurs.** Celui-ci
parle de « Customer/Directory » pour le profil client et de « Directory » pour
le commerçant, sans trancher entre un et deux services. Le choix d'un service
unique est documenté dans l'[ADR 0009](../adr/0009-un-seul-service-directory.md).

## Où vit quoi, à propos d'une personne

C'est la confusion la plus coûteuse à laisser s'installer :

| | Identity | Directory | Driver |
|---|---|---|---|
| Compte, mot de passe, jetons | oui | non | non |
| Rôles et claims | oui | non | non |
| Nom, téléphone | référence | copie de travail | copie de travail |
| Adresses favorites | non | oui | non |
| Commerçant, points de collecte | rattachement seulement | oui | non |
| Documents KYC, véhicule, position | non | non | oui |

Identity publie `AccountRegistered` ; Directory en crée le profil. Un compte et
un profil sont deux choses distinctes, et le destinataire d'un colis n'a ni
l'un ni l'autre — il ne doit jamais s'en voir créer.

## Les échanges

- **Synchrone** : gRPC entre services, avec échéance imposée, corrélation et
  report du jeton de l'utilisateur final.
- **Asynchrone** : Kafka, avec Outbox à l'émission et Inbox à la réception. La
  clé de partition est toujours l'identifiant de l'agrégat, ce qui garantit
  l'ordre des événements d'une même livraison.
- **Vers l'extérieur** : REST/JSON derrière Traefik, via trois BFF et la
  Partner API. Aucun service gRPC n'est exposé publiquement.

Les contrats vivent dans [`contracts/`](../../contracts) et sont la source de
vérité des deux côtés.

## Les trois règles qui ne se négocient pas

1. **L'autorisation se vérifie côté service.** Le BFF valide le jeton, mais ne
   décide rien : il le propage, et le service le revalide et applique la matrice
   de visibilité lui-même.
2. **Le prix est figé à la confirmation.** Un changement de grille tarifaire ne
   touche jamais une course déjà confirmée. D'où le `PricingSnapshot` recopié
   dans la livraison, et non une référence au devis.
3. **Seul le livreur livre.** La transition vers `Delivered` n'existe que pour
   l'acteur `Driver`, et seulement avec un OTP valide. Un administrateur peut
   clore en `Failed` ou `Cancelled`, jamais en `Delivered`. Deux tests
   d'architecture échouent si quelqu'un modifie la table des transitions dans
   ce sens.

## Pour aller plus loin

- [Machine à états d'une livraison](flux-livraison.md)
- [Matrice de visibilité, et où elle est appliquée](matrice-visibilite.md)
- [Points à trancher](points-a-trancher.md)
- [Décisions d'architecture](../adr/)
