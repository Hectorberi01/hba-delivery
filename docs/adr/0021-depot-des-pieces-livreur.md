# 0021 — Garage pour les binaires, et la passerelle porte les octets

Statut : **acceptée** — septembre 2026, amendée le 27 septembre 2026
Tranche le [point 14](../architecture/points-a-trancher.md) de
points-a-trancher.md.
Porte une exception explicite à l'[ADR 0002](0002-grpc-et-kafka.md) : voir
« Les octets traversent la passerelle ».

## Contexte

Le référentiel acteurs liste, parmi les données du livreur, ses documents KYC —
CNI, permis, carte grise, photo — « dans MinIO ». Aucun code ne les recevait :
le dépôt n'appelait aucun stockage objet, et le point 14 attendait la première
fonctionnalité qui écrive des binaires pour choisir un moteur. L'inscription du
livreur dans l'application est cette fonctionnalité.

Trois questions se posaient ensemble : quel moteur, par où passent les octets,
et quelles règles encadrent un dossier.

## Décision

### Le moteur : Garage

Option A du point 14. Un binaire Rust conçu pour l'auto-hébergement sur
matériel modeste et liaisons lentes — le profil d'un VPS à Cotonou. Il demande
un `garage.toml` et une initialisation en deux temps (`layout assign` puis
`layout apply`) avant de servir : ce n'est pas un conteneur qu'on lance et qui
marche.

Les pièces d'identité de livreurs béninois restent ainsi dans le périmètre
auto-hébergé. C'est ce qui écarte l'option C, autrement plus confortable : un
stockage managé transporte des CNI hors UEMOA selon le fournisseur, et ce n'est
pas une décision d'infrastructure mais une décision sur des données
personnelles.

### L'accès passe par un port, jamais par le SDK en direct

`IObjectStore` dans la couche Application de Driver, son implémentation S3 dans
Infrastructure. Le point 14 le disait déjà : Garage, SeaweedFS et un stockage
managé parlent tous S3, donc le choix reste réversible **tant que rien
n'importe le SDK ailleurs que dans l'adaptateur**.

### Les octets traversent la passerelle, qui les relaie au service

`POST /api/driver/v1/documents` en multipart. La passerelle **ne touche pas au
stockage** : elle relaie le corps à une route HTTP du service Driver, et c'est
Driver qui écrit l'objet, enregistre la pièce, et supprime l'objet si
l'enregistrement échoue.

> **Amendement, septembre 2026.** La première rédaction disait « la passerelle
> réécrit vers le stockage et le service enregistre la pièce dans la même
> transaction ». Les deux moitiés de cette phrase ne peuvent pas être vraies
> ensemble : si la passerelle écrit l'objet, cette écriture est hors de la
> transaction du service. Elle aurait de plus donné à la passerelle les
> identifiants du stockage et la convention de clés — dupliqués dans deux
> services — et personne pour nettoyer une CNI orpheline après un
> enregistrement refusé.

**UNE EXCEPTION ASSUMÉE À L'ADR 0002**, qui réserve gRPC aux appels
synchrones internes, et que l'ADR 0015 vient de confirmer en étant rejetée.
Cet appel-là passe en HTTP, sur `/internal/v1/`, et c'est le seul. La raison
est que le transport binaire est précisément ce que gRPC fait mal ici : un
champ `bytes` ou un flux traverse les intercepteurs de trace, où une pièce
d'identité n'a rien à faire. Le typage de bout en bout que l'ADR 0015 défend
protège des champs renommés ; il ne protège rien sur un flux d'octets.

Si une seconde route interne en HTTP se présente un jour, ce n'est plus une
exception mais une tendance, et la question de l'ADR 0015 devra être rouverte.

L'URL présignée aurait épargné un saut, mais elle exige d'exposer le stockage
sur Internet avec TLS et CORS, et surtout elle sépare l'écriture du fichier de
son enregistrement : le service n'apprend l'existence d'un objet que si
l'application pense à le lui dire. Un téléphone qui perd le réseau entre les
deux laisse un binaire orphelin que personne ne réclame — et une pièce
d'identité orpheline n'est pas un déchet neutre.

CE CHOIX A UN COUT ASSUME : la passerelle porte le trafic. Il est supportable
parce que les envois sont rares — quatre pièces par livreur, une fois — et
parce que l'application redimensionne avant d'envoyer (voir ci-dessous).

### L'image est réduite sur le téléphone, pas sur le serveur

Une CNI photographiée fait trois à huit mégaoctets. Le livreur paie ses
données, souvent à la recharge. L'application réduit à 1600 pixels sur le grand
côté et recompresse en JPEG avant l'envoi ; le serveur refuse au-delà de
5 Mo. Une pièce d'identité reste lisible bien en deçà.

### Trois règles de dossier, absentes du référentiel

**Le dossier se soumet complet.** `SubmitForReview` refuse tant qu'une pièce
requise manque ou que le véhicule n'est pas déclaré. Sans cette règle, un
livreur soumet un dossier partiel, ops le rejette, et les deux ont travaillé
pour rien.

**Un rejet n'est pas définitif.** Après `REJECTED`, le livreur corrige et
resoumet ; le dossier repasse en `PENDING_VERIFICATION`. Sans cette règle, la
seule issue serait de recréer un compte — donc un second profil pour la même
personne, avec la même CNI.

**La photo de profil n'est pas une pièce KYC.** Elle se change librement, sans
rouvrir l'examen. Elle sert à ce que le client reconnaisse qui arrive, pas à
prouver une identité — ce que font la CNI et le permis.

## Conséquences

- Le `PackageReference Include="Minio"` de `Hba.Driver.Infrastructure` cesse
  d'être mort : ce SDK parle le S3 standard et sert d'adaptateur à Garage.
- Une pièce n'est jamais servie en clair : la passerelle rend une URL signée à
  courte durée, conformément au tableau de visibilité du référentiel — le
  livreur voit les siennes, l'admin les voit toutes, personne d'autre.
- Le contrat gRPC ne porte plus `AttachDocument` ni `SetProfilePhoto` : ces
  deux appels supposaient que la passerelle écrive l'objet et transmette sa
  clé. Plus personne ne les appellerait.
- `SUSPENDED` ne se traite pas comme `REJECTED`. Un compte suspendu l'est par
  décision d'ops, pas pour un dossier incomplet : resoumettre ne doit pas le
  débloquer. Seul ops lève une suspension.
- La rétention des pièces n'est pas tranchée. Le point 7 couvre les preuves de
  livraison et les positions ; les pièces d'identité posent la même question et
  n'y figurent pas. À traiter avant la mise en production, pas avant le pilote.

## Alternatives écartées

**SeaweedFS** (option B) démarre plus simplement, mais son authentification S3
tient dans un fichier séparé : oublié, l'accès est anonyme. Un stockage de
pièces d'identité ouvert par défaut d'oubli est le mauvais réglage par défaut.

**Fichiers sur volume** (option D). Le moins de pièces mobiles, et la seule
option que le point 14 désigne comme non réversible : tout le reste parle S3,
elle non. Le pilote tiendrait ; la sortie du pilote demanderait une reprise.

## Redeposer une piece modifie sa ligne, n'en cree pas une autre

**Constate le 27 septembre 2026.** Le depot d'une piece remontait en HTTP 500
avec `DbUpdateConcurrencyException: expected to affect 1 row(s), but actually
affected 0 row(s)`.

`AttachDocument` retirait la piece precedente de la collection et ajoutait la
nouvelle. EF Core en tire un DELETE suivi d'un INSERT sur la meme cle
d'unicite `(driver_id, type)`. Le SaveChanges de ce chemin ne contient rien
d'autre : l'agregat lui-meme n'est pas modifie, et `DriverDocumentAttached`
n'est pas publie. Par elimination, la seule instruction qui puisse compter
zero ligne est ce DELETE — et il compte zero des que deux depots de la meme
piece se croisent : le premier valide, le second ne trouve plus la ligne qu'il
venait de lire.

Une piece par nature et par livreur : la ligne existe deja, elle change de
contenu. `DriverDocument.ReplaceWith` fait un UPDATE sur la meme ligne, ce qui
reste vrai que le depot soit le premier ou le troisieme. Le pire cas d'un
croisement devient un objet orphelin dans le stockage — le compromis deja
retenu plus haut dans cet ADR.

**Une course perdue n'est pas une panne.** Le jeton `xmin` existe pour
departager deux ecritures concurrentes ; rien ne rattrapait
`DbUpdateConcurrencyException`, qui ressortait donc en 500 « erreur interne ».
`DriverDbContext` la traduit maintenant en `DomainException`
`CONCURRENT_MODIFICATION`, que l'intercepteur gRPC et le filtre HTTP rendent
en 409.

RESTE OUVERT : les autres services portent le meme jeton `xmin` sans cette
traduction. Identity, Delivery, Dispatch, Pricing, Payment et Notification
renvoient encore un 500 quand une ecriture concurrente perd.

## Le stockage a deux adresses : celle du service, celle du client

**Constate le 27 septembre 2026.** Les vignettes du dossier, dans la console
comme dans l'application, echouaient toutes en `ERR_NAME_NOT_RESOLVED` sur
`http://garage:3900/...`.

Une URL signee n'est pas faite pour celui qui la fabrique : elle est faite
pour etre suivie par quelqu'un d'autre. Le service parle a Garage par son nom
de reseau Docker, et c'est le bon nom pour ecrire ; le navigateur d'ops et le
telephone du livreur ne le resolvent pas.

**ET L'HOTE NE SE REECRIT PAS APRES COUP.** La signature SigV4 couvre
l'en-tete `Host` : remplacer l'hote d'une URL deja signee l'invalide. C'est ce
qui interdit la correction evidente — un `Replace` dans la vue — et impose de
connaitre l'adresse publique AU MOMENT de signer.

`ObjectStore:PublicEndpoint` porte cette adresse, et un second client S3 lui
est dedie. Il n'ouvre jamais de connexion : signer est un calcul local, donc
ce client peut pointer vers une adresse que le service lui-meme ne joindrait
pas. L'ecriture, la lecture serveur et la suppression continuent de passer par
l'adresse interne.

Le seau reste prive : ce qui circule, ce sont des URL signees de cinq minutes,
comme decide plus haut. Ce qui change, c'est que le port S3 de Garage doit
etre joignable depuis le client — en developpement par l'IP de la machine sur
le reseau local (pas `localhost` : la meme URL doit marcher sur l'ordinateur
et sur le telephone), en production par une route dediee.

RESTE A FAIRE AVANT LA PRODUCTION : la route du stockage n'existe pas encore
dans la configuration Traefik, et `deploy/docker-compose.yml` ne publie les
ports de Garage qu'en developpement. Tant que ce n'est pas fait, les vignettes
ne s'afficheront qu'en local.
