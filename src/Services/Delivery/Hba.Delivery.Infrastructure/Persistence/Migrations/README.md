# Migrations EF Core — Delivery

Aucune migration n'est versionnée pour l'instant : la première doit être générée
sur un poste disposant du SDK .NET 9 et de `dotnet-ef`.

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial \
  --project src/Services/Delivery/Hba.Delivery.Infrastructure \
  --startup-project src/Services/Delivery/Hba.Delivery.Api \
  --output-dir Persistence/Migrations
```

Points à vérifier sur la migration générée :

- la colonne `xmin` apparaît dans le `CreateTable` du fichier C# : c'est normal.
  Npgsql retire les colonnes système à la génération du SQL, le `CREATE TABLE`
  ne la contient pas (`dotnet ef migrations script` pour s'en assurer) ;
- le schéma doit être `delivery` et lui seul ;
- l'index unique `(PartnerId, ExternalOrderId)` doit bien porter le filtre
  `"ExternalOrderId" IS NOT NULL`.
