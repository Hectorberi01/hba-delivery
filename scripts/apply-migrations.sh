#!/usr/bin/env bash
# Applique les migrations existantes a la base de developpement.
#
# POURQUOI CE SCRIPT EXISTE. En Production, Database:AutoMigrate vaut false :
# un service ne doit pas modifier le schema de sa base au demarrage. C'est le
# bon comportement, mais il veut dire que les migrations doivent etre appliquees
# par quelqu'un. En developpement, c'est ce script.
#
# Il vise « localhost » via appsettings.Development.json, donc les ports publies
# par « make up ». Il n'a rien a voir avec un deploiement.
set -euo pipefail

export ASPNETCORE_ENVIRONMENT=Development

# CE SCRIPT VISE « localhost », donc les ports PUBLIES. L'infrastructure de
# developpement (« make up ») publie 5432 ; celle de production
# (« make infra-up ») ne publie rien, par construction. Le dire tout de suite
# evite huit echecs de connexion et un diagnostic a rallonge.
if ! (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null; then
  echo "Rien n'ecoute sur 127.0.0.1:5432." >&2
  echo >&2
  echo "Cause la plus frequente : l'infrastructure demarree est celle de" >&2
  echo "production (make infra-up / make prod-up), dont Postgres ne publie" >&2
  echo "aucun port. En local, utilisez :" >&2
  echo >&2
  echo "  make down && docker compose -f deploy/compose.infra.yml down" >&2
  echo "  make up          # infra de developpement, publie 5432" >&2
  echo "  make migrate" >&2
  echo "  make services-up # tous les services, sans retoucher l'infra" >&2
  exit 1
fi
applied=0
skipped=()

for path in src/Services/*/; do
  svc=$(basename "$path")
  infra="$path/Hba.$svc.Infrastructure"
  api="$path/Hba.$svc.Api"
  migrations="$infra/Persistence/Migrations"

  if ! compgen -G "$migrations/*_*.cs" >/dev/null 2>&1; then
    skipped+=("$svc")
    continue
  fi

  echo "-- $svc"

  # « --no-build » LIT LE bin/, PAS LES SOURCES. C'est ce qui rend l'appel
  # rapide, et c'est aussi son piege : une migration ajoutee depuis la
  # derniere compilation n'est pas dans l'assembly, donc EF ne voit aucun
  # instantane, conclut que le modele a des changements en attente, et echoue
  # sur PendingModelChangesWarning — en accusant le modele alors que le
  # fichier de migration est parfaitement correct a cote.
  #
  # On compare donc l'assembly aux fichiers de migration avant de s'en
  # remettre au cache.
  dll=$(ls -t "$infra"/bin/*/net*/"Hba.$svc.Infrastructure.dll" 2>/dev/null | head -1)
  recent=$(ls -t "$migrations"/*.cs 2>/dev/null | head -1)

  if [ -n "$dll" ] && [ -n "$recent" ] && [ "$dll" -nt "$recent" ]; then
    dotnet ef database update --project "$infra" --startup-project "$api" --no-build
  else
    # LES DEUX FLUX SONT MUETS sur cette tentative, pas seulement stderr :
    # EF ecrit son exception sur la sortie standard. Sans cela, un echec
    # attendu — et immediatement rattrape par la compilation qui suit —
    # remplit le terminal d'une pile d'appels alarmante pour rien.
    echo "   (assembly plus ancien que les migrations : compilation)"
    dotnet ef database update --project "$infra" --startup-project "$api" --no-build >/dev/null 2>&1 \
      || dotnet ef database update --project "$infra" --startup-project "$api"
  fi

  applied=$((applied + 1))
done

echo
echo "Migrations appliquees pour $applied service(s)."
if [ ${#skipped[@]} -gt 0 ]; then
  echo "Sans migration, donc ignores : ${skipped[*]}"
  echo "Ce n'est pas une erreur : ces services n'ont pas encore de schema."
fi
