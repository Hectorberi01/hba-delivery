# Migrations EF Core — Identity

```bash
dotnet ef migrations add Initial \
  --project src/Services/Identity/Hba.Identity.Infrastructure \
  --startup-project src/Services/Identity/Hba.Identity.Api \
  --output-dir Persistence/Migrations
```

Points à vérifier sur la migration générée :

- schéma `identity`, et lui seul ;
- `xmin` apparaît dans le `CreateTable` du fichier C# : c'est normal, Npgsql
  retire les colonnes système à la génération du SQL, le `CREATE TABLE` ne la
  contient pas (`dotnet ef migrations script` pour s'en assurer) ;
- index uniques filtrés sur `phone`, `email`, `driver_id` et `client_id` ;
- `refresh_tokens.token_hash` unique.

## Amorçage du premier administrateur

Aucun compte n'existe au premier démarrage, et `CreateBackOfficeAccount` exige
déjà le rôle `admin` : personne ne peut créer le premier. Le plus simple, une
fois la migration appliquée, est d'insérer ce compte à la main :

```sql
INSERT INTO identity.accounts
  (id, email, "DisplayName", roles, "Status", password_hash, "CreatedAt")
VALUES (gen_random_uuid(), 'admin@hbatechettrade.com', 'Administrateur',
        'admin', 1, '<empreinte PBKDF2>', now());
```

L'empreinte se produit avec `PasswordHash.FromPlainText`. Une commande CLI
d'amorçage serait plus propre : elle reste à écrire.
