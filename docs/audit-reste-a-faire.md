# Audit — ce qui reste avant une première version de production

État au 25 septembre 2026. Établi en lisant le dépôt, pas en se fiant à
l'impression d'avancement.

---

## Le constat qui commande tout le reste

Le dépôt **présente huit services**. Il en contient **quatre et demi**.

| Service | Domaine | Application | Infrastructure | gRPC | Migrations |
|---|---|---|---|---|---|
| Identity | 11 | 15 | 17 | 320 lignes | 3 |
| Delivery | 13 | 23 | 12 | 216 lignes | 1 |
| Directory | 10 | 9 | 7 | 236 lignes | 1 |
| Notification | 6 | 5 | 10 | — | 1 |
| **Pricing** | **9** | **1** | **1** | **11 lignes** | **0** |
| **Dispatch** | **1** | **1** | **1** | **11 lignes** | **0** |
| **Driver** | **1** | **1** | **1** | **11 lignes** | **0** |
| **Payment** | **1** | **1** | **1** | **11 lignes** | **0** |

(nombre de fichiers `.cs`)

Les quatre services du bas ne contiennent qu'un `Readme.cs` et deux fichiers
d'enregistrement de dépendances vides. Leur `*GrpcService.cs` fait onze lignes :
c'est une coquille. Ils ont pourtant chacun leur atelier CI, leur Dockerfile,
leur compose et leur `.env` — tout l'appareillage d'un service réel. **C'est ce
qui rend l'avancement trompeur.**

Pricing est le cas le plus près du but : le domaine est écrit — zones, grilles,
calcul de devis — mais rien ne l'expose. Neuf fichiers de règles que personne ne
peut appeler.

### La conséquence directe

`CreateDeliveryHandler` prend `IPricingClient` et `IPaymentClient` dans son
constructeur. Ces deux clients appellent des services vides.

**Aucune livraison ne peut être créée aujourd'hui.** Le parcours minimal —
devis, paiement, recherche de livreur, remise — a quatre maillons sur cinq
manquants. Ce n'est pas une finition : c'est le cœur du produit.

---

## Ce qui fonctionne réellement

À ne pas sous-estimer, c'est la partie difficile à reconstruire :

- **La chaîne Outbox → Kafka → Inbox** tourne. Delivery, Directory et
  Notification consomment des topics.
- **Identity** est complet : OTP, jetons RS256, rôles, comptes partenaires,
  sessions. 17 méthodes gRPC.
- **Directory** porte clients, commerçants et points de collecte. 13 méthodes.
- **Delivery** porte le cycle de vie d'une livraison, ses transitions et ses
  vues. 23 fichiers d'application.
- **Notification** : catalogue de modèles, chaîne WhatsApp puis SMS, traces
  d'envoi.
- **Quatre passerelles** avec 11 fichiers d'endpoints.
- **Deux applications Flutter** (client 15 fichiers, livreur 13) sur un socle
  partagé.
- **Onze ateliers CI**, douze ADR acceptées.

---

## Ce qui reste, par ordre de ce qui bloque le plus

### 1. Les quatre services manquants

Sans eux, rien ne se commande. Par ordre de dépendance :

| | Ce qu'il faut écrire | Débloque |
|---|---|---|
| **Pricing** | application + infrastructure + migration ; le domaine existe | le devis, donc l'écran de commande |
| **Payment** | domaine complet + MTN MoMo + webhooks | la confirmation de paiement |
| **Dispatch** | domaine complet + offres + verrou Redis | la recherche de livreur |
| **Driver** | domaine complet + KYC + position Redis GEO | l'app livreur |

Pricing est le plus rapide et débloque l'écran le plus visible. Payment porte le
plus de risque externe (agrément, webhooks, réconciliation).

### 2. Les décisions non prises

**Treize des quatorze points de `points-a-trancher.md` sont ouverts.** Trois
bloquent directement une mise en production :

- **Point 10 — opérateur SMS.** Le code de remise part obligatoirement par SMS :
  le destinataire n'a pas de compte, donc pas d'opt-in WhatsApp possible. Or ce
  code **est** la preuve de livraison (ADR 0005). Sans opérateur, aucune
  livraison ne peut être clôturée. C'est le point le plus bloquant du lot, et il
  demande une démarche commerciale, pas du code.
- **Point 12 — encaisseur de la recharge**, et **point 2 — paiement des
  livraisons partenaires.** Sans réponse, le modèle de règlement reste théorique.
- **Point 14 — moteur de stockage objet.** Bloque les documents KYC du livreur,
  donc l'activation d'un livreur.

Et **les ADR 0013 (facturation) et 0014 (canaux des codes) sont toujours au
statut « proposée »**, alors que du code a déjà été écrit sur leur base.

### 3. Les défauts relevés et non corrigés

| Sujet | Effet en production | Coût |
|---|---|---|
| Clés DataProtection éphémères | tous les utilisateurs déconnectés à chaque redéploiement | faible |
| `OpenTelemetry` 1.10.0 — CVE-2026-42191 | traces lisibles et falsifiables sur l'hôte | faible |
| Journalisation de l'Outbox | des milliers de piles d'appels identiques pendant un incident | moyen |
| 8 images en `:latest` | déploiement d'une version que personne n'a validée | faible |
| Anciens secrets dans l'historique git | à ne jamais réutiliser en production | — |
| Débogage gRPC | la réflexion rend `grpcurl` utilisable, 2 lignes par service | très faible |

### 4. Le chantier en cours

Le consentement WhatsApp : agrégat, événement, mapping, migration et routage
faits. **Reste** la captation du consentement à l'inscription, les tests, et
l'envoi réel vers un numéro.

---

## Ce que je ferais dans l'ordre

1. **Lancer la démarche opérateur SMS** aujourd'hui. Délai externe, donc le plus
   tôt possible ; tout le reste peut avancer en parallèle.
2. **Finir Pricing.** Le domaine est là, c'est le moins cher, et ça débloque
   l'écran de commande.
3. **Valider ou rejeter les ADR 0013 et 0014**, sur lesquelles du code repose
   déjà.
4. **Payment**, avec les webhooks et la réconciliation.
5. **Dispatch**, puis **Driver**.
6. Les défauts du tableau ci-dessus, groupés en une passe.

---

## Ce que cet audit ne dit pas

Je n'ai pas exécuté les tests, ni mesuré la couverture, ni relu la logique
métier de Delivery et Identity ligne à ligne. Cet audit dit **ce qui existe**,
pas **si ce qui existe est juste**. Une revue du parcours de livraison —
transitions d'état, idempotence, autorisation — est un travail distinct, à faire
avant la mise en service.
