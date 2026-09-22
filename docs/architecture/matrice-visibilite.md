# Matrice de visibilité — où elle est réellement appliquée

La matrice du référentiel n'est pas qu'un tableau de documentation : elle est
implémentée à deux endroits précis du service Delivery, et nulle part ailleurs.

| Donnée | Client | Destinataire | Livreur | Commerçant | Partenaire | Admin |
|---|---|---|---|---|---|---|
| Prix et détail du devis | Oui | Non | Sa rémunération seulement | Si donneur d'ordre | Oui | Oui |
| Téléphone du livreur | Pendant la mission | Par SMS pendant la mission | — | Pendant la mission | Non | Oui |
| Adresse de destination | Oui | Oui | Après acceptation | Oui | Oui | Oui |
| OTP de remise | Oui | Oui | Non (il le saisit) | Non | Non | Non |
| Documents KYC livreur | Non | Non | Les siens | Non | Non | Oui (URL signée) |

## Les deux points d'application

### 1. `DeliveryAccess` — le périmètre

`src/Services/Delivery/Hba.Delivery.Application/Authorization/DeliveryAccess.cs`

Décide **ce que l'appelant a le droit de voir du tout** :

- `ScopeFor` impose le filtre d'une liste. Le client ne peut pas demander les
  livraisons d'un autre : le `CustomerId` du filtre est écrasé par celui du
  jeton, quoi que l'appelant ait envoyé.
- `EnsureCanRead` échoue en **NotFound** et non en Forbidden hors périmètre. Un
  403 confirmerait l'existence de la livraison à quelqu'un qui n'a rien à en
  savoir.

### 2. `DeliveryViewMapper` — le contenu

`src/Services/Delivery/Hba.Delivery.Application/Views/DeliveryViewMapper.cs`

Décide **ce que l'appelant voit de la livraison**. C'est le seul endroit du
service où un agrégat devient un DTO, et il n'existe pas de version « complète »
du DTO qui pourrait circuler par erreur : les champs interdits valent `null`.

- l'OTP n'est renseigné que pour le client donneur d'ordre, et seulement tant
  que la course est ouverte ;
- le livreur reçoit `DriverEarningXof` et jamais `Pricing` ;
- l'adresse de destination et le destinataire sont nuls pour un livreur non
  affecté ;
- le téléphone du livreur n'apparaît que pendant la mission, pour le client et
  le commerçant.

Le destinataire n'apparaît pas dans cette matrice côté API : **ce n'est pas un
utilisateur authentifié**. Il reçoit l'OTP et l'adresse par SMS ou WhatsApp, via
le service Notification. Aucun compte ne doit lui être créé implicitement.

## Ce qui est volontairement absent

Les BFF ne filtrent rien. Ils traduisent REST en gRPC et remontent la réponse
telle quelle. Si un BFF se mettait à masquer des champs, il y aurait deux
sources de vérité pour la même règle — et celle du BFF serait contournable en
appelant le service directement depuis le réseau interne.
