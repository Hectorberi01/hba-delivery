# Migrations EF Core — Notification

```bash
dotnet ef migrations add Initial \
  --project src/Services/Notification/Hba.Notification.Infrastructure \
  --startup-project src/Services/Notification/Hba.Notification.Api \
  --output-dir Persistence/Migrations
```

Le journal des envois n'a pas de politique de purge. Il grossit d'une ligne par
SMS : à quelques milliers de courses par mois, ce n'est pas un problème avant
longtemps, mais ce n'en est pas moins un sujet à trancher.
