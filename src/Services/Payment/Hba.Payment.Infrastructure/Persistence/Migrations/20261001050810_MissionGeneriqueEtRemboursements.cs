using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MissionGeneriqueEtRemboursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_intents_delivery_id",
                schema: "payment",
                table: "payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_driver_ledger_entries_delivery_id",
                schema: "payment",
                table: "driver_ledger_entries");

            migrationBuilder.RenameColumn(
                name: "delivery_id",
                schema: "payment",
                table: "payment_intents",
                newName: "reference_id");

            migrationBuilder.RenameColumn(
                name: "delivery_id",
                schema: "payment",
                table: "driver_ledger_entries",
                newName: "reference_id");

            // CORRIGE A LA MAIN. EF avait ecrit un DropColumn suivi d'un AddColumn :
            // il n'a pas reconnu le renommage de « delivery_reference » en
            // « reference_label », alors qu'il a reconnu celui de « delivery_id ».
            // Applique tel quel, il aurait EFFACE TOUTES LES REFERENCES LISIBLES du
            // grand livre — le releve de chaque chauffeur aurait affiche des UUID a
            // la place de « HBA-2026-00123 » pour tous ses gains passes.
            migrationBuilder.RenameColumn(
                name: "delivery_reference",
                schema: "payment",
                table: "driver_ledger_entries",
                newName: "reference_label");

            // La colonne passe de 32 a 64 caracteres : une reference de course n'en
            // a pas, et c'est l'identifiant complet qui s'y inscrit en attendant.
            migrationBuilder.AlterColumn<string>(
                name: "reference_label",
                schema: "payment",
                table: "driver_ledger_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<int>(
                name: "service_type",
                schema: "payment",
                table: "payment_intents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "service_type",
                schema: "payment",
                table: "driver_ledger_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // REPRISE DES LIGNES EXISTANTES, AJOUTEE A LA MAIN.
            //
            // EF pose « service_type » a zero, c'est-a-dire Unspecified — une valeur
            // que les agregats REFUSENT. Toute intention de paiement deja en base
            // serait devenue illisible par le code qui la relit, et aucun paiement en
            // cours n'aurait pu etre confirme. Tout ce qui existe avant cette
            // migration est une livraison, par construction : le service de transport
            // de personnes n'existait pas.
            migrationBuilder.Sql(
                "UPDATE payment.payment_intents SET service_type = 1 WHERE service_type = 0;");

            // LES VERSEMENTS RESTENT A « NON PRECISE », ET C'EST VOULU : un versement
            // solde un cumul, pas une mission. Seules les remunerations (kind = 1)
            // portent une nature. Reprendre toute la table aurait attribue une
            // livraison a des lignes qui n'en ont jamais eu.
            migrationBuilder.Sql(
                "UPDATE payment.driver_ledger_entries SET service_type = 1 WHERE kind = 1;");

            // LE DEFAUT A ZERO EST RETIRE, parce que le modele n'en declare aucun.
            // Le laisser rendrait silencieuse une insertion qui oublie la nature,
            // alors que les agregats la refusent justement a la creation.
            migrationBuilder.Sql(
                "ALTER TABLE payment.payment_intents ALTER COLUMN service_type DROP DEFAULT;");
            migrationBuilder.Sql(
                "ALTER TABLE payment.driver_ledger_entries ALTER COLUMN service_type DROP DEFAULT;");

            migrationBuilder.CreateTable(
                name: "refunds_due",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount_collected = table.Column<long>(type: "bigint", nullable: false),
                    recommended_refund = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_amount = table.Column<long>(type: "bigint", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds_due", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_service_type_reference_id",
                schema: "payment",
                table: "payment_intents",
                columns: new[] { "service_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ux_driver_ledger_mission",
                schema: "payment",
                table: "driver_ledger_entries",
                columns: new[] { "service_type", "reference_id" },
                unique: true,
                filter: "kind = 1 AND reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_due_status_created_at",
                schema: "payment",
                table: "refunds_due",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_refunds_due_intention",
                schema: "payment",
                table: "refunds_due",
                column: "payment_intent_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refunds_due",
                schema: "payment");

            migrationBuilder.DropIndex(
                name: "IX_payment_intents_service_type_reference_id",
                schema: "payment",
                table: "payment_intents");

            migrationBuilder.DropIndex(
                name: "ux_driver_ledger_mission",
                schema: "payment",
                table: "driver_ledger_entries");

            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "payment",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "payment",
                table: "driver_ledger_entries");

            migrationBuilder.RenameColumn(
                name: "reference_id",
                schema: "payment",
                table: "payment_intents",
                newName: "delivery_id");

            migrationBuilder.RenameColumn(
                name: "reference_id",
                schema: "payment",
                table: "driver_ledger_entries",
                newName: "delivery_id");

            // LE RETOUR EST UN RENOMMAGE AUSSI, pas une colonne neuve : revenir en
            // arriere ne doit pas perdre ce que l'aller a conserve.
            //
            // LA TRONCATURE A 32 CARACTERES EST ASSUMEE ET IRREVERSIBLE : un
            // identifiant de course n'y tient pas. C'est le prix d'un retour arriere
            // apres que des courses de personnes ont ete remunerees, et il vaut mieux
            // l'ecrire ici que de le decouvrir sur une erreur Postgres.
            migrationBuilder.Sql(
                "UPDATE payment.driver_ledger_entries SET reference_label = left(reference_label, 32);");

            migrationBuilder.AlterColumn<string>(
                name: "reference_label",
                schema: "payment",
                table: "driver_ledger_entries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.RenameColumn(
                name: "reference_label",
                schema: "payment",
                table: "driver_ledger_entries",
                newName: "delivery_reference");

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_delivery_id",
                schema: "payment",
                table: "payment_intents",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "IX_driver_ledger_entries_delivery_id",
                schema: "payment",
                table: "driver_ledger_entries",
                column: "delivery_id",
                unique: true,
                filter: "kind = 1 AND delivery_id IS NOT NULL");
        }
    }
}
