# web — portail commerçant et back-office (Next.js)

Une seule application, deux espaces, un seul BFF.

## Authentification

```
POST /api/web/v1/auth/login              { login, password, deviceId }
POST /api/web/v1/auth/refresh            { refreshToken, deviceId }
POST /api/web/v1/auth/logout             { refreshToken }
GET  /api/web/v1/auth/me
POST /api/web/v1/auth/password           { currentPassword, newPassword }
POST /api/web/v1/auth/sessions/revoke-all
```

`login` accepte l'e-mail ou le téléphone : l'utilisateur n'a pas à se souvenir
de ce qu'il a fourni à l'inscription. Un identifiant inconnu et un mot de passe
faux donnent la même réponse.

Changer de mot de passe ferme toutes les autres sessions : l'interface doit
renvoyer vers l'écran de connexion.

## Espace commerçant

Rôles `merchant_owner` et `merchant_staff`. Base :
`http://localhost:5100/api/merchant/v1`.

Gérer ses points de collecte, suivre ses livraisons, marquer une commande prête,
remettre le colis, consulter l'historique.

```
GET  /api/merchant/v1/profile
PUT  /api/merchant/v1/profile
GET  /api/merchant/v1/pickup-points
POST /api/merchant/v1/pickup-points
PUT  /api/merchant/v1/pickup-points/{id}
POST /api/merchant/v1/pickup-points/{id}/active
```

Les horaires d'ouverture sont exprimés en minutes depuis minuit, par jour ISO
(1 = lundi). Deux plages le même jour décrivent une coupure de midi ; elles ne
peuvent pas se chevaucher. Un point de collecte sans horaire est considéré
ouvert.

`merchant_staff` a des droits réduits — préparation et remise. L'interface peut
masquer l'annulation, mais le service la refuse de toute façon : ne pas
s'appuyer sur le masquage pour la sécurité.

## Espace back-office

Rôles `admin`, `ops`, `support`, `finance`. Base :
`http://localhost:5100/api/admin/v1`.

Valider ou rejeter le KYC, suspendre un livreur, gérer zones et grilles,
superviser les courses, forcer une réaffectation ou une clôture, déclencher un
remboursement, créer les clients OAuth des partenaires, consulter les KPI.

```
GET  /api/admin/v1/merchants            ?query=&onlyActive=&pageSize=&offset=
POST /api/admin/v1/merchants
GET  /api/admin/v1/merchants/{id}
POST /api/admin/v1/accounts/back-office
POST /api/admin/v1/accounts/merchant
POST /api/admin/v1/accounts/{id}/suspend
POST /api/admin/v1/accounts/{id}/reactivate
POST /api/admin/v1/partners              (rôle admin)
POST /api/admin/v1/partners/{id}/rotate-secret  (rôle admin)
```

Trois choses à ne pas oublier dans l'interface :

- **Le motif est obligatoire** sur toute action sensible. Elle est auditée :
  auteur, date, motif, TraceId.
- **Aucun bouton « marquer comme livré ».** Un administrateur peut clore en
  `FAILED` ou `CANCELLED`, jamais en `DELIVERED`. Le service refuse ;
  l'interface ne doit même pas le proposer.
- **Les secrets partenaires ne s'affichent qu'une fois.** La création d'un
  client OAuth renvoie le `clientSecret` et le `webhookSigningSecret` ; ils ne
  sont plus jamais relisibles. L'écran doit le dire clairement et proposer de
  les copier avant de fermer.

## Démarrage

```bash
cd apps/web
cp .env.example .env.local     # HBA_GATEWAY_URL pointe sur la passerelle
npm install
npm run dev                    # http://localhost:3000
```

La passerelle doit tourner : `make up` puis `make services-up` à la racine du
dépôt. Sans elle, la connexion échoue sur « La passerelle est injoignable ».

## Comment la console parle à la passerelle

Le navigateur ne détient **aucun jeton**. Il appelle `/api/hba/<chemin>`, un
gestionnaire de route Next qui pose le `Bearer` côté serveur et relaie vers la
passerelle. Les deux jetons vivent dans des cookies `httpOnly` : un script
injecté dans la page ne peut pas les recopier.

- `POST /api/session/connexion` — échange login et mot de passe contre la paire
  de jetons, puis vérifie que le compte porte un rôle de back-office. Cette
  vérification n'est qu'une **explication** : un commerçant a un compte valide,
  et sans ce message il se connecterait à une console qui lui refuserait chaque
  écran sans dire pourquoi. Ce qui protège, c'est que chaque service vérifie le
  JWT (ADR 0007).
- `POST /api/session/deconnexion` — révoque le jeton de rafraîchissement chez
  Identity, puis efface les cookies.
- `/api/hba/{admin|web|merchant}/v1/...` — procuration. Sur un 401 elle
  rafraîchit **une** fois et rejoue. Trois préfixes seulement : une procuration
  qui colle un jeton d'administrateur sur n'importe quel chemin est un député
  confus.

Un troisième cookie, `hba_principal`, est lisible par le navigateur. Il porte le
nom et les rôles, sert à dessiner l'interface, et n'accorde rien.

## Ce que l'API ne sait pas encore servir

Les écrans nomment ces manques à l'endroit où la donnée devrait être, plutôt
que d'afficher des valeurs de démonstration. Une maquette remplie de faux
chiffres finit toujours par être lue comme une vraie mesure.

| Écran | Ce qui manque | Route à ouvrir |
|---|---|---|
| Tableau de bord | Objectifs mensuels | *décision produit* — aucune source, où vivent les objectifs ? |
| Tableau de bord | Niveaux de service Express / Standard / Premium | *n'existe pas* — le domaine n'a pas de paliers |
| Tableau de bord | Comparaison par zone | *n'existe pas* — Driver connaît des positions, pas des secteurs |
| Livreurs | Note du livreur, zone principale, couverture | *aucune* — ces notions n'existent dans aucun contrat |
| Clients | Annuaire et fiche client | `GET /api/admin/v1/customers[/{id}]` |
| Paramètres | Général, tarification, zones, notifications | `GET / PUT /api/admin/v1/...` |
| Connexion | Réinitialisation du mot de passe | `POST /api/web/v1/auth/password/forgot` et `/reset` |

Les indicateurs sont servis depuis : `GET /api/admin/v1/kpi` appelle Delivery,
Payment, Dispatch et Driver **en parallèle** et rend un seul objet, plus la
liste des blocs indisponibles. Chaque bloc reste joignable seul —
`/kpi/deliveries`, `/kpi/payments`, `/kpi/dispatch`, `/kpi/drivers`. La
fenêtre est résolue par la passerelle et imposée aux quatre, pour que les
quatre blocs couvrent la même période (ADR 0019 et 0020).

`GET /api/admin/v1/drivers` a été ajouté depuis : nouveau RPC `ListDrivers`
dans `driver_service.proto`, requête et handler dans Driver, route dans la
passerelle. La lecture est ouverte au back-office, l'écriture — KYC,
suspension — reste réservée au rôle admin : le support a besoin de retrouver un
livreur pour répondre à un client, pas de valider son dossier.

Deux de ces manques ne sont **pas** de simples oublis techniques :

- l'annuaire client touche à des données personnelles — qui, du support ou de
  la finance, a le droit de lire quoi n'est pas tranché dans le référentiel ;
- la réaffectation d'une course (`ForceReassign`) répond UNIMPLEMENTED tant que
  la politique d'annulation, point 3 des points à trancher, ne l'est pas. La
  console l'écrit à l'écran plutôt que d'afficher un bouton qui échouerait.

## Décisions visibles dans le code

- **Les chiffres du tableau de bord portent sur une page de résultats**, pas sur
  la base, et l'écran le dit avant de les montrer. `src/lib/agregats.ts`
  disparaîtra le jour où un service exposera de vrais indicateurs.
- **Les énumérations protobuf arrivent en entiers.** La passerelle sérialise les
  messages protobuf avec System.Text.Json, qui rend un enum C# sous forme de
  nombre. `src/lib/statuts.ts` accepte les deux formes.
- **Couleurs de marque et couleurs de donnée sont deux familles distinctes.** Le
  teal habille l'interface et ne code jamais une valeur ; la palette de séries
  est validée pour les daltonismes sur fond blanc. Partout où une teinte passe
  sous 3:1 face au blanc, la valeur est écrite en toutes lettres à côté.
- **Aucun bouton « marquer comme livré ».** Seule la remise avec l'OTP du
  destinataire produit DELIVERED (ADR 0005).
- **Le motif est exigé avant l'envoi** sur toute action sensible, et l'écran dit
  qu'il est conservé avec le nom et la date.
