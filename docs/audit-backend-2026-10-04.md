# Audit du backend, service par service

**4 octobre 2026.** Quinze services, une passerelle, environ 70 000 lignes de C# hors
migrations et code généré. L'audit porte sur l'état du dépôt au 4 octobre, Rating et le
pourboire compris — écrits la veille.

## Comment il a été fait, et ce que cela vaut

Six lectures menées en parallèle, une par lot de services, chacune en **lecture seule**
sur le dépôt : ni compilation, ni exécution, ni base, ni conteneur. Tout ce qui est
affirmé ici vient du code lu, et chaque constat porte `chemin/fichier.cs:ligne` pour
être vérifié en dix secondes.

**Les cinq constats les plus lourds ont été revérifiés un à un**, parce qu'un faux
constat coûte plus cher qu'un constat manquant. Quatre tiennent tels quels ; le
cinquième — « Ride ne peut pas démarrer » — était trop large : le service démarre
parfaitement en conteneur, son `docker-compose.yml` fournit le jeton de service. C'est
depuis l'IDE qu'il refuse. Le constat est corrigé plus bas.

Ce que cet audit **ne** dit pas : rien n'a été mesuré. Aucune affirmation de
performance, aucun chiffre de latence, aucun test exécuté. Là où un constat demande une
exécution pour être tranché, il est rangé sous « à vérifier » plutôt que présenté comme
établi.

## Ce qu'il faut lire en premier

Vingt-neuf constats bloquants au total. Les dix-huit qui suivent sont ceux dont
l'effet est le plus direct, rangés non par service mais par ce qu'ils coûtent ; les
onze autres sont dans leur section.

### De l'argent qui part, ou qui ne revient pas

1. **Un paiement arrivé après l'abandon automatique garde l'argent et tue la course.**
   Delivery abandonne une course impayée au bout de quinze minutes, dans un état
   terminal. Le webhook de la seizième minute ne peut plus rien, le message est
   abandonné, et personne n'ouvre de remboursement. Quinze minutes, c'est la durée de
   vie d'un devis — pas celle d'un paiement mobile money à Cotonou.
   *(Delivery, `ExpirationDesImpayees.cs:392`)*
2. **Le même enchaînement sur une course de personnes**, par une autre porte : le
   passager annule pendant qu'il paie, le webhook confirme dix secondes plus tard,
   l'argent reste et aucune ligne n'entre dans la file de `finance`.
   *(Ride, `PaymentEventsConsumer.cs:70`)*
3. **Un pourboire est publié sur Kafka comme le paiement de la mission.** Deux dégâts :
   chaque pourboire fait échouer trois fois le consommateur de Delivery, et un
   remboursement de pourboire de 200 F marque **la livraison entière** remboursée — le
   client est informé du remboursement d'une course qu'il a bien reçue.
   *(Payment, `PaymentIntegrationEventPublisher.cs:64`)*
4. **Le chauffeur qui annule encaisse la retenue destinée à dédommager son
   déplacement.** `RetenueDue()` ne regarde pas qui annule, et la route existe côté
   livreur. Exploitable en boucle.
   *(Ride, `RideAggregate.cs:152`)*
5. **Un crédit peut n'être jamais appliqué.** La clé de rejeu de Billing n'est pas
   rattachée au titulaire : la même clé réutilisée pour un autre compte rend le mouvement
   du premier et ne crédite rien au second. Qui a payé ne voit pas son solde bouger, et la
   réponse a l'air d'un succès.
   *(Billing, `CreditAccount.cs:48`)*
6. **Le type de véhicule du devis n'est jamais vérifié à la consommation.** On demande un
   devis en moto, on commande une voiture avec : prix ferme de la moto, part du chauffeur
   calculée dessus. C'est la troisième dimension de la clé tarifaire, après la nature
   (corrigée le 1er octobre) et le trajet (corrigé le 30 septembre).
   *(Pricing, `Quote.cs:190`)*

### Des données personnelles qui sortent

7. **Le code OTP est écrit en clair et durablement** dans `identity.outbox_messages`,
   donc dans Postgres et sur Kafka. Aucune purge d'Outbox n'existe. Le domaine affirme
   pourtant « l'empreinte du code est stockée, jamais le code » : c'est vrai dans Redis,
   faux en base.
   *(Identity, `OutboxOtpNotifier.cs:54`)*
8. **Le secret de signature des webhooks partenaires est rendu en clair à `ops`,
   `support` et `finance`**, alors que la simple liste des partenaires est réservée à
   `admin`. La route la plus ouverte est celle qui rend un secret.
   *(Identity, `GetPartnerWebhookConfig.cs:31`)*
9. **La fiche complète d'un client — téléphone entier, courriel, toutes ses adresses
   favorites avec les noms et numéros de tiers — est rendue à tout le back-office,
   `finance` compris**, sans masquage ni journal, par une route qui contourne tout le
   travail de celle d'à côté.
   *(Directory, `DirectoryAccess.cs:50`)*
10. **Un ré-abonnement après la clôture ré-arme le suivi de position, sans fin.** Le hub
    ne demande jamais si la mission est encore ouverte ; une simple reconnexion réseau
    sur un écran resté ouvert réinscrit la mission, et plus aucun événement ne viendra
    l'éteindre. Le client suit alors son livreur chez le client suivant, puis chez lui.
    C'est la dérive que le point 37 interdit nommément.
    *(Realtime, `CustomerHub.cs:109`)*
11. **La clé d'API du service de courriel est transmise à l'opérateur SMS.** Les trois
    adaptateurs de Notification s'enregistrent sous le même nom de client HTTP : leurs
    trois configurations s'empilent sur un seul objet, et le dernier gagne. Chaque appel
    SMS part donc avec l'en-tête d'autorisation de Resend vers `eu.api.ovh.com`.
    *(Notification, `NotificationInfrastructureExtensions.cs:274`)*
12. **`OpenReview` n'a aucun contrôle d'appelant** alors que le contrat écrit « RÉSERVÉ
    AUX SERVICES, jamais exposé à une application ». N'importe quel jeton valide fabrique
    une notation sur une mission inventée, puis la note cinq étoiles. **Code écrit la
    veille, et c'est le mien.**
    *(Rating, `RatingGrpcService.cs:100`)*
13. **`CreateTipIntent` ne vérifie aucun appelant non plus** : ni rôle, ni lien avec la
    mission. Ses deux voisins immédiats le font. **Même origine, même journée.**
    *(Payment, `CreateTipIntent.cs:56`)*

### Un utilisateur coincé, ou une course perdue

14. **Deux recherches du même balayage offrent au même livreur, et il peut accepter les
    deux.** La réservation du livreur passe par Kafka, le planificateur traite cinquante
    recherches en quelques millisecondes : les trois barrières annoncées sont toutes
    **par mission**, aucune n'est par livreur. Deux colis partent avec un seul porteur.
    *(Dispatch, `AdvanceDispatch.cs:127`)*
15. **Le plafond de la recherche de proximité est appliqué avant le filtre de
    fraîcheur**, sur un index Redis que rien ne purge. Quinze points morts plus proches
    que les livreurs en service suffisent à rendre une carte vide, et cinquante à faire
    échouer une vague — les trois anneaux étant concentriques, élargir reprend les mêmes
    morts. C'est le « aucun livreur dans ce secteur » du 3 octobre, revenu par une autre
    porte.
    *(Location, `RedisPositionStore.cs:256`)*
16. **Un livreur reste coincé en mission et ops ne peut pas l'en sortir.** `GoOffline`
    refuse depuis `ON_MISSION`, même pour `admin` — alors que le commentaire du contrôle
    d'accès promet l'inverse. La seule réparation est de le suspendre, ce qui écrit un
    motif mensonger dans son dossier et le fait retomber dans un second défaut.
    *(Driver, `DriverAggregate.cs:692`)*
17. **Une course dont le paiement n'a pas pu s'ouvrir reste « demandée » pour toujours** :
    le balayage des impayées exclut précisément les courses sans intention de paiement,
    c'est-à-dire exactement celles qu'il devrait fermer.
    *(Ride, `RideRepository.cs:84`)*
18. **Un `AccountErased` qui échoue trois fois est acquitté et perdu.** Il n'est pas
    reproductible : la ligne d'Identity a disparu dans la même transaction que sa
    publication. Le profil complet du client reste dans Directory pour toujours, et aucun
    écran ne le signale.
    *(Directory, `KafkaConsumerBase.cs:117`)*

---

## Six motifs qui traversent tout le backend

Ce que la lecture service par service ne montre pas : les mêmes erreurs reviennent aux
mêmes endroits. Les corriger une fois, au bon niveau, vaut mieux que quinze correctifs.

### 1. Un consommateur à bout de tentatives acquitte quand même

`src/BuildingBlocks/Hba.BuildingBlocks.Messaging/Kafka/KafkaConsumerBase.cs:117` : après
`MaxDeliveryAttempts`, la boucle journalise puis **valide l'offset**. Le message ne
revient jamais, la ligne d'Inbox reste à `ProcessedAt = null`, et rien ne la rejoue.

C'est le mécanisme commun derrière **quatre** des constats bloquants ci-dessus (3, 14 pour
partie, 16, 18) et derrière plusieurs sérieux. Trois tentatives couvrent environ sept
secondes : un service redémarré pendant ce laps perd l'événement définitivement.

Il manque une file de rebut, et une décision sur ce qu'on en fait. C'est le même trou que
celui repéré hier côté production : `OutboxOptions.MaxAttempts = 10` est **déclaré et
jamais lu**, donc un message qui ne peut pas partir est réessayé indéfiniment toutes les
512 secondes. Les deux bouts de la chaîne ont le même défaut de bout de course.

### 2. « L'autorisation se vérifie côté service » est enfreinte de la même façon partout

La consigne 3 du référentiel est explicite, et la matrice de visibilité la répète : « Les
BFF ne filtrent rien. […] celle du BFF serait contournable en appelant le service
directement depuis le réseau interne. » Sept endroits font pourtant exactement cela :

| Service | Ce que le service rend à qui ne devrait pas | Qui filtre aujourd'hui |
|---|---|---|
| Ride | `RideView` rend tout à tout le monde : prix payé au chauffeur, téléphone et plaque au passager sans borne de temps | la passerelle |
| Rating | `driver_id` et `customer_id` dans chaque avis | `ClientRatingEndpoints.Lisible` |
| Driver | nom, téléphone, plaque par `GetDriverPublicProfile`, sans rôle ni borne | une route |
| Location | identifiant, cap, vitesse, précision à un jeton client | `ClientNearbyEndpoints` |
| Delivery | `driver_id` dans `AssignedDriverView` | deux projections de la passerelle |
| Payment | `provider_reference`, que le domaine dit « jamais hors du service » | personne |
| Delivery | nom et plaque du livreur **après** la clôture | personne |

Le cas de Delivery est le plus démonstratif : le hub temps réel appelle `GetDelivery`
**avec le jeton du client** et lit `Driver.DriverId`. La règle ne tient donc déjà plus,
sans qu'aucun attaquant soit nécessaire.

### 3. `Roles.Overlaps(HbaRoles.BackOffice)` comme garde unique

Le référentiel distingue quatre rôles internes et leur donne quatre périmètres. Le code
les confond dans une seule garde à au moins huit endroits, et le résultat est toujours le
même : **`finance` obtient des données personnelles ou des gestes qui engagent de
l'argent.**

- Identity : le secret de webhook d'un partenaire, le téléphone et le courriel de
  n'importe quel compte.
- Directory : la fiche complète d'un client ; la modification d'un commerçant et de ses
  points de collecte.
- Delivery : l'historique de courses d'un client nommé, prix compris ; la clôture forcée,
  qui publie une retenue.
- La passerelle : le dossier KYC d'un livreur, URL signées des pièces comprises.

Partout ailleurs le dépôt fait la découpe et écrit pourquoi — « admin SEULEMENT, comme la
création et la rotation », « instruire un versement : finance et admin ». Ces huit
endroits-là ne l'ont pas faite.

### 4. Une enveloppe illisible fabrique un identifiant neuf, donc désarme l'Inbox

Même ligne dans cinq consommateurs : `Guid.TryParse(...) ? id : Guid.CreateVersion7()`.
Un message dont l'enveloppe est mal formée reçoit un identifiant **différent à chaque
livraison** : il n'est plus dédoublonné du tout. Pour Notification, cela veut dire
plusieurs SMS facturés pour un seul message ; pour Delivery, un traitement répété.

Rien ne l'exploite aujourd'hui, tous les producteurs du dépôt remplissant l'enveloppe.
C'est une garantie qui s'éteindra sans bruit le jour où l'un d'eux ne le fera plus. Un
identifiant illisible doit être un rejet, pas un identifiant neuf.

### 5. Des portes de sortie écrites et jamais branchées

`PartnerClient.Disable`, `Merchant.Deactivate`, `BillingAccount.Suspend`, et
`Delivery.Unassign` : quatre méthodes de domaine complètes, testées pour certaines, et
**sans aucun appelant**. Aucune rpc, aucune commande, aucune route.

Deux conséquences concrètes. Le secret d'un partenaire fuite : la seule réponse est de
lui en donner un nouveau, on ne peut pas lui couper l'accès. Une boutique ferme : le
message d'erreur dit « désactivez le commerçant plutôt que son dernier point de
collecte », geste qu'aucune route ne permet — et Dispatch continue d'y envoyer des
livreurs.

### 6. Les tests couvrent le domaine, jamais l'autorisation

Un seul service du backend éprouve réellement ses règles d'accès : **Media**, avec
trente-deux cas. Partout ailleurs, les tests portent sur le calcul et les transitions.

Sans projet de tests du tout : Dispatch, Location, Routing, Rating, Realtime, et **la
passerelle** — qui porte une centaine de routes et sept politiques.

Ce n'est pas une remarque de forme : **la quasi-totalité des constats des sections 2 et 3
ci-dessus est exactement ce qu'une suite d'autorisation attrape au premier essai.**

---

# Service par service

Dans chaque section, les constats vont du plus grave au moins grave. Les dimensions
saines sont dites en une ligne : elles comptent autant que les défauts, et ne pas les
écrire ferait croire qu'elles n'ont pas été regardées.

---

## Identity

Émet les jetons (OTP par téléphone, mot de passe, rafraîchissement à usage unique),
détient les comptes, les rôles, le consentement WhatsApp, les clients OAuth partenaires et
les jetons de service.

- **[BLOQUANT] Le code OTP est écrit en clair, durablement, en base** — `Hba.Identity.Infrastructure/Services/Messaging/OutboxOtpNotifier.cs:54`
  `Variables.Add("code", code)` met le code à six chiffres dans la charge protobuf de
  l'Outbox, donc dans `identity.outbox_messages.Payload`, puis sur Kafka. Aucune purge
  d'Outbox n'existe dans le dépôt. Une lecture de cette table six mois plus tard rend tous
  les codes de connexion émis depuis la mise en production. Le domaine affirme pourtant
  « l'empreinte du code est stockée, jamais le code » (`Otp/OtpChallenge.cs:16`) : vrai
  dans Redis, faux dans Postgres.

- **[BLOQUANT] Le secret de signature HMAC d'un partenaire est rendu à tout le back-office** — `Hba.Identity.Application/Features/Partners/Queries/GetPartnerWebhookConfig.cs:31`
  La garde est `Roles.Overlaps(HbaRoles.BackOffice)`, là où `ListPartnerClients.cs:37`
  exige `admin` avec ce raisonnement écrit : « admin SEULEMENT, comme la création et la
  rotation ». Un agent `finance`, à qui la liste des partenaires est refusée, obtient le
  secret qui authentifie tous les webhooks HBA vers ce partenaire — de quoi forger
  `delivery.delivered`.

- **[BLOQUANT] Six appuis sur « renvoyer » enferment le titulaire une heure dehors** — `Hba.Identity.Infrastructure/Services/Otp/RedisOtpStore.cs:116`
  Le compteur horaire est incrémenté **avant** le contrôle de délai entre deux envois. Cinq
  demandes refusées par le délai — aucun SMS envoyé — suffisent à atteindre le plafond. Le
  titulaire est verrouillé une heure pour un seul SMS reçu, et son numéro est son seul
  identifiant. Sous-cas : `StringIncrementAsync` puis `KeyExpireAsync` ne sont pas
  atomiques ; un arrêt entre les deux laisse la clé sans TTL, et le numéro verrouillé
  **définitivement**.

- **[BLOQUANT] Un compte en sursis de suppression, suspendu, n'a plus aucune issue** — `Hba.Identity.Domain/Accounts/Account.cs:369`
  `Suspend` ne regarde pas `PendingDeletion` et laisse les deux dates d'effacement. Dès
  lors la purge ne le voit plus, `RequestDeletion` refuse, `CancelDeletion` sort en
  silence, et l'écran annonce au titulaire qu'il n'a rien demandé. Symétriquement,
  `Reactivate` (`:384`) laisse une échéance d'effacement orpheline.

- **[SÉRIEUX] Le retrait du consentement WhatsApp ne laisse aucune trace** — `Hba.Identity.Application/Common/IntegrationEvents/IdentityIntegrationEventPublisher.cs:134`
  Le fait de domaine est levé, le publieur le jette dans son `_ => null`, et le message
  n'existe pas au contrat. Le proto écrit pourtant « l'historique vit dans l'événement
  `AccountWhatsAppConsentChanged` ». Plus rien ne prouve qu'un consentement a existé ni
  quand il a cessé — c'est précisément la preuve que Meta demande.

- **[SÉRIEUX] Le téléphone et le courriel de n'importe quel compte, sans masquage ni journal** — `Hba.Identity.Application/Features/Accounts/Queries/GetPrincipal.cs:25`
  Garde `BackOffice`, donc `finance` compris, alors que la décision du 27 septembre
  l'exclut de toute donnée personnelle du client et impose un masquage hors fiche.
  Atténuation : la passerelle n'appelle cette requête qu'avec un corps vide — le trou
  n'est atteignable que depuis le réseau interne.

- **[SÉRIEUX] Aucun moyen de désactiver un client partenaire** — `Hba.Identity.Domain/Partners/PartnerClient.cs:173`
  `Disable()` n'a aucun appelant ; `enabled` est pourtant au contrat et dans la console.
  Secret fuité : la seule réponse possible est de lui en donner un nouveau.

- **[SÉRIEUX] « Réessayez dans 45 secondes » arrive dans les applications comme un refus définitif** — `Hba.Identity.Api/Middleware/DomainExceptionMiddleware.cs:92`
  Toute `DomainException` non typée sort en 409. Or `apps/hba_core/lib/src/api_exception.dart:53`
  range 408 et 429 en « pas maintenant » et **tout autre 4xx en refus**. `ResourceExhausted`
  → 429 existe dans la table de traduction et n'est jamais émis. Même chose pour
  `ACCOUNT_LOCKED` et `INVALID_PHONE`, qui sortent en 409 là où 429 et 400 sont les causes.

- **[SÉRIEUX] `retry_after_seconds` rend la durée de vie du défi, pas le délai de renvoi** — `.../Commands/RequestOtpHandler.cs:92`
  300 au lieu de 60. L'écran fait patienter cinq minutes un client qui pouvait redemander
  au bout d'une.

- **[SÉRIEUX] Dérive de contrat sur le rôle `service`** — `contracts/proto/hba/identity/v1/identity_service.proto:122`
  Le proto affirme « le jeton émis ne porte AUCUN rôle du référentiel » ; l'implémentation
  pose `Roles = [service]`. La consigne 1 impose les dix noms « dans le code, les claims
  JWT **et la documentation** » — le proto est la documentation qui fait contrat.

- **[SÉRIEUX] `ListBackOfficeAccounts` balaie toute la table des comptes** — `.../Repositories/IdentityRepositories.cs:117`
  Quatre `LIKE '%,role,%'` : joker en tête, aucun index utilisable. La page « Équipe »
  scanne l'intégralité d'`accounts`, clients et livreurs compris.

- **[MINEUR]** `Suspend` et `Reactivate` écrivent un statut d'origine en dur dans l'audit
  (`Account.cs:381`) — suspendre un compte en sursis publie « Active → Suspended », faux.
  · Le code OTP est consommé dans Redis **avant** l'enregistrement de la session
  (`VerifyOtpHandler.cs:54` contre `:102`) : une écriture qui échoue perd le code.
  · `IIdempotencyStore` enregistré et jamais lu (`DependencyInjection.cs:49`).
  · Un commentaire promet un test qui n'existe pas (`Roles.cs:6`) : les deux listes de
  rôles sont recopiées à la main dans deux assemblages.
  · Secrets de développement dans `appsettings.Development.json:17` et
  `http-client.env.json:5`, dont le mot de passe d'amorçage de l'administrateur.

**Sain** — Outbox dans la transaction métier ; `xmin` sur les comptes, les jetons et les
clients partenaires ; la rotation à usage unique révoque la chaîne sur rejeu ; le rôle
`service` est refusé par `SetRoles` et par les trois fabriques ; la réponse de `RequestOtp`
n'énumère pas les numéros.

**Non vérifié** — Aucun test de la couche Application : aucune des règles d'autorisation
ci-dessus n'est éprouvée.

---

## Directory

Détient les profils clients et les commerçants avec leurs points de collecte ; crée les
fiches sur `AccountRegistered`, les efface sur `AccountErased`.

- **[BLOQUANT] La fiche complète d'un client est ouverte à tout le back-office, `finance` compris** — `Hba.Directory.Application/Authorization/DirectoryAccess.cs:50`
  `ResolveCustomerId` accepte n'importe quel identifiant demandé dès que l'appelant est au
  back-office, et `GetCustomer` rend nom, **téléphone complet**, courriel et **toutes les
  adresses favorites** — avec les noms et téléphones de tiers qu'elles portent. Tout le
  travail de la requête d'à côté — exclusion de `finance`, masquage pour `ops`, écriture
  dans `personal_data_reads` — est contourné par cette seule route.

- **[BLOQUANT] Un `AccountErased` qui échoue trois fois est perdu sans retour** — `src/BuildingBlocks/.../KafkaConsumerBase.cs:117`
  L'offset est validé, la ligne d'Inbox reste inachevée, et l'événement n'est pas
  reproductible : la ligne d'Identity a disparu dans la même transaction que sa
  publication. Nom, téléphone, courriel, adresses de domicile et de tiers restent dans
  Directory **pour toujours**, sans qu'aucun écran ne le signale. `AccountRegistered` a un
  rattrapage ; celui-ci n'en a pas.

- **[BLOQUANT] L'effacement des médias est avalé, et la fiche part quand même** — `Hba.Directory.Infrastructure/MediaAccess/MediaCatalogue.cs:127`
  L'appel à Media est enveloppé dans un `catch` qui journalise, puis la fiche est
  supprimée. Media indisponible trente secondes : la photo de profil survit et plus rien
  dans Directory ne sait qu'il reste un fichier à détruire — l'état exact que le
  commentaire de `EraseCustomer.cs:60` dit vouloir éviter. L'écran promet au client que
  « passé cette date, tout disparaît sans retour possible ».

- **[BLOQUANT] Aucun moyen de désactiver un commerçant, et le message d'erreur renvoie à cette porte inexistante** — `Hba.Directory.Domain/Merchants/Merchant.cs:208`
  `Deactivate` n'a aucun appelant. Fermer le dernier point de collecte est refusé avec
  « désactivez le commerçant plutôt que son dernier point » — ce qu'aucune route ne
  permet. Le point reste actif, Dispatch continue d'y envoyer des livreurs, et le filtre
  `only_active` porte sur un booléen qui ne peut jamais valoir faux.

- **[BLOQUANT] Le topic `hba.directory.events.v1` n'a aucun consommateur** — `.../DirectoryIntegrationEventPublisher.cs:121`
  Le contrat écrit l'intention : « un point de collecte qui bouge concerne Dispatch : un
  livreur ne doit pas être envoyé à une adresse qui n'est plus la bonne ». Un restaurant
  déménage, l'adresse est corrigée, l'événement part, personne ne l'écoute, et chaque
  trajet inutile est payé.

- **[SÉRIEUX] `finance` et `support` peuvent déplacer un point de collecte** — `.../DirectoryAccess.cs:96`
  `EnsureCanManageMerchant` accepte tout le back-office, alors que la **création** d'un
  commerçant est réservée à `admin` et `ops`, avec le raisonnement écrit.

- **[SÉRIEUX] L'adresse et le contact d'un point de collecte sont rendus à tout appelant authentifié** — `.../Merchants/MerchantHandlers.cs:244`
  Un `customer`, un `driver`, un `partner` ou un jeton de service obtiennent le GPS, le
  repère, le **nom et le téléphone du contact**, sans aucune vérification de lien avec une
  course. Le garde-fou réel est la difficulté de deviner un GUID.

- **[SÉRIEUX] La lecture de la photo d'un client par un tiers n'est pas consignée** — `.../Customers/CustomerPhotoHandlers.cs:186`
  Même porte large que `GetCustomer`, et la lecture n'entre pas dans `personal_data_reads`.
  La fiche d'accès montre qui a ouvert la fiche, pas qui a obtenu une URL signée du
  portrait — alors que ce journal est présenté comme le dissuasif.

- **[SÉRIEUX] Aucune idempotence : un ajout d'adresse rejoué finit par bloquer le client** — `.../DirectoryInfrastructureExtensions.cs:85`
  La file hors ligne de l'application rejoue ; deux adresses identiques sont créées ;
  répété, le client atteint le plafond de quinze et reçoit `TOO_MANY_ADDRESSES` sur une
  liste qu'il n'a pas remplie.

- **[SÉRIEUX] `ListCustomers` trie et compte toute la table à chaque appel** — `.../Repositories.cs:32`
  `ORDER BY display_name` sans index sur cette colonne, plus un `COUNT(*)` complet ; avec
  une recherche, deux `ILIKE '%…%'` à chaque frappe. Et `SearchAsync` charge les agrégats
  entiers — jusqu'à quinze adresses par client — pour n'en rendre que quatre champs
  masqués.

- **[MINEUR]** Les adresses favorites ne lèvent aucun événement de domaine
  (`Customer.cs:172`) : ajouter ou retirer une adresse ne laisse aucune trace, là où la
  photo et le profil en ont une. · Le courriel est accepté sans validation (`:164`), alors
  qu'Identity a un objet-valeur pour cela — et c'est ce champ que Notification lit. · Une
  enveloppe illisible fabrique un identifiant neuf (`IdentityEventsConsumer.cs:39`). ·
  `UpdateProfile` publie même sans changement.

**Sain** — Outbox dans la transaction ; l'Inbox rejoue correctement un message inachevé ;
l'offset n'est validé qu'après traitement ; `xmin` sur les deux agrégats ; `GetCustomerFile`,
`ListCustomers` et `GetCustomerAccessLog` appliquent exactement la table du 27 septembre ;
tous les chemins d'écriture ferment la porte au rôle `service`.

---

## Delivery

Porte le cycle de vie d'un colis : création sur devis consommé, encaissement, recherche,
exécution, remise contre code, clôture. C'est aussi là que la matrice de visibilité est
censée s'appliquer.

- **[BLOQUANT] Un paiement arrivé après l'abandon garde l'argent et tue la course** — `.../Commands/Internal/ExpirationDesImpayees.cs:392`
  Le balayage passe en `PAYMENT_FAILED` — état **terminal** — toute course impayée de plus
  de quinze minutes. Le client paie à la 14e minute, le webhook arrive à la 16e :
  `ConfirmPayment` lève, le message est rejoué trois fois puis abandonné, et côté Payment
  `MarkSucceeded` ne regarde jamais l'échéance. Argent prélevé, course morte, aucun
  remboursement, et le client lit « paiement échoué ».

- **[BLOQUANT] `DeliveryPaymentFailed` n'existe pas sur le bus** — `.../IntegrationEvents/DeliveryIntegrationEventPublisher.cs:517`
  Le fait de domaine tombe dans le `_ => null` du publieur, et le contrat n'a pas de charge
  correspondante alors que l'énuméré porte `PAYMENT_FAILED`. Payment ne ferme donc jamais
  l'intention. C'est l'événement qui manque pour que le défaut précédent cesse d'être
  silencieux.

- **[BLOQUANT] Driver injoignable à l'affectation : le livreur reste coincé** — `.../Commands/Internal/InternalHandlers.cs:136`
  L'appel porte une échéance de trois secondes ; les trois tentatives du consommateur
  couvrent sept secondes. Au-delà, le message est abandonné : la course reste en recherche,
  le livreur reste `ON_MISSION`, il ne peut ni se mettre hors ligne ni recevoir d'offre.
  `ReleaseLateAcceptance` couvre l'autre branche du même `if` ; celle-ci n'a aucun filet.

- **[SÉRIEUX] `finance` lit l'historique de courses d'un client nommé, prix compris** — `.../Authorization/DeliveryAccess.cs:31`
  `ScopeFor` rend le filtre inchangé aux quatre rôles internes confondus. La matrice écrit
  « Historique de ses courses : finance Non » et « Ouvrir l'annuaire : finance Non ».

- **[SÉRIEUX] Le nom et la plaque du livreur restent visibles après la clôture** — `.../Views/DeliveryViewMapper.cs:72`
  Seul le **téléphone** est borné à la mission en cours. Le référentiel borne l'identité
  entière « jusqu'à la clôture ». L'historique rendant vingt-cinq courses, un client garde
  le nom et l'immatriculation de vingt-cinq livreurs, indéfiniment. C'est le raisonnement
  appliqué au téléphone du destinataire, non appliqué dans l'autre sens.

- **[SÉRIEUX] Pagination tronquée en silence au-delà de cent lignes** — `Hba.Delivery.Api/Grpc/DeliveryGrpcService.cs:111`
  La couche Api garde la taille demandée, le gestionnaire la borne à 100, et le jeton de
  page suivante est calculé sur la taille **demandée** : une console qui demande 500 reçoit
  100 lignes et un jeton vide. Le reste du périmètre est inatteignable, sans erreur.

- **[SÉRIEUX] La clôture forcée est ouverte aux quatre rôles internes** — `.../Commands/Closure/ClosureHandlers.cs:44`
  Et l'audit présente les quatre comme `Admin`. Une clôture en `Cancelled` publie une
  retenue : c'est un geste qui engage de l'argent.

- **[SÉRIEUX] Les deux points d'application de la matrice ne sont couverts par aucun test** — `DeliveryAccess.cs`, `DeliveryViewMapper.cs`
  Les trois constats de visibilité ci-dessus sont exactement ce qu'une suite d'autorisation
  aurait attrapé. Les tests de domaine couvrent l'OTP, les transitions et la retenue.

- **[MINEUR]** `Unassign` est écrit, câblé côté service, et inatteignable — la
  réaffectation répond `UNIMPLEMENTED` parce que le point 3 n'était pas tranché. Piège à
  connaître avant de brancher : `Unassign` remet le livreur à null, et la retenue retombe
  alors à zéro — après réaffectation, le déplacement du premier livreur ne serait plus
  dédommagé. · L'étape de la preuve ne part pas chez Media (`DepotDePreuveHttp.cs:123`) :
  une photo de collecte est rangée comme une preuve de remise, même restriction et même
  purge. · Migration `Pourboire` vide. · `quote_id` et `quoted_at` jamais renseignés dans
  l'instantané tarifaire rendu.

**Sain** — Outbox dans la transaction, drainée juste avant le commit ; `xmin` sur
l'agrégat, donc double affectation et devinettes d'OTP concurrentes départagées en base ;
le compteur de tentatives d'OTP est persisté **avant** le refus, et la comparaison est à
temps constant ; les quatre actions du livreur et la création lisent réellement leur clé
d'idempotence, cloisonnée par donneur d'ordre ; SQL agrégé entièrement paramétré.

---

## Ride

Le cycle de vie d'une course de personnes, de la demande au dépôt du passager.

- **[BLOQUANT] Paiement encaissé sur une course déjà close, sans ligne de remboursement** — `Hba.Ride.Api/Messaging/PaymentEventsConsumer.cs:70`
  La branche « succès » n'appelle que le démarrage de la recherche. Si la course est
  annulée ou échouée, le refus est journalisé en Information et **acquitté**. Scénario — et
  c'est le chemin normal du point 3 : le passager commande, lance son paiement, se lasse,
  annule avant qu'un chauffeur n'accepte (l'annulation est gratuite et autorisée), et le
  webhook confirme dix secondes plus tard. L'argent est encaissé, il n'y a pas de course,
  rien n'entre dans la file de `finance`, et aucun journal n'est en Warning.

- **[BLOQUANT] Le chauffeur qui annule encaisse la retenue** — `Hba.Ride.Domain/Rides/RideAggregate.cs:152`
  `RetenueDue()` ne regarde que la présence d'un chauffeur, jamais l'auteur de
  l'annulation, et le contrôle d'accès admet explicitement le chauffeur affecté. Payment
  crédite le chauffeur du montant et réduit d'autant le remboursement recommandé au
  passager. Grille à 20 %, course de 3 000 F : un chauffeur accepte puis annule aussitôt et
  gagne 600 F pour une course qu'il a refusée. Exploitable en boucle.

- **[BLOQUANT] Une course dont le paiement n'a pas pu s'ouvrir reste « demandée » pour toujours** — `.../Repositories/RideRepository.cs:84`
  Le balayage exige une intention de paiement non nulle. Or quand Payment est injoignable,
  l'intention rendue est vide et n'est pas rattachée : la garde exclut donc exactement les
  courses qu'elle devrait fermer. Trente secondes d'indisponibilité, et le passager a un
  devis brûlé, aucune page de paiement, et une course indéfiniment « en cours » sans autre
  issue que la réinstallation.

- **[BLOQUANT] Le back-office, `support` compris, peut clôturer une course en `COMPLETED`** — `.../Commands/RideLifecycleHandlers.cs:101`
  La clôture fixe le prix final et publie la rémunération, que Payment crédite. Le
  référentiel est explicite : un admin « peut seulement clôturer en `FAILED` ou
  `CANCELLED` avec motif ». Et une course de personnes n'a pas d'OTP : la clôture par le
  chauffeur est la **seule** preuve que le service a été rendu. Un agent `support` facture
  un passager pour une course qui n'a jamais démarré.

- **[SÉRIEUX] La matrice n'est appliquée nulle part côté service** — `.../Common/Views/RideView.cs:63`
  `RideView.From(ride)` ne prend pas l'appelant : elle rend toujours les deux identifiants,
  le prix estimé et le bloc chauffeur complet. Les quatre lectures l'utilisent telle quelle,
  y compris celle du chauffeur. Deux effets mesurables : le chauffeur reçoit le prix payé
  par le passager, que la matrice lui refuse ; et le passager garde le téléphone et la
  plaque du chauffeur sans aucune borne de temps, sur vingt courses d'historique. Delivery
  a un mappeur pour cela ; Ride n'en a pas.

- **[SÉRIEUX] Le chauffeur ne peut pas voir sa rémunération : elle n'est pas au contrat** — `contracts/proto/hba/ride/v1/ride_service.proto:120`
  Le message `Ride` porte le prix estimé et le prix final, jamais `driver_earning`. Le
  chauffeur voit donc le seul chiffre que la matrice lui interdit, et pas celui qu'elle lui
  accorde.

- **[SÉRIEUX] Hors périmètre, Ride répond 403 là où Delivery répond 404** — `.../Authorization/RideAccess.cs:107`
  La matrice tranche l'inverse pour Delivery : « un 403 confirmerait l'existence de la
  livraison à quelqu'un qui n'a rien à en savoir ». Ici, itérer des identifiants distingue
  les courses existantes au code de réponse.

- **[SÉRIEUX] Une course peut être payée pour un véhicule que personne ne peut servir** — `.../RideAggregate.cs:328`
  `Request` n'interdit pas le type non précisé, et la passerelle y envoie tout nom inconnu.
  Un corps portant `"vehicleType": "VELO"` crée la course, l'encaisse, et la recherche
  finit en « aucun chauffeur » après paiement.

- **[SÉRIEUX] Le véhicule du chauffeur affecté n'est pas confronté à celui commandé** — `.../Clients/DriverGrpcDirectory.cs:85`
  Un passager paie une voiture, Dispatch affecte une moto, la course se déroule, rien ne
  signale l'écart.

- **[SÉRIEUX] Le chauffeur reste en mission si l'affectation arrive après l'annulation** — `.../Commands/InternalRideHandlers.cs:89`
  L'annulation part sans identifiant de chauffeur ; l'affectation en vol est refusée et
  acquittée ; Driver ne libère que sur un événement **nommant** le chauffeur. Aucun second
  événement ne le nommera jamais.

- **[SÉRIEUX] Le balayage des impayées perd tout un lot sur un seul conflit** — `.../Internal/ExpirationDesCoursesImpayees.cs:102`
  Cent courses modifiées puis enregistrées en un seul appel : un webhook arrivé entre la
  lecture et l'écriture sur **une** course annule les 99 autres — alors que le commentaire
  du gestionnaire promet l'inverse. La requête elle-même n'a aucun index : balayage complet
  de `rides` toutes les minutes.

- **[SÉRIEUX] Annulation non rejouable pour une application hors ligne** — `.../RideAggregate.cs:561`
  Le référentiel exige de l'app livreur des actions mises en file avec clé d'idempotence ;
  aucune route mutante du chauffeur n'en porte, et `Cancel` rend `RIDE_ALREADY_CLOSED` au
  rejeu, là où `MarkPaymentFailed` traite le même cas en succès.

- **[SÉRIEUX, corrigé après vérification] Le service refuse de démarrer depuis l'IDE** — `Hba.Ride.Infrastructure/DependencyInjection.cs:92`
  `AddHbaServiceToken` valide au démarrage que l'identifiant et le secret sont renseignés,
  et **aucun des deux `appsettings` de Ride ne déclare la section `ServiceToken`** — les
  quatre autres services qui appellent cette extension la déclarent. **Vérifié
  moi-même** : en conteneur le service démarre, `src/Services/Ride/docker-compose.yml:73`
  fournit les trois variables. Le défaut ne touche donc que l'exécution depuis l'IDE, qui
  est le mode de travail quotidien. Second verrou dans le même sens : la configuration de
  développement d'Identity ne connaît aucun client de service `ride`.

- **[MINEUR]** Motif d'annulation facultatif pour le back-office, là où le référentiel
  écrit « avec motif ». · `RidePaymentFailed` ne sort pas, et le publieur annonce deux
  faits retenus quand il y en a trois. · Migration `Pourboire` vide. · Mot de passe de
  développement dans l'`appsettings.json` non-Development.

**Sain** — Outbox dans la transaction, drainée avant le commit ; l'identifiant d'événement
dérivé pour les demandes de notification est déterministe au rejeu et distinct de celui de
la course ; les cinq consommateurs du topic existent.

**Tests** — Un seul fichier, dix cas, tous sur le montant de la retenue. Non couverts :
l'auteur de l'annulation, la table des transitions, le contrôle d'accès entier,
l'idempotence de la création, le rejeu des consommateurs, le contenu de la vue par acteur.

---

## Pricing

Grilles tarifaires versionnées par zone, nature, véhicule et plage horaire ; émission et
consommation unique des devis ; zones PostGIS.

- **[BLOQUANT] Le type de véhicule n'est jamais vérifié à la consommation du devis** — `Hba.Pricing.Domain/Quotes/Quote.cs:190`
  `Consume` contrôle la nature, le preneur, l'expiration et le trajet — jamais le véhicule,
  qui fait pourtant partie de la **clé** de la grille. Le champ n'est ni dans la requête de
  consommation, ni dans le message rendu : aucun appelant ne peut même le relire. Ride
  transmet le véhicule venu du **corps de la requête** sans le rapprocher du devis.
  Scénario réalisable depuis l'application cliente avec un simple jeton `customer` :
  demander un devis en `MOTORCYCLE` Ganhi → Calavi, puis commander avec ce devis et
  `"vehicleType": "CAR"` — nature identique, trajet identique, tous les contrôles passent.
  Course en voiture au tarif d'une moto, prix ferme, part du chauffeur calculée dessus.
  C'est mot pour mot la faille du 30 septembre et celle du 1er octobre, sur la troisième
  dimension de la même clé. **Vérifié : la signature de `Consume` (ligne 192) ne reçoit pas
  le véhicule, et le seul refus de correspondance est celui de la nature (ligne 210).**

- **[SÉRIEUX] Aucun jeton de concurrence : la consommation unique n'est pas garantie** — `.../Configurations/PricingConfigurations.cs:119`
  Pricing est le **seul** service du dépôt dont les agrégats ne mappent pas `Version` sur
  `xmin` — et la colonne existe quand même en base, jamais incrémentée, ce qui donne
  l'apparence d'un verrou. `Consume` est un lire-puis-écrire sans garde : deux
  consommations simultanées du même devis avec deux missions différentes passent toutes
  les deux, et aucun index unique ne l'empêche. `QUOTE_ALREADY_CONSUMED` ne tient qu'en
  séquentiel, ce que les tests vérifient.

- **[SÉRIEUX] `ConsumeQuote` n'a aucune vérification d'autorisation** — `.../Features/Quotes/Commands/ConsumeQuote.cs:48`
  Aucun appel au contrôle d'accès, qui n'a d'ailleurs pas de méthode pour cette commande.
  Le raisonnement que ce même fichier écrit pour les grilles vaut ici : le jeton reporté
  est celui de l'utilisateur final. N'importe quel porteur de jeton atteignant le service
  brûle le devis d'un tiers, et le passager légitime obtient « devis déjà consommé » au
  moment de commander. Le contrat dit « appelé par Delivery, JAMAIS par une application
  cliente » : c'est un appel qui devrait exiger le rôle `service`.

- **[SÉRIEUX] La marque « mesure dégradée » est écrite et jamais lue** — `.../Common/Views/QuoteView.cs:16`
  La colonne existe, la vue ne la porte pas, le contrat n'a pas de champ, aucune requête ne
  liste les devis marqués. La décision du 2 octobre promettait que « l'exploitation peut
  retrouver les courses concernées » : cela demande aujourd'hui un accès SQL direct.

- **[SÉRIEUX] Le repli local perd toute la part à la minute sur un prix ferme** — `.../Services/Routing/HaversineRouteEngine.cs:48`
  La mesure de repli rend une durée nulle, donc une part à la minute nulle. Grille à 50 F
  la minute, trajet de 25 minutes, Routing arrêté : 1 250 F perdus en plus de la
  sous-estimation de distance, sur un prix ferme. Le fichier l'annonce lui-même, et rien
  n'empêche de saisir une grille à tarif horaire non nul.

- **[MINEUR]** Six migrations entièrement vides, dont quatre portant des noms empruntés à
  d'autres services. · Outbox, Inbox, idempotence et producteur Kafka enregistrés alors
  qu'aucun agrégat de Pricing ne lève d'événement : le dispatcher interroge indéfiniment
  une table vide. · Le semis initial ne peut pas porter le taux de retenue du point 3.

**Tests** — Les dix cas de consommation passent tous la nature « livraison » : le refus de
non-correspondance de nature n'est testé nulle part, et le véhicule, non contrôlé, encore
moins. Rien ne couvre le calculateur, les plages horaires, ni le chevauchement de grilles.

---

## Routing

Mesure un trajet ou une matrice via OSRM, avec cache Redis et repli à vol d'oiseau marqué.
Aucune base, aucune donnée possédée.

- **[SÉRIEUX] Le fournisseur par défaut est le repli, y compris hors développement** — `Hba.Routing.Api/appsettings.json`
  `"Provider": "haversine"` est dans l'`appsettings.json` non-Development, et c'est aussi
  la valeur par défaut du code. Le même objet est alors enregistré comme fournisseur **et**
  comme repli : chaque réponse est marquée dégradée. En chaîne : tout devis est dégradé, le
  prix d'une course est ferme et sous-estimé d'environ un tiers en ville, et la marque n'est
  lisible nulle part (voir Pricing). Le point 35 écrit : « brancher Routing n'est pas une
  amélioration parmi d'autres, c'est la condition de ce choix ». Aucun fichier de `deploy/`
  ne fournit `Routing__Provider=osrm`, et le service `osrm` déclaré dans l'infrastructure
  n'est interrogé par personne.

- **[SÉRIEUX] `[Authorize]` nu, alors que l'en-tête affirme le contraire** — `Hba.Routing.Api/Grpc/RoutingGrpcService.cs:25`
  Le commentaire écrit « aucun appelant public : l'ouvrir au dehors reviendrait à offrir un
  moteur de routage gratuit à qui possède un jeton » — et c'est littéralement ce que fait
  un `[Authorize]` sans rôle. Pricing reporte le jeton de l'utilisateur final : un jeton
  `customer` est accepté. Le calcul de matrice, 400 cases par appel, n'a aucun quota.

- **[MINEUR]** La clé de cache du tracé n'est pas symétrique (`RedisRouteCache.cs:107`
  contre `:50`) : la lecture indexe sur le tracé **demandé**, l'écriture sur le tracé
  **reçu** — défaut de cache permanent et silencieux sur les couples où OSRM rend une
  géométrie vide. · Les points d'entrée OSRM par profil ne sont configurables nulle part :
  tant que le dictionnaire est vide, le profil vélo rend des mesures de voiture. · Le jeton
  d'annulation n'est pas transmis aux appels Redis.

**Sain** — La tolérance aux pannes de cache ferme correctement l'incident du 2 octobre ;
le repli n'est jamais mis en cache ; une cellule injoignable n'est pas traduite en zéro ;
un profil non précisé retombe sur la voiture, la mesure la plus prudente.

**Tests** — Aucun. Non couverts : l'ordre cache → fournisseur → repli, l'inversion
longitude/latitude (dont le commentaire dit qu'elle « produit un itinéraire parfaitement
valide, quelque part en Somalie »), la cellule injoignable, le plafond de 400.

---

## Dispatch

Ouvre une recherche par mission payée, sollicite un livreur à la fois du plus proche au
plus loin sur trois anneaux, arbitre l'acceptation.

- **[BLOQUANT] Deux recherches du même balayage offrent au même livreur** — `.../Commands/AdvanceDispatch.cs:127`
  La réservation est **asynchrone** : l'offre part par l'Outbox (sondage d'une seconde),
  Kafka, puis le consommateur de Driver. Le planificateur, lui, parcourt jusqu'à cinquante
  recherches en quelques millisecondes. Deux livraisons dans le même quartier, même
  balayage : la seconde lit Driver où le livreur est encore disponible et le retient aussi.
  Il accepte les deux — le verrou Redis porte sur la **mission**, et chaque agrégat ne
  regarde que ses propres offres. Deux courses passent en « livreur affecté » sur le même
  homme ; le second démarrage de mission lève, et l'exception est avalée. Les trois
  barrières annoncées sont toutes par mission ; aucune n'est par livreur, alors que
  l'invariant du référentiel est « un livreur ne peut porter qu'une seule offre réservée à
  la fois ».

- **[BLOQUANT] Un réessai d'une acceptation réussie répond « déjà prise »** — `.../Commands/AcceptOffer.cs:19`
  La commande porte une clé d'idempotence que le gestionnaire n'utilise **nulle part**, et
  le magasin est enregistré sans appelant. Le livreur appuie, le réseau de Cotonou coupe la
  réponse, l'application rejoue comme l'ADR le lui impose : l'état est désormais « affecté »,
  l'agrégat lève « déjà prise », et l'écran annonce au livreur qu'il a perdu la course —
  alors qu'il l'a gagnée, qu'il est en mission, et que le client l'attend.

- **[SÉRIEUX] L'acceptation ne revérifie pas l'éligibilité** — `.../Commands/AcceptOffer.cs:65`
  Trente secondes passent entre l'envoi et l'acceptation, et rien n'y est relu : ni le
  dossier, ni l'état, ni la position. Un dossier refusé pendant ce laps empêche les offres
  futures mais laisse vivante celle en cours : un livreur au dossier refusé transporte le
  colis d'un client.

- **[SÉRIEUX] Une acceptation perdue à la concurrence sort en erreur interne** — `.../Persistence/DispatchDbContext.cs:38`
  Le conflit de concurrence n'est pas traduit. Le planificateur expire l'offre pendant que
  le livreur accepte : il reçoit un 500 avec une référence de support au lieu de « offre
  expirée ». L'écart est écrit noir sur blanc dans le contexte de Driver, qui nomme Dispatch
  parmi les services au même trou.

- **[MINEUR]** Le plafond de balayage est une constante de 50, justifiée par une durée de
  vie de quatre-vingt-dix secondes que le point 23 a révoquée — dix livreurs à trente
  secondes font cinq minutes. · Deux codes de refus annoncés au contrat et impossibles à
  produire : l'application livreur prévoit deux écrans jamais atteints. · Le journal de
  démarrage annonce « 3 vagues », notion que le point 23 a supprimée. · Les adresses de
  Location et Routing sont absentes de l'`appsettings`. · `GetCurrentOffer` choisit au
  hasard entre deux offres en attente.

**Sain** — La libération du livreur est complète sur tous les chemins de clôture ; le
verrou Redis libère par script conditionnel ; index unique contre le rejeu de la
confirmation ; l'identifiant d'offre est posé par le domaine, donc l'événement en porte un
vrai ; le classement par temps d'arrivée ne peut pas bloquer une vague.

**Tests** — Aucun projet. Ni la concurrence d'acceptation, ni l'expiration parallèle, ni le
plafond de dix livreurs, ni le passage d'anneau.

---

## Driver

Tient le dossier KYC, le véhicule, les habilitations et l'état opérationnel ; répond « qui
est éligible » par identifiants, sans jamais connaître de position.

- **[BLOQUANT] `GetDriverPublicProfile` n'a aucune autorisation** — `.../Queries/GetDriver.cs:48`
  Le gestionnaire rend nom, téléphone, plaque, marque, modèle et couleur sans vérifier le
  rôle, l'existence d'une mission, ni aucune borne de temps. Un jeton `customer` quelconque
  avec l'identifiant d'un livreur appris la semaine dernière obtient son numéro personnel,
  indéfiniment. Le commentaire du fichier le reconnaît et renvoie à la passerelle — ce que
  la consigne 3 interdit.

- **[SÉRIEUX] `Reinstate` revalide un dossier dont le véhicule n'a jamais été examiné** — `Hba.Driver.Domain/Drivers/DriverAggregate.cs:337`
  Déclarer un véhicule renvoie le dossier à l'examen mais **n'efface pas** la date de
  validation, dont `Reinstate` déduit le statut retrouvé. Livreur validé en moto → déclare
  une voiture → ops le suspend puis lève la suspension → il repasse « vérifié » avec une
  voiture que personne n'a vue et sans la carte grise correspondante. Il reçoit des offres.
  Le commentaire promet « le statut retrouvé se déduit, il ne s'invente pas » : ici il
  s'invente.

- **[SÉRIEUX] Rien ne sort un livreur de « en mission », et ops ne peut pas le forcer** — `.../DriverAggregate.cs:692`
  `GoOffline` lève depuis cet état, même pour `admin` — alors que le contrôle d'accès
  annonce précisément l'inverse. Un événement de fin perdu (trois tentatives, offset
  acquitté) laisse le livreur invisible pour toutes les vagues et incapable de se mettre
  hors ligne. La seule réparation par l'API est de le **suspendre**, ce qui écrit un motif
  mensonger dans son dossier et le fait retomber dans le défaut précédent.

- **[SÉRIEUX] `GetApplication` ne rend pas marque, modèle et couleur** — `Hba.Driver.Api/Grpc/DriverGrpcService.cs:345`
  Trois champs du contrat restent vides, et la vue ne les transporte même pas. Le livreur
  déclare « Toyota Corolla blanche », revient sur l'écran, ne voit rien, et refait sa
  déclaration. Le profil public, lui, les rend : c'est une omission de mappage.

- **[SÉRIEUX] Le topic `hba.driver.events.v1` n'a aucun consommateur** — `.../DriverIntegrationEventPublisher.cs:46`
  Trois faits publiés, personne en face. Le défaut n'est pas la publication — c'est une
  trace d'audit utile — mais le commentaire, qui affirme « c'est le dispatch qui en dépend :
  un livreur passé hors ligne ne doit plus figurer dans une vague ». C'est faux : Dispatch
  lit l'éligibilité en synchrone, ce qui est la bonne conception. Le jour où quelqu'un s'y
  fiera pour supprimer cet appel, les vagues partiront vers des livreurs hors ligne. Au
  passage, quatre messages par mission et par livreur.

- **[MINEUR]** `GetApplication` refuse `support` et `finance` contrairement à son propre
  contrat, ce qui rend deux branches de sa journalisation inatteignables. · `ops` ne peut
  ni suspendre ni rétablir un livreur, alors que le référentiel lui confie « la supervision
  des livreurs » — plus étroit que la règle, donc sans risque, mais à décider. ·
  `CapacityGrams` est écrit et jamais interrogé : le filtre ne porte que sur le type, et
  l'événement de confirmation ne porte pas le poids. · Deux codes d'erreur déclarés et
  recopiés à la main dans les routes.

**Sain** — Une seule sauvegarde par commande, Outbox dedans ; **le seul service du dépôt
qui traduise le conflit de concurrence en code métier** ; index exactement sur le filtre du
chemin chaud ; listes plafonnées et paginées ; une liste d'identifiants vide ne rend pas
l'annuaire.

---

## Location

Tient la dernière position de chaque livreur dans Redis avec une fenêtre de fraîcheur, et
répond « où est-il » / « qui est près d'ici » sans savoir qui est le livreur.

- **[BLOQUANT] Le plafond est appliqué avant le filtre de fraîcheur, sur un index que rien ne purge** — `.../Services/RedisPositionStore.cs:256`
  La recherche géographique reçoit la limite et rend les N membres les plus proches ; la
  fraîcheur n'est testée qu'**ensuite**, sur ces N-là. Or rien n'expire : un livreur dont
  le téléphone s'éteint laisse son point pour toujours — seul un passage explicite hors
  ligne l'efface. Carte du client : quinze points morts plus proches que les livreurs en
  service, les quinze sont écartés après coup, la réponse est vide, et le client lit
  « aucun livreur dans ce secteur » dans un quartier couvert. Dispatch : cinquante points
  morts dans l'anneau de 2 km suffisent à rendre zéro candidat, et **les trois anneaux
  étant concentriques**, élargir reprend les mêmes morts. La course finit en « aucun
  livreur trouvé » avec des livreurs disponibles à côté, et le symptôme ment sur sa cause.

- **[SÉRIEUX] Le jeton de service est reconnu à l'absence de rôle, ce qui est faux depuis le 30 septembre** — `.../Authorization/LocationAccess.cs:91`
  Le test est « aucun rôle, ou back-office », avec le commentaire « un jeton de service ne
  porte aucun rôle ». Le référentiel a ajouté le rôle `service` précisément parce que cette
  convention cassait. Deux conséquences : la branche visée est **morte** — un vrai jeton de
  service est refusé ; et tout jeton authentifié **sans aucun rôle** peut localiser un
  livreur nommé. Personne ne tombe dessus aujourd'hui parce que l'appel descend d'une
  requête back-office dont le jeton est reporté ; le jour où il partira d'un planificateur
  ou d'un consommateur, l'offre manuelle du 27 septembre cessera de marcher.

- **[SÉRIEUX] La recherche de proximité rend l'identifiant, le cap, la vitesse et la précision à un jeton client** — `.../Features/Positions/PositionQueries.cs:104`
  Le garde-fou est une liste de **refus** (partenaire, commerçant) : un `customer` passe.
  Le point 24 écrit que l'identifiant « n'est JAMAIS rendu » et que c'est « ce qui borne
  tout le reste » ; le point 37 refuse au client le cap, la vitesse et la précision même
  pour le livreur qui le sert. Les deux ne tiennent qu'à la projection de la passerelle.
  Un identifiant stable, c'est la possibilité de suivre le même livreur d'un jour sur
  l'autre — exactement ce que le point 24 refuse.

- **[MINEUR]** Aucune clé Redis n'a de TTL et aucune purge n'existe : assumé et motivé dans
  le code, mais c'est la cause directe du bloquant ci-dessus, et le balayage évoqué comme
  solution n'existe pas. · La mise à jour de position ne fait aucune lecture, donc la carte
  de l'exploitation montre tout téléphone qui émet, dossier en attente ou refusé compris.

**Sain** — La fenêtre de fraîcheur voyage avec chaque réponse, y compris les vides ; le
rayon terrestre est celui de Redis, donc deux écrans n'affichent pas deux distances ;
aucune lecture en N+1 ; la liste des positions fraîches part de l'ensemble trié des
horodatages et évite précisément le défaut de la recherche ; le canal des battements est
aligné avec Realtime, nom et champs compris ; « absent » et « zéro » restent distincts de
bout en bout.

**Tests** — Aucun projet.

---

## Payment

Encaisse les missions et les pourboires par FedaPay (page hébergée, webhook signé comme
seule vérité), tient le grand livre des livreurs, les demandes de versement et la file des
remboursements à faire à la main.

- **[BLOQUANT] `CreateTipIntent` ne vérifie aucun appelant** — `.../Commands/CreateTipIntent.cs:56`
  Le gestionnaire valide le montant, prend le sujet du jeton comme payeur, et c'est tout :
  ni rôle, ni vérification que l'appelant est le client de la mission. Ses deux voisins
  immédiats le font. La passerelle ne compare pas non plus l'identifiant de la course au
  sujet du jeton : **personne ne le fait**. Un livreur, un `support`, voire un jeton de
  service, ouvre une page de paiement à son nom sur la mission d'un tiers, apprend du
  message d'erreur si cette mission est allée au bout, et consomme le garde-fou « un seul
  pourboire par mission ». **Vérifié : le contexte d'appel est injecté ligne 43 et n'est lu
  qu'une fois, ligne 135, pour le sujet.**

- **[BLOQUANT] Une intention de pourboire déjà ouverte est rendue telle quelle, sans comparer le montant ni le payeur** — `.../Commands/CreateTipIntent.cs:86`
  Un passager demande 200 F, abandonne, redemande 1 000 F : le service lui rend l'intention
  de 200 F et son adresse de paiement. Il paie 200 F en croyant en donner 1 000. Combiné au
  constat précédent, l'intention rendue peut être celle d'un **autre payeur**, avec son
  identifiant, sa référence fournisseur et son adresse — trois champs que le domaine
  réserve au service et à `finance`.

- **[BLOQUANT] Un pourboire est publié comme le paiement de la mission** — `.../IntegrationEvents/PaymentIntegrationEventPublisher.cs:64`
  Les trois événements de paiement ne portent pas la raison, et leur référence est la
  mission. Deux dégâts vérifiés : côté Delivery, la confirmation sur une livraison déjà
  remise lève, trois réessais, journal en erreur — **à chaque pourboire** ; et le
  remboursement, lui, **passe**, car `MarkRefunded` n'a aucun garde d'état : un pourboire
  de 200 F repris à la main marque la livraison entière remboursée, et l'écran du client
  lui annonce le remboursement d'une course qu'il a bien reçue. Côté Ride, aucun dommage :
  c'est le seul des deux consommateurs qui se protège.

- **[SÉRIEUX] Payment accepte le pourboire d'une livraison, que le point 42 ne tranche pas** — `Hba.Payment.Domain/Payments/PaymentIntent.cs:187`
  Le point écrit « ce que cela ne dit pas : le pourboire d'une livraison de colis ». Le
  domaine et le service l'acceptent pourtant ; le seul garde-fou est la passerelle qui code
  la nature en dur — exactement la règle que la consigne 3 interdit de poser là. Et c'est
  dans ce cas non décidé que le constat précédent fait le plus de dégâts.

- **[SÉRIEUX] La ligne de pourboire n'a pas l'index unique que le code affirme avoir** — `.../Configurations/DriverLedgerConfiguration.cs:63`
  Trois index uniques protègent la rémunération, le dédommagement et le versement. Le
  pourboire n'en a aucun, et la migration du jour n'ajoute que deux colonnes — alors que le
  gestionnaire écrit « la ligne déjà écrite est donc cherchée avant, par le même garde-fou
  que la rémunération ». Il n'existe pas.

- **[SÉRIEUX] Le garde-fou du crédit porte sur la mission, pas sur l'intention** — `.../Commands/ApplyProviderOutcome.cs:173`
  Intention A refusée, intention B payée et créditée ; le client revient sur la page de A,
  encore ouverte, et paie : un échec peut devenir un succès, puis le crédit trouve la ligne
  de B et **sort sans rien écrire**. Argent encaissé, livreur non crédité, et aucune ligne
  de remboursement — la file ne regarde que les intentions de mission.

- **[SÉRIEUX] Un remboursement de pourboire ne reprend rien au livreur** — `.../PaymentIntent.cs:355`
  L'événement est levé, aucun consommateur, aucune écriture compensatoire. La ligne de
  crédit est immuable — c'est juste — mais rien n'écrit la ligne de plus qui la corrige. Le
  relevé affirme que HBA doit un argent qu'elle a rendu, et le versement le paiera.

- **[SÉRIEUX] Les statistiques mélangent le pourboire et la recette** — `.../Reads/PaymentStatsReader.cs:70`
  Les quatre agrégations ne filtrent ni ne ventilent la raison. Commission zéro : un
  pourboire appartient en entier au livreur. La recette est donc surévaluée, sans moyen de
  séparer — sur un message dont le commentaire met justement en garde contre deux lectures
  de recette qu'on confond.

- **[SÉRIEUX] Une intention abandonnée bloque la mission définitivement** — `.../Repositories/PaymentIntentRepository.cs:22`
  « Vivante » est défini par le seul statut ; l'échéance n'est lue par personne, et le
  statut « expirée » n'a **aucun producteur** dans tout le dépôt. Aucun planificateur
  n'expire les intentions, alors que le référentiel en annonce un : un client qui abandonne
  la page reçoit toujours la même adresse périmée, et pour un pourboire, la durée de vie de
  dix minutes ne sert à rien.

- **[MINEUR]** La référence fournisseur quitte le service pour tout appelant, alors que le
  domaine et le proto disent « jamais hors du service et du back-office finance ». · Trois
  codes d'erreur déclarés et jamais levés, dont celui de la signature de webhook — le
  webhook répond un 400 nu. · Le message de remboursement du contrat décrit un
  déclenchement qui n'existe plus.

**Sain** — Outbox dans la transaction ; `xmin` sur les intentions, les demandes et les
remboursements dus ; index uniques sur la référence fournisseur, sur « une demande en cours
par livreur », sur « une ligne de file par intention », sur la rémunération et le
dédommagement ; **la signature du webhook est vérifiée sur le corps brut, à temps constant,
avec tolérance d'horodatage** ; le débit du grand livre n'a lieu qu'à la consignation du
virement, dans la même transaction que le passage à « versée » ; l'autorisation des
versements est centralisée et vérifiée dans le service.

**Tests** — Un seul projet, neuf cas, tous sur le dédommagement. Rien sur le pourboire,
rien sur le webhook et son rejeu, rien sur l'idempotence, rien sur l'autorisation des
versements, rien sur les remboursements. Billing, en comparaison, porte trois projets et
une soixantaine de cas dont la concurrence réelle.

---

## Billing

Tient le compte prépayé des donneurs d'ordre professionnels : une seule règle de débit
`solde + plafond ≥ montant`, un journal en ajout seul, une alerte de seuil.

- **[BLOQUANT] La clé de rejeu n'est pas rattachée au titulaire** — `.../Commands/CreditAccount.cs:48`
  La lecture de rejeu porte sur la portée et la clé seules ; ni le titulaire demandé ni
  celui du mouvement retrouvé ne sont comparés. Perte d'argent : `finance` crédite le
  commerçant A avec la clé « octobre-2026 », puis le commerçant B avec la même clé — le
  service rend le mouvement de A et **ne crédite rien à B**, qui a payé et dont le solde ne
  bouge pas, avec une réponse qui a l'air d'un succès. Fuite : un partenaire rejoue comme
  clé l'identifiant d'une course d'un autre, en nommant son propre compte, et reçoit le
  mouvement du premier — son identifiant de compte, sa référence de course, son solde. Le
  commentaire du débit annonce exactement la protection qui manque ; le contrôle est fait
  sur le titulaire de la **requête**, pas sur celui du mouvement. Côté base, l'index est à
  l'inverse **global** : une clé réutilisée d'une portée à l'autre franchit le magasin
  applicatif puis casse à l'écriture en erreur opaque.

- **[SÉRIEUX] Le rôle `service` obtient une écriture, que le référentiel lui refuse nommément** — `.../Authorization/BillingAccess.cs:173`
  Le référentiel écrit « il n'ouvre AUCUNE écriture, deux lectures aujourd'hui », et le
  socle de sécurité répète la phrase. L'annulation de débit l'accorde pourtant, et l'appel
  existe pour de bon depuis Delivery avec un jeton de service forcé. Le raisonnement du
  code est bon ; c'est la liste fermée du référentiel qui ne l'a pas enregistré — et c'est
  le document qu'un relecteur croira.

- **[MINEUR]** Deux annulations strictement concurrentes ne rendent pas le même mouvement :
  la seconde est rejetée par l'index au lieu d'être servie — « rejouable sans dommage »
  tient en séquentiel, pas en parallèle. · La branche « déjà remboursé » saute la
  vérification de compte que le chemin normal fait. · Inbox posée et aucun consommateur,
  alors qu'un commentaire décrit une recharge « consommée ici par l'Inbox, donc
  idempotente » : la recharge est entièrement manuelle. · L'alerte de seuil n'a aucun
  consommateur (point 32 non tranché, trou connu). · Migration `Pourboire` vide, alors que
  Billing n'a rien à voir avec cette fonctionnalité. · `Suspend` et `Reactivate` n'ont
  aucune rpc : l'état « suspendu » ne peut être créé par aucun chemin de production.

**Sain, et c'est le service le mieux tenu du backend** — Le verrou pessimiste de ligne
refuse de faire semblant quand aucune transaction n'est ouverte ; tout le débit — verrou,
règle, mouvement, clé, Outbox — tient dans une seule transaction ; unicité en base sur le
couple propriétaire ; vingt-neuf cas de test d'autorisation, dont la non-confusion
Partenaire/Commerçant à identifiant égal ; six tests d'intégration sur Postgres couvrant
les deux débits concurrents et le verrou hors transaction.

**Non vérifié** — Aucun rapprochement périodique ne confronte la somme des mouvements au
solde mis en cache, alors que l'agrégat l'annonce comme nécessaire.

---

## Rating

Ouvre une notation à chaque mission achevée, depuis les événements de Delivery et de Ride,
et la garde vide jusqu'à ce que le client note. **Service écrit le 3 octobre.**

- **[BLOQUANT] `OpenReview` n'a aucun contrôle d'appelant** — `Hba.Rating.Api/Grpc/RatingGrpcService.cs:100`
  Le contrat est explicite : « RÉSERVÉ AUX SERVICES, jamais exposé à une application ».
  L'implémentation ne porte que l'autorisation de classe, et le gestionnaire n'injecte même
  pas le contexte d'appel. **Vérifié : aucune occurrence du contexte d'appel dans
  `OpenReview.cs`.** Un livreur appelle avec une référence tirée au hasard, son propre
  identifiant et le compte d'un complice ; le complice note cinq étoiles. Répété, cela
  fabrique une moyenne de toutes pièces. Le propre commentaire du gestionnaire dit
  « laisser l'application les fournir reviendrait à laisser un client noter quelqu'un qui
  ne l'a jamais servi » : c'est ce que fait cette rpc. La règle n'est appliquée nulle part —
  ni au service, ni à la passerelle.

- **[BLOQUANT] Trois lectures sur quatre acceptent un identifiant de la requête sans vérifier le rôle** — `.../Features/Reviews/ReviewQueries.cs:72`
  La forme est la même aux trois endroits : « si l'appelant a le rôle X, le jeton ; sinon,
  l'identifiant de la requête » — et rien ne demande ensuite si cet appelant a le droit de
  nommer quelqu'un. Un jeton `customer` obtient la moyenne d'un livreur (ligne 72), que le
  point 41 lui refuse, et ses **commentaires** (ligne 112). Un jeton `driver`, `partner` ou
  `service` obtient les missions à noter d'un client nommé, chacune portant le livreur qui
  l'a portée (ligne 31).

- **[SÉRIEUX] La règle « aucun identifiant de livreur » n'est appliquée que dans la passerelle** — `.../Common/ReviewViews.cs:6`
  La vue porte toujours les deux identifiants et le mappeur les recopie ; c'est la
  projection de la passerelle qui les retire du JSON. Le jeton du client étant reporté
  jusqu'à Rating, un appel direct au service avec ce même jeton rend l'identifiant du
  livreur, que le point 24 réserve à l'affectation.

- **[SÉRIEUX] Une note peut être réécrite en course de vitesse** — `.../Configurations/ReviewConfiguration.cs:9`
  L'agrégat n'a **pas** de jeton de concurrence — la configuration n'appelle jamais
  `IsRowVersion`, et la migration crée une colonne que rien n'écrit. Tous les autres
  agrégats du dépôt sont sur `xmin`. Deux envois parallèles sur la même mission, l'un à une
  étoile, l'autre à cinq : les deux lisent « pas encore notée », les deux passent la garde,
  le dernier gagne sans erreur. L'invariant « une seule fois, et sans retour » existe pour
  empêcher qu'un livreur fasse corriger une note ; il est contournable par un doublon de
  requête.

- **[SÉRIEUX] Aucun projet de tests** — Quatre projets dans la solution, zéro test. Ni
  l'ouverture par un appelant non autorisé, ni la reprise du sujet du jeton, ni le barème,
  ni le seuil d'affichage, ni l'idempotence du consommateur.

- **[MINEUR]** L'idempotence promise par l'ouverture n'est pas rattrapée : deux ouvertures
  concurrentes font sortir la seconde en erreur non traduite, ce qui brûle les trois
  tentatives du consommateur. · `Time__ZoneId` est posé par le compose avec un commentaire
  qui en explique l'usage, et le service n'enregistre jamais le composant qui le lit. · Une
  table `personal_data_reads` est créée sans journal ni purge branchés. · Les deux index
  servent le filtre mais pas le tri. · Un jeton livreur sans claim produit « aucun livreur
  désigné », alors que le livreur a bien été désigné et que c'est le jeton qui est mal
  formé.

**Sain** — L'index unique porte le type de mission, ce qui empêche une course et une
livraison de même identifiant de se télescoper ; les deux topics sont consommés ; la
moyenne est calculée en base en un aller-retour, les non notées écartées ; l'envoi d'une
note vérifie bien le client côté service, back-office compris ; les deux listes sont
plafonnées.

---

## Notification

Consomme les commandes d'envoi, choisit le canal d'après le catalogue, rend le modèle,
envoie, consigne une ligne par tentative.

- **[BLOQUANT] Les trois adaptateurs partagent un seul client HTTP : la clé d'API du courriel part chez l'opérateur SMS** — `.../Extensions/NotificationInfrastructureExtensions.cs:274`
  Les trois enregistrements utilisent la surcharge qui dérive le nom logique du client de
  **l'interface**, pas de l'implémentation : les trois visent donc le même client nommé, et
  leurs trois configurations s'empilent dans l'ordre d'enregistrement. **Vérifié : les trois
  appels sont bien `AddHttpClient<INotificationSender, …>` aux lignes 195, 274 et 303.**
  Avec la configuration réellement déployée, l'adaptateur SMS reçoit un client dont
  l'adresse de base et l'en-tête d'autorisation sont ceux du service de courriel. Les SMS
  partent quand même, l'adaptateur construisant des URL absolues — mais **chaque appel SMS
  transmet la clé d'API du service de courriel à l'opérateur**, clé qui permet d'envoyer du
  courrier au nom du domaine. L'échéance de dix secondes posée exprès pour un code de
  connexion est au passage écrasée par celle du courriel. Et le jour où WhatsApp sera
  activé : l'adaptateur poste un chemin **relatif**, donc tous les gabarits partiraient vers
  l'API du courriel, échoueraient, et la chaîne retomberait silencieusement sur le SMS —
  cinq à dix fois plus cher, ce que l'ADR 0014 existe précisément pour éviter. Le journal
  accuserait « refus de Meta, statut 404 ».
  *Correctif : nommer chaque client, ou passer par des interfaces distinctes.*

- **[SÉRIEUX] Une enveloppe illisible désarme l'Inbox et le message repart** — `Hba.Notification.Api/Messaging/NotificationCommandsConsumer.cs:37`
  Chaque tentative ouvre une **nouvelle** entrée d'Inbox au lieu d'être écartée : trois
  appels réels à l'opérateur pour un message, et autant de segments facturés. Sur un
  rééquilibrage de partition, le client reçoit son code deux fois.

- **[SÉRIEUX] Le consentement WhatsApp n'est vérifié que par l'appelant** — `.../Sending/SendNotificationHandler.cs:32`
  La chaîne de canaux est suivie sans jamais interroger le consentement ; l'adaptateur
  l'écrit lui-même : « c'est à l'appelant de ne demander WhatsApp que pour un titulaire qui
  a consenti ». La règle tient aujourd'hui parce qu'Identity choisit la charge en fonction.
  Un second producteur qui publie sans canal imposé — la forme que le consommateur traduit
  en « suis la chaîne » — fait partir un gabarit vers un numéro qui n'a rien accordé. Le
  référentiel pose « sans consentement, aucun message WhatsApp ne part ».

- **[SÉRIEUX] Un opérateur SMS est implémenté alors que le point 10 n'est pas tranché** — `.../Extensions/NotificationInfrastructureExtensions.cs:268`
  Le point écrit « état du code : l'interface attend son implémentation », et le fichier des
  adaptateurs répète « aucun opérateur SMS n'est choisi ». L'adaptateur OVHcloud existe,
  complet, et c'est la valeur déployée. OVHcloud ne figure dans aucune des options listées,
  et les questions que le point exige de poser — connexion directe à MTN, Moov et Celtiis,
  délai médian, déclaration de l'expéditeur — n'ont pas de réponse dans le dépôt.
  Soit la décision a été prise et le point doit l'écrire, soit elle ne l'a pas été.

- **[SÉRIEUX] Aucune clé d'idempotence côté SMS** — `Senders/OvhSmsSender.cs:68`
  L'adaptateur courriel en pose une, et le contrat explique pourquoi : l'Inbox n'écarte pas
  le cas où l'envoi a **réussi** et où sa validation a échoué juste après. C'est le canal du
  code de remise — la preuve de livraison. Le destinataire reçoit deux fois le même code, et
  deux segments sont facturés.

- **[MINEUR]** Outbox, producteur Kafka et magasin d'idempotence enregistrés et jamais
  utilisés : le dispatcher interroge indéfiniment une table vide. · La colonne du
  destinataire reçoit parfois un identifiant de compte, dont le journal ne rend que les
  quatre derniers caractères — illisible pour le support. · Le téléphone du livreur promis
  au destinataire par la matrice n'a **aucun modèle** : le seul SMS qui lui part porte la
  référence et le code. · La branche push est morte des deux côtés.

**Sain** — L'autorisation du journal et de l'état des canaux est vérifiée dans les
gestionnaires, pas à la passerelle, avec un plafond de 200 lignes ; le destinataire est
masqué dès la couche applicative ; l'appel à l'annuaire part avec un jeton de service et
distingue « pas d'adresse » d'« annuaire muet » ; les modèles ne mélangent jamais deux
publics ; une trace par tentative, et le repli est consigné.

---

## Media

Tient l'inventaire des binaires, écrit dans Garage, signe des URL de lecture de cinq
minutes, purge les preuves au bout d'un mois.

- **[SÉRIEUX] Une suppression efface la ligne d'inventaire même quand le stockage a refusé** — `.../Assets/MediaHandlers.cs:219`
  L'ordre est : retirer la ligne, appeler le stockage, enregistrer — et l'adaptateur S3
  **avale** l'échec avec un simple avertissement. Garage indisponible trois secondes, un
  `admin` supprime la pièce d'identité d'un livreur : la ligne part, l'objet reste dans le
  seau, sans inventaire — donc introuvable, hors d'atteinte de la purge et de l'effacement
  de compte. C'est exactement ce que la purge des preuves prend soin d'éviter : « on préfère
  un inventaire qui pointe brièvement dans le vide à un fichier que plus personne ne sait
  nommer ». Même forme, en lot, pour l'effacement d'un compte.

- **[MINEUR]** Le type du fichier est celui que l'appelant **déclare** : aucun contrôle des
  octets de tête. · La conversion des énumérations contrat ↔ domaine repose sur un test que
  le commentaire annonce et qui n'existe pas — « le jour où l'une bouge sans l'autre, des
  cartes d'identité deviendraient des photos de profil » ; les deux coïncident aujourd'hui,
  vérifié ligne à ligne. · La liste des médias n'a aucun plafond ni pagination au contrat. ·
  La racine HTTP est anonyme.

**Sain, et c'est le composant le mieux tenu du lot** — Le contrôle d'accès applique la
matrice exactement : preuve de livraison réservée à `admin` et au service, liste qui tait
ce qu'elle refuserait de rendre, refus en « introuvable » pour ne pas confirmer une
existence, dépôt de service borné au seul couple course + preuve. Trente-deux cas de test
couvrent tout cela. L'URL signée dure cinq minutes et est produite **après** la
vérification, avec journalisation de la demande.

---

## Realtime

Trois hubs derrière la passerelle : état des missions au client, offres au livreur, carte
complète à l'exploitation. L'annuaire mission → chauffeur dans Redis est ce qui autorise
une position à passer.

- **[BLOQUANT] Un ré-abonnement après la clôture ré-arme le suivi, sans fin** — `Hba.Realtime.Api/Hubs/CustomerHub.cs:109`
  L'abonnement interroge le service propriétaire sur deux choses — la mission est-elle
  accessible, qui y est affecté — et **jamais** si elle est encore ouverte : les lectures
  répondent normalement pour une course livrée, et l'instantané du livreur y reste rempli.
  L'annuaire est donc réinscrit. Sans client modifié : l'écran de suivi reste monté après la
  livraison — il affiche l'état final et la page de pourboire — et l'application se
  réabonne à **chaque reconnexion automatique**. Un passage de réseau suffit. Kafka a déjà
  éteint l'inscription et ne repassera plus : à partir de là, chaque battement de ce livreur
  est routé vers ce client, chez son client suivant, puis chez lui. C'est la dérive que le
  point 37 interdit et que la matrice décrit comme « pire ici : la personne est nommée » —
  le client connaît déjà son nom, son téléphone et sa plaque.

- **[SÉRIEUX] Le garde-fou de durée de vie de l'annuaire ne se déclenche jamais** — `.../Diffusion/AnnuaireDesMissions.cs:104`
  L'échéance porte sur le **hachage entier** et est repoussée à chaque affectation de la
  plateforme. Les options promettent « au pire, la fuite dure douze heures au lieu de
  toujours » ; l'annuaire lui-même reconnaît que « son expiration est sans cesse repoussée
  tant que la plateforme tourne ». Les deux textes se contredisent, et c'est le second qui a
  raison.

- **[SÉRIEUX] Le démarrage d'une course n'est jamais diffusé** — `.../Messaging/RideEventsDiffuseur.cs:147`
  Le commentaire affirme « sept événements au contrat, et tous sont diffusés : un écran de
  suivi doit refléter chaque étape ». Le contrat ne porte **aucun** événement pour le
  démarrage : il va de « chauffeur arrivé » à « terminée ». Le passager monte, et son écran
  reste sur « le chauffeur est arrivé ».

- **[MINEUR]** Un groupe de consommateurs Kafka par conteneur, nommé d'après la machine :
  Kafka accumule un groupe mort par déploiement, pour trois topics. · Aucun test.

**Sain** — La vérification d'abonnement est déléguée au service propriétaire avec le jeton
du client, sans recopier la règle ; le refus ne distingue pas « pas à toi » de « n'existe
pas » ; le hub livreur ne prend rien de l'appelant ; la trame du client ne porte ni cap, ni
vitesse, ni précision, et la séparation d'avec la trame d'exploitation est explicite ;
l'absence d'Inbox est argumentée et correcte pour un effet qui est « écrire dans une
socket » ; une nature inconnue ne retombe jamais sur le colis.

**Non vérifié** — Le jeton est lu à la poignée de main et figé pour la vie de la connexion,
alors qu'un jeton d'accès vaut quinze minutes. Toute invocation sur une connexion plus
vieille reporterait un jeton expiré, et le hub rendrait « cette mission n'est pas
accessible » pour une cause sans rapport. Trancher demande de savoir si le client SignalR
Dart rappelle sa fabrique de jeton à chaque reconnexion.

---

## La passerelle

Entrée unique des quatre surfaces : traduit REST en gRPC, relaie trois flux d'octets,
recolle Location et Driver pour les cartes, relaie les hubs et les webhooks.

- **[SÉRIEUX] Les trois relais d'octets n'ont aucun plafond de taille** — `Endpoints/Relais/RelaisDeDepot.cs:50`
  Ni limite de corps, ni limite de partie multipart, sur aucune des trois routes. Media, lui,
  pose les deux. Vingt requêtes de 28 Mo en parallèle sur la photo de profil : chaque corps
  est mis en tampon sur le disque de la passerelle, puis Media refuse à 5 Mo. Le refus arrive
  après que les octets ont traversé. Ne pas connaître les plafonds métier n'est pas une
  raison de ne pas borner un corps.

- **[SÉRIEUX] L'API partenaire publique rend du protobuf sérialisé tel quel** — `Endpoints/Partner/PartnerEndpoints.cs:101`
  Le statut sort en **entier**, les dates en `{ seconds, nanos }`. Un partenaire externe lit
  `"status": 4` sur une API REST versionnée et doit connaître la numérotation du `.proto`.
  Le dépôt sait que c'est un piège — la projection cliente le raconte en détail — et
  l'application livreur porte des lecteurs doubles écrits exprès pour l'absorber. Une
  quinzaine de routes internes sont dans le même cas.

- **[SÉRIEUX] Les webhooks partenaires n'existent pas** — `Webhooks/WebhookSender.cs:45`
  L'expéditeur est enregistré et **injecté nulle part**. Le référentiel promet au partenaire
  quatre événements signés, « avec réessais, backoff, et journal des envois consultable » :
  aucun des trois n'existe, et la classe ne porte ni réessai ni journal. La création d'un
  partenaire collecte pourtant une URL, et la rotation fait tourner un secret que rien
  n'utilise.

- **[SÉRIEUX] Le dossier KYC d'un livreur, URL signées comprises, est ouvert à `finance`** — `Endpoints/Driver/ApplicationEndpoints.cs:174`
  Le groupe porte la politique back-office, les quatre rôles confondus, et la projection rend
  une URL de lecture par pièce. Rien dans le métier de `finance` — paiements, remboursements,
  reversements — ne demande d'ouvrir une carte d'identité.

- **[SÉRIEUX] Les quatre routes d'indicateurs bloc par bloc n'ont aucune échéance** — `Endpoints/Web/KpiEndpoints.cs:152`
  La route agrégée pose délibérément huit secondes, avec le raisonnement écrit ; les quatre
  blocs séparés héritent de l'échéance du client, trente secondes. La console attend une
  demi-minute exactement là où le raisonnement juste au-dessus l'interdit.

- **[MINEUR]** Un motif absent rend 500 au lieu de 400 sur cinq routes qui affectent une
  chaîne nullable à une propriété protobuf. · Un jeton livreur sans claim rend 500, alors
  que le hub traite le même cas en le nommant. · Un état de remboursement mal orthographié
  retombe en silence sur « à traiter », là où la route des versements refuse explicitement
  ce cas et explique pourquoi. · Créer un compte interne est moins filtré à la passerelle
  que lister l'équipe — sans conséquence, Identity refuse, mais cela se lit à l'envers de
  l'intention. · Media, Realtime et Rating sont absents de la page d'état des services, et
  la ligne du stockage objet est encore rattachée à Driver. · L'adresse de Notification
  manque en développement. · Le champ de preuve est encore transmis depuis le corps, alors
  que Delivery le refuse systématiquement et écrit « le jour où plus personne ne l'envoie,
  c'est le champ qui doit disparaître du contrat ». · La route des webhooks n'a aucune
  limitation de débit — la signature est vérifiée côté Payment, donc rien ne passe, mais
  rien ne borne les tentatives forgées.

**Sain** — Toutes les routes portent une politique explicite, relevé exhaustif fait ; la clé
d'idempotence est prise dans l'**en-tête** et jamais dans le corps, partout ; les
projections des livreurs alentour respectent le point 24 à la lettre, plafonds en dur
compris ; la pagination est plafonnée **côté service** partout où la passerelle relaie une
taille libre ; la traduction des erreurs gRPC vers HTTP est correcte.

**Non vérifié** — Le jeton des hubs voyage dans la chaîne de requête, et la passerelle relaie
les hubs par un client HTTP instrumenté : l'attribut d'URL complète d'une trace client porte
la chaîne de requête, et `access_token` ne figure pas dans la liste des valeurs masquées.
Serilog, lui, ne le journalise pas. Trancher demande de regarder une trace sortante vers
Realtime dans le collecteur.

---

# Ce qui demande une décision, pas un correctif

Cinq constats ne se corrigent pas en écrivant du code : ils demandent qu'on tranche
d'abord. Les ranger ici évite qu'un correctif les tranche en silence.

1. **Que fait-on d'un message qu'on ne peut pas traiter ?** C'est le motif transverse
   n° 1, et il n'a pas de réponse aujourd'hui — ni au bout de la chaîne d'envoi
   (`MaxAttempts` déclaré, jamais lu), ni au bout de la chaîne de réception (offset
   acquitté). Une file de rebut, un écran pour la lire, et une règle sur qui la traite.
2. **`finance` voit-il des données personnelles ?** Le référentiel dit non, huit endroits
   du code disent oui. Tant que ce n'est pas tranché dans un sens, chaque correctif
   individuel est une décision prise par celui qui écrit la ligne.
3. **Le pourboire d'un colis.** Payment l'accepte, le point 42 ne l'a pas ouvert, et c'est
   dans ce cas non décidé que la confusion d'événements fait le plus de dégâts.
4. **L'opérateur SMS.** Le point 10 dit qu'aucun n'est choisi ; un adaptateur complet est
   déployé. Soit le point est périmé, soit le code a pris les devants.
5. **Comment on sort un livreur de « en mission ».** Le trou est connu depuis hier ; la
   réparation par suspension écrit un motif mensonger dans un dossier, ce qui n'est pas un
   détail pour quelqu'un dont le dossier décide du droit de travailler.

---

# Un ordre de bataille

Proposé, pas prescrit. Le critère est le rapport entre ce que le défaut coûte et ce que le
correctif demande.

**Cette semaine — ce qui perd de l'argent ou expose des secrets**

1. Nommer les trois clients HTTP de Notification. Une ligne par adaptateur, et la clé d'API
   du courriel cesse de partir chez l'opérateur SMS.
2. Ajouter le contrôle d'appelant sur `OpenReview` et sur `CreateTipIntent`, et comparer le
   montant et le payeur sur une intention de pourboire déjà ouverte. Code d'hier, défauts
   d'hier.
3. Porter la raison du paiement dans les trois événements de Payment, et garder l'état dans
   `Delivery.MarkRefunded`. Les deux ensemble ferment le remboursement fantôme.
4. Rattacher la clé de rejeu de Billing au titulaire, des deux côtés — magasin applicatif et
   index.
5. Ne plus écrire le code OTP dans l'Outbox.

**Ensuite — ce qui perd des courses**

6. Filtrer la fraîcheur **avant** le plafond dans la recherche de proximité, et poser un
   TTL sur les clés de position. C'est une correction courte avec un effet immédiat et
   visible sur la carte du client comme sur les vagues.
7. Réserver un livreur **par livreur**, pas par mission, au moment où une vague le retient.
8. Lire la clé d'idempotence de l'acceptation d'offre : un livreur à qui on annonce qu'il a
   perdu une course qu'il a gagnée perd confiance une seule fois.
9. Vérifier le véhicule à la consommation du devis, et poser `xmin` sur les agrégats de
   Pricing.
10. Allonger le délai d'abandon des impayées, et publier l'événement de paiement échoué.

**Puis — la dette qui rendra tout le reste moins cher**

11. Un projet de tests d'autorisation par service, sur le modèle de Media. La majorité des
    constats des deux premières sections tombe à ce moment-là, et ne revient plus.
12. Une vue par acteur dans Ride et dans Rating, sur le modèle du mappeur de Delivery. La
    règle redescend dans le service, et la passerelle cesse d'être le seul endroit où elle
    vit.
13. Brancher OSRM, ou assumer le repli : aujourd'hui tout devis est dégradé et personne ne
    peut le savoir.

---

# Ce que cet audit n'a pas pu voir

- **Rien n'a été exécuté.** Les constats de concurrence — double consommation de devis,
  double acceptation, double notation — sont établis par l'absence de garde en base, pas
  par une course de vitesse observée.
- **Les tests n'ont pas été lancés**, seulement lus. Quand il est écrit « non couvert »,
  cela veut dire qu'aucun fichier ne nomme la règle, pas qu'un test échoue.
- **La topologie réseau effective n'a pas été vérifiée.** Plusieurs constats d'autorisation
  supposent qu'un appel direct à un service est possible depuis le réseau interne. Que ce
  soit difficile ne rend pas la règle appliquée : la matrice de visibilité dit elle-même
  que c'est le modèle de menace à retenir.
- **Les services tiers n'ont pas été interrogés.** Ce que FedaPay notifie réellement sur un
  remboursement fait au tableau de bord reste une question ouverte, et le code la pose
  lui-même.
- **Les consommateurs vivant hors de ce dépôt** — HBA Express, HBA Food — démentiraient
  peut-être deux constats de « topic sans consommateur ». Seul le déploiement le dirait.
