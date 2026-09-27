# Webhooks partenaires

## Ce qui est en place

`WebhookSender` signe et envoie un appel sortant : HMAC-SHA256 sur
`timestamp.corps`, en-tête `X-HBA-Signature` au format `t=…,v1=…`. Le partenaire
vérifie avec son secret, émis une seule fois à la création du client OAuth.

## Ce qui reste à faire, et pourquoi ce n'est pas fait

Il manque trois pièces, volontairement non écrites :

1. **La source des événements.** Le référentiel acteurs prévoit qu'au MVP
   HBA Express et HBA Food consomment Kafka directement au lieu des webhooks.
   Le consommateur `hba.delivery.events.v1` → webhook n'est donc pas câblé.
2. **Le registre des partenaires** (URL, secret, quotas) appartient au service
   Identity ; la Partner API devra le lire, pas le détenir.
3. **Le journal des envois consultable** et les relances avec backoff supposent
   une base propre à cette passerelle. Elle n'existe pas encore : aucun choix
   n'a été fait entre une base dédiée et une table dans Identity.

Tant que ces trois points ne sont pas tranchés, envoyer des webhooks depuis
cette passerelle reviendrait à inventer un comportement que le référentiel
ne décrit pas.
