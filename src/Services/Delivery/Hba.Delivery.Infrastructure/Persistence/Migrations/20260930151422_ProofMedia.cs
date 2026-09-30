using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Delivery.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Les deux preuves deviennent des identifiants de media.
    /// </summary>
    ///
    /// <remarks>
    /// ECRITE A LA MAIN, faute de SDK .NET sur le poste ou la decision a ete
    /// prise. Sa forme suit celle de « DeliveryRefund », et l'instantane du
    /// modele a ete mis a jour dans le meme geste.
    ///
    /// ELLE DETRUIT DEUX COLONNES, et c'est pourquoi elle verifie d'abord.
    /// « PickupProofObjectKey » et « DeliveryProofObjectKey » portaient une
    /// chaine venue du telephone que rien ne validait ; elles doivent etre vides
    /// partout, parce qu'aucune application n'a jamais envoye ce champ. « Doivent
    /// etre » n'est pas « sont » : le bloc DO ci-dessous en fait une certitude,
    /// et fait echouer la migration plutot que d'effacer une donnee qu'on
    /// n'attendait pas. Une migration qui detruit en silence ce qu'elle n'avait
    /// pas prevu est le genre de chose qu'on decouvre trois mois plus tard.
    ///
    /// UNE CONVERSION N'AURAIT RIEN SAUVE de toute facon : une cle de stockage
    /// est un chemin, un identifiant de media est un uuid, et l'un ne se deduit
    /// pas de l'autre.
    /// </remarks>
    public partial class ProofMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM delivery.deliveries
                        WHERE "PickupProofObjectKey" IS NOT NULL
                           OR "DeliveryProofObjectKey" IS NOT NULL
                    ) THEN
                        RAISE EXCEPTION 'PREUVE_EXISTANTE : des cles de preuve sont enregistrees alors que cette migration les suppose vides. Rien n''a ete modifie. Releve-les avant de rejouer.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "DeliveryProofObjectKey",
                schema: "delivery",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "PickupProofObjectKey",
                schema: "delivery",
                table: "deliveries");

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryProofMediaId",
                schema: "delivery",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PickupProofMediaId",
                schema: "delivery",
                table: "deliveries",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryProofMediaId",
                schema: "delivery",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "PickupProofMediaId",
                schema: "delivery",
                table: "deliveries");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryProofObjectKey",
                schema: "delivery",
                table: "deliveries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupProofObjectKey",
                schema: "delivery",
                table: "deliveries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }
    }
}
