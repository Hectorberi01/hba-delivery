# Applications

Trois applications, aucune n'est encore échafaudée : ce dossier ne contient que
ce qu'il faut savoir avant de lancer `flutter create` ou `create-next-app`.

Elles vivent ici pour que le dépôt donne la vue complète du système. Rien
n'empêche de les sortir dans leurs propres dépôts plus tard : elles ne
dépendent du backend que par les contrats.

| Dossier | Technologie | Passe par | Public |
|---|---|---|---|
| `client_app/` | Flutter | Client BFF | Client donneur d'ordre |
| `driver_app/` | Flutter | Driver BFF | Livreur indépendant |
| `web/` | Next.js | Web BFF | Commerçant et back-office |

Aucune application ne parle gRPC ni Kafka. Elles consomment du REST/JSON via
leur BFF, derrière Traefik.

## Code généré depuis les contrats

`contracts/buf.gen.yaml` prévoit une génération Dart et TypeScript dans
`apps/_generated/`. À lancer depuis `contracts/` :

```bash
buf generate
```

Ce code n'est pas versionné : il se régénère.
