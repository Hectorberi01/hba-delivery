# client_app — application client (Flutter)

## Ce que l'application fait

Demander un devis, créer et payer une livraison, la suivre, appeler le livreur,
annuler selon la politique d'annulation, consulter son historique.

Authentification : téléphone + OTP SMS, puis JWT. Rôle `customer`.

## Ce à quoi il faut faire attention

- **Le code de remise s'affiche ici.** Le client peut le lire et le transmettre
  au destinataire. Il disparaît dès que la course est close.
- **Le livreur n'apparaît qu'à partir de `DRIVER_ASSIGNED`** — nom, véhicule,
  téléphone — et jusqu'à la clôture. Le BFF renvoie des champs nuls avant et
  après : l'interface doit le gérer sans supposer leur présence.
- Un devis expire. L'écran doit le redemander plutôt que de créer une livraison
  avec un devis périmé, que le service refusera.

## Authentification

```
POST /api/client/v1/auth/otp/request   { phone, deviceId }
POST /api/client/v1/auth/otp/verify    { challengeId, code, deviceId, displayName }
POST /api/client/v1/auth/refresh       { refreshToken, deviceId }
POST /api/client/v1/auth/logout        { refreshToken }
```

`otp/request` répond la même chose quel que soit le numéro — inconnu, connu,
suspendu. C'est volontaire : sans cela, la route dirait à n'importe qui quels
numéros ont un compte chez HBA. L'application demande donc le nom APRÈS
vérification, à tout le monde, et l'ignore si le compte en a déjà un.

Le jeton d'accès dure quinze minutes, le jeton de rafraîchissement trente jours.
Ce dernier **ne sert qu'une fois** : chaque rafraîchissement en renvoie un
nouveau, qu'il faut stocker à la place de l'ancien. Rejouer un jeton déjà
consommé révoque toute la session — l'application doit donc écrire le nouveau
jeton avant de considérer l'opération terminée.

En développement, le code SMS est fixé à `000000`.

## Profil et adresses

```
GET    /api/client/v1/me
PUT    /api/client/v1/me              { displayName, email }
GET    /api/client/v1/addresses
POST   /api/client/v1/addresses       { label, latitude, longitude, landmark, phone, contactName, notes, setAsDefault }
PUT    /api/client/v1/addresses/{id}
DELETE /api/client/v1/addresses/{id}
```

La première adresse enregistrée devient automatiquement l'adresse principale.

## Démarrage

```bash
cd apps/client_app
flutter create --org com.hbatechettrade --project-name hba_client .
```

Base d'API en développement : `http://localhost:5101/api/client/v1`.
