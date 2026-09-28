#!/usr/bin/env bash
# Génère la migration initiale de chaque service qui possède une base.
#
# Ces migrations ne sont pas versionnées d'avance : un fichier de migration va
# de pair avec un instantané du modèle, et les écrire à la main revient à mentir
# à EF Core sur ce qu'il croit savoir de la base. Une seule commande les produit
# correctement.
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Le SDK .NET n'est pas dans le PATH. Sur macOS : brew install --cask dotnet-sdk" >&2
  exit 1
fi

if ! dotnet ef --version >/dev/null 2>&1; then
  echo "Installation de dotnet-ef…"
  dotnet tool install --global dotnet-ef
fi

NAME="${1:-Initial}"

# service : chemin
SERVICES=(
  "Identity:src/Services/Identity"
  "Directory:src/Services/Directory"
  "Delivery:src/Services/Delivery"
  "Notification:src/Services/Notification"
  "Pricing:src/Services/Pricing"
  "Payment:src/Services/Payment"
  "Driver:src/Services/Driver"
  "Dispatch:src/Services/Dispatch"
  "Media:src/Services/Media"
)

# CONTROLE PREALABLE, ET IL A UNE HISTOIRE. « dotnet ef » exige que le PROJET
# DE DEMARRAGE — l'API, pas l'infrastructure — reference
# Microsoft.EntityFrameworkCore.Design. Trois services de suite ont ete ecrits
# sans cette reference, et chaque fois l'erreur est tombee apres une
# compilation complete, loin de sa cause. Le dire ici coute une seconde.
manquants=()
for entry in "${SERVICES[@]}"; do
  svc="${entry%%:*}"
  path="${entry##*:}"
  api="$path/Hba.$svc.Api/Hba.$svc.Api.csproj"

  if [ -f "$api" ] && ! grep -q "EntityFrameworkCore.Design" "$api"; then
    manquants+=("$api")
  fi
done

if [ ${#manquants[@]} -gt 0 ]; then
  echo "Ces projets de demarrage ne referencent pas Microsoft.EntityFrameworkCore.Design :" >&2
  printf '  %s\n' "${manquants[@]}" >&2
  echo >&2
  echo "Ajoutez-y le bloc suivant avant </Project>, puis relancez :" >&2
  cat >&2 <<'BLOC'

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
BLOC
  exit 1
fi

for entry in "${SERVICES[@]}"; do
  svc="${entry%%:*}"
  path="${entry##*:}"

  echo
  echo "── $svc ──"

  # Le script doit pouvoir être relancé : si un service a déjà produit cette
  # migration lors d'un passage précédent, on le saute au lieu d'échouer et
  # d'arrêter les suivants.
  out="$path/Hba.$svc.Infrastructure/Persistence/Migrations"
  if compgen -G "$out/*_$NAME.cs" >/dev/null; then
    echo "Migration « $NAME » déjà présente — ignoré."
    continue
  fi

  dotnet ef migrations add "$NAME" \
    --project   "$path/Hba.$svc.Infrastructure" \
    --startup-project "$path/Hba.$svc.Api" \
    --output-dir Persistence/Migrations
done

echo
echo "Fait. Relisez chaque migration avant de la valider :"
echo "  - le schéma doit être celui du service, et lui seul ;"
echo "  - les index uniques filtrés doivent porter leur filtre ;"
echo "  - outbox_messages et inbox_messages doivent y figurer."
echo
echo "La colonne xmin apparaît dans le CreateTable du fichier C# : c'est normal."
echo "Npgsql retire les colonnes système au moment de générer le SQL"
echo "(NpgsqlMigrationsSqlGenerator.IsSystemColumn), le CREATE TABLE ne la"
echo "contient pas. Pour le vérifier : dotnet ef migrations script."
