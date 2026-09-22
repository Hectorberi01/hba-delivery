# 0014 — Le pilote envoie les codes par email, sauf celui du destinataire

Statut : **proposée** — septembre 2026. En attente de décision.

Tranche le point 10 de `architecture/points-a-trancher.md`, de façon
délibérément provisoire.

## Contexte

Le service Notification est écrit sans adaptateur réel. Hors développement,
`Sms:Provider` vaut `none` et chaque envoi est consigné en « ignoré ». Personne
ne peut donc se connecter : l'OTP est le seul moyen d'authentification du
client et du livreur.

Trois voies avaient été étudiées.

| Voie | Par message | Trois messages |
|---|---|---|
| WhatsApp (gabarit *authentication*, Bénin) | ≈ 2,3 F | ≈ 7 F |
| SMS, accord direct MTN / Moov | 10–25 F | 30–75 F |
| SMS, agrégateur international | ≈ 169 F | ≈ 507 F |

WhatsApp est de loin le moins cher, mais Meta impose un **opt-in préalable** à
tout message de gabarit, et exige la vérification du compte Business et
l'approbation des gabarits. Un accord direct avec MTN ou Moov demande une
négociation et l'enregistrement du nom d'expéditeur auprès de l'ARCEP. Les deux
se comptent en semaines, pas en jours.

L'email, lui, ne coûte rien, ne demande ni contrat ni opt-in opérateur, et se
met en route en une journée : un domaine, SPF, DKIM, DMARC, un expéditeur
transactionnel.

Mais le catalogue de modèles distingue deux codes, et un seul des deux peut
partir par email.

## Décision

**`otp_login` part par email.** La personne qui se connecte a un compte : son
adresse est connue, elle l'a fournie, elle est vérifiable.

**Le téléphone reste l'identifiant du compte.** L'email devient un canal de
remise obligatoire pour les comptes en libre-service, pas une nouvelle clé.
`Account` garde son index unique sur le numéro, `RequestOtp` continue de
prendre un numéro. Le contrat `identity/v1` ne change pas.

**`delivery_otp` ne part pas par email.** Il est destiné au **destinataire**,
qui n'a pas de compte HBA, pas d'adresse dans le modèle, et n'a jamais rien
accepté. L'expéditeur le désigne par un numéro de téléphone, jamais par un
email : il n'y a rien à quoi écrire.

**Pendant le pilote, le code de remise est communiqué à l'expéditeur**, dans
l'écran de suivi de sa course et dans l'email de confirmation. C'est lui qui le
transmet au destinataire, par le moyen qu'il veut — un appel, son propre
WhatsApp, de vive voix.

**`delivery_assigned` et `delivery_completed` partent par email** à
l'expéditeur, qui a un compte.

## Conséquences

- **La preuve de livraison est conservée.** Le livreur demande toujours un code
  au destinataire, et ce code reste la preuve (ADR 0005). Seul le chemin
  d'acheminement change : il passe par l'expéditeur au lieu de partir
  directement.

- **La chaîne est plus faible, et il faut le dire.** Un expéditeur qui ne
  transmet pas le code bloque sa propre livraison. Le pilote mesurera combien
  de courses échouent pour cette raison ; c'est le chiffre qui justifiera — ou
  non — la dépense d'un canal direct vers le destinataire.

- **Le livreur doit avoir un email et le relever.** Pour une flotte pilote
  recrutée en direct, c'est tenable. À l'échelle, ce ne l'est pas : un coursier
  ne consulte pas sa boîte entre deux courses. C'est la limite qui datera cette
  décision.

- **L'email devient obligatoire à l'inscription en libre-service**, alors qu'il
  est aujourd'hui facultatif. Changement de validation dans Identity et sur les
  trois BFF, sans migration de schéma : la colonne existe déjà.

- **`NotificationChannel` gagne une valeur `Email`**, et les gabarits une
  variante email. Les textes SMS restent en place, sans accent : ils
  resserviront tels quels.

- **La contrainte des 160 caractères disparaît pour l'email** — mais elle est
  conservée sur les gabarits SMS, qui redeviendront le canal principal.

- **Décision explicitement provisoire.** Elle sera remplacée par une ADR
  ultérieure dès qu'un canal direct vers le destinataire existe. Le candidat le
  plus probable reste WhatsApp pour le volume et le SMS en repli, l'opt-in du
  destinataire étant le seul obstacle sérieux à lever.

## Ce qui a été écarté

- **Supprimer le code de remise pendant le pilote** : la preuve de livraison
  disparaîtrait, et avec elle le seul recours en cas de litige. L'ADR 0005 y
  perdrait son objet.
- **Faire de l'email l'identifiant du compte** : cela réécrit l'identité, le
  contrat `identity/v1` et les trois BFF, pour un gain nul — l'email n'est ici
  qu'un canal.
- **Attendre WhatsApp ou un accord opérateur avant le pilote** : plusieurs
  semaines d'attente pour un problème que l'email résout à 90 %.
