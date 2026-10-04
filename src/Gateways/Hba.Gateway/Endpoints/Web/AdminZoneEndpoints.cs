using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Pricing.V1;
using ProtoZone = Hba.Contracts.Pricing.V1.Zone;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// Les zones tarifaires, pour le back-office.
/// </summary>
///
/// <remarks>
/// POURQUOI CES ROUTES N'EXISTAIENT PAS, ET CE QUE LEUR ABSENCE COÛTAIT. L'agrégat
/// <c>Zone</c> existait depuis le début, la table <c>pricing.zones</c> aussi, et
/// elle était VIDE : aucune colonne de géométrie, aucune commande, aucun appel.
/// <c>ConfiguredZoneLocator</c> rendait la zone de repli configurée pour TOUT point
/// du globe. Conséquence : une seule grille s'appliquait partout, et le filtre par
/// zone de l'écran des grilles filtrait un champ que personne ne pouvait faire
/// varier. Le zonage tarifaire était décoratif.
///
/// DEUX POLITIQUES, ET PAS UNE SEULE (point 40). La lecture est ouverte au
/// back-office — la carte des zones est de la supervision, et ops a besoin de
/// comprendre pourquoi une course a été chiffrée à ce prix-là. L'écriture est
/// réservée à <c>admin</c>, plus étroitement que les grilles : redessiner une zone
/// DÉPLACE DES PRIX sans toucher à une seule grille, et la finance fixe les prix
/// dans une géographie que l'administration définit.
///
/// LA PASSERELLE NE FAIT QUE LE PREMIER FILTRE. Pricing revérifie lui-même par
/// <c>PricingAccess</c> — le jeton qui lui parvient est celui de l'utilisateur
/// final, reporté, pas celui d'un service de confiance (ADR 0007).
///
/// AUCUNE SUPPRESSION N'EST EXPOSÉE, ET IL N'EN EXISTE PAS. Des grilles et des
/// devis nomment une zone par son code : l'effacer rendrait illisibles des prix
/// déjà facturés. Une zone se désactive, et cesse alors de contenir des points.
/// </remarks>
public static class AdminZoneEndpoints
{
    public static IEndpointRouteBuilder MapAdminZoneEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var lecture = app.MapGroup("/api/admin/v1/zones")
            .RequireAuthorization(HbaPolicies.BackOffice);

        lecture.MapGet("/", async (
            bool? inactives,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var reponse = await pricing.ListZonesAsync(
                new ListZonesRequest { IncludeInactive = inactives ?? false },
                cancellationToken: cancellationToken);

            return Results.Ok(reponse.Zones.Select(Lisible));
        });

        // L'ÉCRITURE A SON PROPRE GROUPE PARCE QU'ELLE A SA PROPRE POLITIQUE. Les
        // réunir sous BackOffice aurait laissé ops dessiner, et donc déplacer des
        // prix ; les réunir sous Admin aurait fermé la carte à ops, qui en a besoin
        // pour lire un dispatch.
        var ecriture = app.MapGroup("/api/admin/v1/zones")
            .RequireAuthorization(HbaPolicies.Admin);

        ecriture.MapPost("/", async (
            DessinerUneZone corps,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(corps);

            var requete = new SaveZoneRequest
            {
                Code = corps.Code ?? string.Empty,
                Name = corps.Nom ?? string.Empty,
                Priority = corps.Priorite,
            };

            // LE CONTOUR ARRIVE EN [latitude, longitude], COMME TOUT LE RESTE DE CE
            // DÉPÔT — et NON dans l'ordre du GeoJSON, qui écrit la longitude en
            // premier. Un écran qui enverrait un GeoJSON brut placerait Cotonou en
            // Somalie ; c'est à lui de convertir, et le nom des champs le dit.
            foreach (var sommet in corps.Contour ?? [])
            {
                requete.Ring.Add(new GeoPoint
                {
                    Latitude = sommet.Latitude,
                    Longitude = sommet.Longitude,
                });
            }

            var zone = await pricing.SaveZoneAsync(requete, cancellationToken: cancellationToken);

            return Results.Ok(Lisible(zone));
        });

        ecriture.MapPost("/{code}/active", async (
            string code,
            ActiverUneZone? corps,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var zone = await pricing.SetZoneActiveAsync(
                new SetZoneActiveRequest { Code = code, Active = corps?.Actif ?? true },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(zone));
        });

        return app;
    }

    /// <summary>
    /// La zone telle que la console la lit.
    /// </summary>
    ///
    /// <remarks>
    /// MISE EN FORME À LA MAIN, comme pour les grilles : relayer le message protobuf
    /// laisserait System.Text.Json sérialiser les énumérations en entiers — le piège
    /// connu de cette passerelle. Ici il n'y a pas d'énumération, mais le contour
    /// sortirait en objets <c>{ latitude, longitude }</c> avec des zéros explicites
    /// pour les champs absents, et la carte n'a que faire d'un sommet à (0,0).
    /// </remarks>
    private static object Lisible(ProtoZone zone) => new
    {
        id = zone.Id,
        code = zone.Code,
        nom = zone.Name,
        priorite = zone.Priority,
        actif = zone.Active,

        // VIDE VEUT DIRE « PAS ENCORE DESSINÉE », et l'écran doit le dire plutôt que
        // d'afficher une zone invisible : une zone sans contour ne contient aucun
        // point, donc ne s'applique à personne.
        contour = zone.Ring.Select(p => new { latitude = p.Latitude, longitude = p.Longitude }),
    };

    /// <summary>
    /// Ce que le back-office envoie pour créer ou redessiner une zone.
    /// </summary>
    ///
    /// <remarks>
    /// UN SEUL VERBE POUR LES DEUX, ET C'EST VOULU : une zone n'a pas d'historique de
    /// formes, donc « créer » et « redessiner » ne se distinguent que par l'existence
    /// du code. Redessiner ne change aucun prix déjà facturé — un devis a capté le
    /// CODE de sa zone, pas son polygone.
    ///
    /// AU MOINS TROIS SOMMETS, et le service refuse en dessous. Un contour qui se
    /// recoupe est refusé par la base, qui est la seule à savoir le voir.
    /// </remarks>
    public sealed record DessinerUneZone(
        string? Code,
        string? Nom,
        int Priorite,
        IReadOnlyList<SommetDto>? Contour);

    public sealed record SommetDto(double Latitude, double Longitude);

    public sealed record ActiverUneZone(bool Actif);
}
