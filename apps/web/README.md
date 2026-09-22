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
`http://localhost:5103/api/merchant/v1`.

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
`http://localhost:5103/api/admin/v1`.

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
npx create-next-app@latest . --typescript --app --eslint
```
