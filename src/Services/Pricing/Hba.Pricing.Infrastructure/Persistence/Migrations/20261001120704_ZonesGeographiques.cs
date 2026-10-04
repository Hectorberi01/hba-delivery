using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Pricing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Le contour des zones, en base et nulle part ailleurs (point 40).
    /// </summary>
    ///
    /// <remarks>
    /// EF A GENERE CETTE MIGRATION VIDE, ET C'EST NORMAL : la colonne « area »
    /// n'est PAS dans le modele EF, a dessein. Le polygone n'est pas une propriete
    /// de l'agregat Zone — le domaine ne fait pas de geometrie et les tests
    /// d'architecture lui interdisent la dependance qui le permettrait — donc
    /// aucune difference de modele a detecter. Le SQL est ecrit a la main.
    ///
    /// CONSEQUENCE A CONNAITRE : « dotnet ef migrations add » ne verra jamais cette
    /// colonne. Elle ne sera jamais supprimee par une migration generee, et jamais
    /// non plus recreee : elle n'existe que par ce fichier.
    /// </remarks>
    public partial class ZonesGeographiques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // L'EXTENSION EST NORMALEMENT DEJA LA, et cette ligne couvre le cas ou
            // elle ne l'est pas. deploy/postgres/init-databases.sh la pose dans
            // chaque base — mais il ne tourne QU'AU PREMIER demarrage du volume :
            // une base creee plus tard par la migration automatique d'EF n'aurait
            // pas l'extension, et la colonne ci-dessous echouerait sur un type
            // inconnu.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");

            // geography ET NON geometry, ET LE CHOIX A DES CONSEQUENCES. geography
            // raisonne en metres sur un ellipsoide : un contour trace a Cotonou s'y
            // mesure juste sans projection locale. En echange, moins de fonctions y
            // sont definies — ST_Contains n'en fait pas partie, d'ou le ST_Covers de
            // PostgisZoneStore.
            //
            // NULLABLE, ET ELLE DOIT L'ETRE. Une zone peut exister avant d'etre
            // dessinee ; sans contour, ST_Covers ne la retient jamais, donc elle ne
            // s'applique a personne. C'est le comportement sur — l'inverse d'une
            // zone qui attraperait tout.
            migrationBuilder.Sql(
                "ALTER TABLE pricing.zones ADD COLUMN IF NOT EXISTS area geography(Polygon, 4326);");

            // SANS INDEX GiST, LA RECHERCHE DE ZONE BALAIE LA TABLE A CHAQUE DEVIS.
            // Elle est sur le chemin de tout prix demande : dix zones aujourd'hui,
            // mais c'est l'appel le plus frequent du service.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_zones_area\" ON pricing.zones USING GIST (area);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("DROP INDEX IF EXISTS pricing.\"IX_zones_area\";");
            migrationBuilder.Sql("ALTER TABLE pricing.zones DROP COLUMN IF EXISTS area;");

            // L'EXTENSION N'EST PAS RETIREE, ET CE N'EST PAS UN OUBLI. DROP EXTENSION
            // echouerait si une autre table l'utilisait, et reussirait en detruisant
            // ses colonnes sinon — dans les deux cas, une migration qui recule sur
            // Pricing n'a pas a decider du sort d'une extension partagee par la base.
        }
    }
}
