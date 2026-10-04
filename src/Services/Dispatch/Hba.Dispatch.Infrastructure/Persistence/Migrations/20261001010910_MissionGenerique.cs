using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Dispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MissionGenerique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dispatches_delivery_id",
                schema: "dispatch",
                table: "dispatches");

            migrationBuilder.RenameColumn(
                name: "delivery_id",
                schema: "dispatch",
                table: "dispatches",
                newName: "reference_id");

            migrationBuilder.AddColumn<int>(
                name: "service_type",
                schema: "dispatch",
                table: "dispatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "vehicle_type",
                schema: "dispatch",
                table: "dispatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // REPRISE DES LIGNES EXISTANTES, AJOUTEE A LA MAIN.
            //
            // EF pose « service_type » a zero, c'est-a-dire Unspecified — une valeur
            // que DispatchAggregate.Start REFUSE. Toute recherche en cours au moment
            // du deploiement serait devenue illisible par le code qui la relit : plus
            // aucune offre n'aurait pu partir, et le planificateur aurait echoue a
            // chaque balayage. Tout ce qui existe avant cette migration est une
            // livraison, par construction : le transport de personnes n'existait pas.
            //
            // AVANT L'INDEX, ET L'ORDRE COMPTE. L'index unique porte sur
            // (service_type, reference_id) : le creer sur des lignes toutes a zero
            // passerait aujourd'hui — les identifiants diffèrent — mais laisserait la
            // table dans un etat que le domaine refuse.
            migrationBuilder.Sql(
                "UPDATE dispatch.dispatches SET service_type = 1 WHERE service_type = 0;");

            // LE DEFAUT A ZERO EST RETIRE : le modele n'en declare aucun pour la
            // nature, justement pour qu'une insertion qui l'oublie echoue au lieu de
            // passer en silence. « vehicle_type », lui, GARDE son defaut — non precise
            // veut dire « n'importe lequel », et c'est le cas de toutes les
            // livraisons.
            migrationBuilder.Sql(
                "ALTER TABLE dispatch.dispatches ALTER COLUMN service_type DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_dispatches_mission",
                schema: "dispatch",
                table: "dispatches",
                columns: new[] { "service_type", "reference_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dispatches_mission",
                schema: "dispatch",
                table: "dispatches");

            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "dispatch",
                table: "dispatches");

            migrationBuilder.DropColumn(
                name: "vehicle_type",
                schema: "dispatch",
                table: "dispatches");

            migrationBuilder.RenameColumn(
                name: "reference_id",
                schema: "dispatch",
                table: "dispatches",
                newName: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "IX_dispatches_delivery_id",
                schema: "dispatch",
                table: "dispatches",
                column: "delivery_id",
                unique: true);
        }
    }
}
