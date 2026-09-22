# Migrations EF Core — Directory

```bash
dotnet ef migrations add Initial \
  --project src/Services/Directory/Hba.Directory.Infrastructure \
  --startup-project src/Services/Directory/Hba.Directory.Api \
  --output-dir Persistence/Migrations
```

Points à vérifier :

- schéma `directory` ;
- `customers.phone` unique ;
- `customer_favorite_addresses` et `merchant_pickup_points` avec une clé
  étrangère en cascade vers leur propriétaire ;
- `merchant_pickup_points.opening_hours` en texte, au format
  `1:480-1200;2:480-1200`.
