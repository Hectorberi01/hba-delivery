using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TarificationParNature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_zone_code_vehicle_type_valid_from",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.AddColumn<int>(
                name: "band_from_minute",
                schema: "pricing",
                table: "tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "band_to_minute",
                schema: "pricing",
                table: "tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 1440);

            migrationBuilder.AddColumn<int>(
                name: "included_weight_grams",
                schema: "pricing",
                table: "tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "per_kilogram",
                schema: "pricing",
                table: "tariffs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "service_type",
                schema: "pricing",
                table: "tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "service_type",
                schema: "pricing",
                table: "quotes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ================= REPRISE DES LIGNES EXISTANTES, AJOUTEE A LA MAIN ===
            //
            // SANS CES DEUX LIGNES, LA PLATEFORME N'A PLUS AUCUN PRIX.
            //
            // EF pose « service_type » a zero, c'est-a-dire Unspecified. Or la nature
            // fait desormais partie de la cle de lecture : FindInForceAsync cherche
            // (zone, NATURE, vehicule, instant). Une grille restee a zero ne serait
            // trouvee par personne — chaque devis, colis comme course, echouerait sur
            // NO_TARIFF des la premiere seconde du deploiement. Ce n'est pas une
            // degradation lente, c'est l'arret des commandes.
            //
            // TOUT CE QUI EXISTE AVANT CETTE MIGRATION EST UN COLIS, par construction :
            // la tarification des courses nait avec elle. La reprise n'invente donc
            // rien, elle ecrit ce qui etait deja vrai et que le schema ne disait pas.
            //
            // LES DEVIS SONT REPRIS AUSSI, et ce n'est pas du zele. Un devis vit quinze
            // minutes : ceux qui sont en vol au moment du deploiement seraient consommes
            // APRES, et Quote.Consume compare la nature demandee a celle du devis. Les
            // laisser a zero ferait echouer sur QUOTE_SERVICE_MISMATCH la creation de
            // livraisons dont le client a deja vu le prix — un refus incomprehensible
            // pour lui, et pour l'exploitation.
            migrationBuilder.Sql(
                "UPDATE pricing.tariffs SET service_type = 1 WHERE service_type = 0;");

            migrationBuilder.Sql(
                "UPDATE pricing.quotes SET service_type = 1 WHERE service_type = 0;");

            // ================= RETRAIT DES DEFAUTS POSES PAR EF ===================
            //
            // LE DEFAUT N'EXISTAIT QUE POUR REMPLIR LES LIGNES DEJA LA. Le modele n'en
            // declare aucun pour la nature — la configuration EF le dit noir sur blanc —
            // precisement pour qu'une insertion qui l'oublie echoue au lieu de passer en
            // silence sous le nom d'une prestation non precisee, que l'agregat refuse.
            //
            // « per_kilogram » PERD AUSSI SON DEFAUT, pour une raison differente : le
            // modele ne lui en declare pas non plus, et laisser celui-ci en base ferait
            // deriver le schema du cliche. La prochaine migration generee tenterait de
            // le retirer, sans que son auteur sache pourquoi.
            //
            // LES TROIS AUTRES COLONNES GARDENT LEUR DEFAUT, ET C'EST VOULU : le modele
            // les declare (band_from_minute = 0, band_to_minute = 1440,
            // included_weight_grams = 0). Une grille sans plage couvre la journee
            // entiere, un colis sans poids inclus n'en a pas : ce sont des valeurs
            // signifiantes, pas des trous a combler.
            migrationBuilder.Sql(
                "ALTER TABLE pricing.tariffs ALTER COLUMN service_type DROP DEFAULT;");

            migrationBuilder.Sql(
                "ALTER TABLE pricing.quotes ALTER COLUMN service_type DROP DEFAULT;");

            migrationBuilder.Sql(
                "ALTER TABLE pricing.tariffs ALTER COLUMN per_kilogram DROP DEFAULT;");

            // L'INDEX EST CREE EN DERNIER, SUR DES DONNEES DEJA JUSTES. Il n'est pas
            // unique : plusieurs grilles coexistent legitimement sur la meme cle, une
            // par plage horaire. C'est le controle de chevauchement, cote application,
            // qui garantit qu'une minute n'a qu'un prix.
            migrationBuilder.CreateIndex(
                name: "IX_tariffs_zone_code_service_type_vehicle_type_valid_from",
                schema: "pricing",
                table: "tariffs",
                columns: new[] { "zone_code", "service_type", "vehicle_type", "valid_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_zone_code_service_type_vehicle_type_valid_from",
                schema: "pricing",
                table: "tariffs");

            // ================= CE RETOUR ARRIERE DETRUIT DES DONNEES ==============
            //
            // IL EFFACE LES GRILLES ET LES DEVIS DE COURSE, ET IL LE DOIT.
            //
            // Le code d'avant cette migration lit les grilles par (zone, vehicule)
            // seulement : il ne sait pas qu'une nature existe. Une grille de course
            // laissee en place serait donc choisie pour tarifer un COLIS — avec le
            // plancher d'une course et la part d'un chauffeur. Personne ne le verrait
            // avant la premiere facture, et c'est exactement le defaut que la nature a
            // ete introduite pour fermer.
            //
            // LA PERTE EST REELLE ET ASSUMEE : les grilles de course saisies depuis le
            // deploiement ne sont pas recuperables par un nouveau Up. Un retour arriere
            // ici n'est pas une manoeuvre de routine — c'est un incident, et il faut
            // avoir note les baremes avant de le declencher.
            //
            // LES DEVIS DE COURSE PARTENT D'ABORD. Ils ne portent pas de cle etrangere
            // vers la grille, donc l'ordre n'est pas une contrainte technique ; il
            // suffit qu'aucun devis de course ne survive a la colonne qui le disait.
            migrationBuilder.Sql("DELETE FROM pricing.quotes WHERE service_type = 2;");

            migrationBuilder.Sql("DELETE FROM pricing.tariffs WHERE service_type = 2;");

            migrationBuilder.DropColumn(
                name: "band_from_minute",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "band_to_minute",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "included_weight_grams",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "per_kilogram",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "pricing",
                table: "quotes");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_zone_code_vehicle_type_valid_from",
                schema: "pricing",
                table: "tariffs",
                columns: new[] { "zone_code", "vehicle_type", "valid_from" });
        }
    }
}
