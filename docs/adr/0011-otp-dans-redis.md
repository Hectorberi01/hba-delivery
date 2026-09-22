# 0011 — Les codes SMS vivent dans Redis, pas en base

Statut : acceptée — septembre 2026

## Contexte

Un code de connexion dure cinq minutes. Le mettre en base coûterait une écriture
durable, une transaction, un index et une purge, pour une donnée qui n'a plus
aucune valeur dix minutes plus tard.

Se pose aussi la question du transport : Identity ne parle à aucun opérateur
téléphonique.

## Décision

- **Le défi vit dans Redis**, avec une durée de vie qui expire toute seule.
- **Seule l'empreinte du code est stockée**, en HMAC-SHA256 avec un poivre de
  configuration. Sans poivre, six chiffres se retrouvent par force brute en une
  fraction de seconde. L'empreinte dépend aussi de l'identifiant du défi : un
  code intercepté ne se rejoue pas sur une autre demande.
- **Cinq tentatives par défi**, puis il faut en redemander un.
- **Un envoi par minute et par numéro**, compté dans Redis. Un SMS coûte de
  l'argent, et un numéro qu'on bombarde est un numéro qu'on harcèle.
- **L'envoi passe par l'Outbox**, sous forme de `NotificationCommand` sur
  `hba.notification.commands.v1`. Le code ne transite PAS par le topic des
  événements d'identité, que d'autres services consomment.

## Conséquences

- Redis devient une dépendance d'Identity. S'il tombe, plus personne ne se
  connecte par SMS — mais le portail web, qui utilise un mot de passe, continue.
- Le code n'est jamais relisible, contrairement à l'OTP de remise d'un colis,
  que le client doit voir (ADR 0005). Les deux se ressemblent et n'ont pas du
  tout les mêmes contraintes : ne pas les confondre.
- En développement, un code fixe évite d'aller le chercher dans les journaux à
  chaque connexion. La configuration qui l'active est explicitement nommée
  `FixedCodeForDevelopment` et n'a aucune valeur par défaut en production.
