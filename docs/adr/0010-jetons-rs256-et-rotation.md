# 0010 — Jetons RS256, rafraîchissement à usage unique

Statut : acceptée — septembre 2026

## Contexte

Sept services doivent vérifier les jetons qu'Identity émet. Deux options :

- **HS256**, clé symétrique partagée. Simple, mais chaque service détient de quoi
  fabriquer un jeton : un seul service compromis, et tout l'est.
- **RS256**, clé asymétrique. Identity signe ; les autres vérifient avec la clé
  publique, qu'ils récupèrent eux-mêmes.

Par ailleurs, un jeton d'accès n'est pas révocable : une fois émis, il vaut
jusqu'à son expiration.

## Décision

- **RS256.** Identity expose `/.well-known/openid-configuration` et
  `/.well-known/jwks.json` ; chaque service les lit via son middleware JwtBearer,
  avec son `Authority`. Aucun secret partagé.
- **Jeton d'accès court** : quinze minutes. C'est ce qui rend une suspension
  effective sans mécanisme de révocation.
- **Jeton de rafraîchissement long** : trente jours, parce qu'un livreur ne doit
  pas se reconnecter tous les matins. Seule son empreinte SHA-256 est stockée.
- **Rotation à usage unique.** S'en servir le consomme et en émet un nouveau,
  dans la même chaîne de session. Un jeton déjà consommé qui revient signale une
  copie : toute la chaîne est révoquée et l'utilisateur se reconnecte.
- **La clé privée vit hors du conteneur**, dans un volume. Une clé générée au
  démarrage invaliderait tous les jetons à chaque déploiement, et différerait
  d'une instance à l'autre. La génération automatique n'est autorisée qu'en
  développement, et elle le journalise.

## Conséquences

- Un service compromis ne peut pas forger de jeton.
- La rotation des clés est possible sans coupure : les anciennes clés publiques
  restent publiées dans les JWKS le temps que les jetons émis avec elles
  expirent.
- Une suspension de compte révoque aussi les sessions, sinon la personne
  travaillerait jusqu'à l'expiration de son jeton d'accès.
- Ce qui reste à faire : les jetons de service à service, pour les appels sans
  utilisateur final — un planificateur, par exemple. Voir l'ADR 0007.
