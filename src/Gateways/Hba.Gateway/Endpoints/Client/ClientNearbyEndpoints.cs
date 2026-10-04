using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Location.V1;

namespace Hba.Gateway.Endpoints.Client;

/// <summary>
/// Les livreurs autour du client : POSITION ET VÉHICULE, RIEN D'AUTRE.
/// </summary>
public static class ClientNearbyEndpoints
{
    /// <summary>
    /// PLAFONDS EN DUR, ET NON RÉGLABLES PAR L'APPELANT.
    ///
    /// Sans eux, une seule requête suffirait à cartographier une ville : rayon
    /// de cent kilomètres, limite à dix mille, et la flotte entière tient dans
    /// une réponse. Le client a besoin de savoir s'il y a quelqu'un près de
    /// lui ; il n'a besoin ni d'un rayon de son choix, ni d'un dénombrement.
    /// </summary>
    private const int RayonMetres = 3000;
    private const int Plafond = 15;

    public static IEndpointRouteBuilder MapClientNearbyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/client/v1").RequireAuthorization(HbaPolicies.Customer);

        group.MapGet("/drivers/nearby", async (
            double lat,
            double lng,
            LocationService.LocationServiceClient locations,
            DriverService.DriverServiceClient drivers,
            CancellationToken cancellationToken) =>
        {
            // DES COORDONNÉES INVALIDES SE REFUSENT ICI. Les laisser passer
            // ferait porter à Driver une requête Redis GEO qui échouera plus
            // loin, avec un message qui ne dira pas d'où venait la faute.
            if (double.IsNaN(lat) || double.IsNaN(lng) ||
                lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                return Results.BadRequest(new { error = "Coordonnees invalides." });
            }

            // ON DEMANDE EXACTEMENT LE PLAFOND, ET PLUS LE TRIPLE.
            //
            // IL A FALLU SUR-INTERROGER TANT QUE DRIVER ECARTAIT : la geographie ne
            // sait pas qui est disponible, et demander quinze points en rendait
            // presque toujours moins une fois le filtre passe. Depuis le 3 octobre
            // 2026 la carte montre tout le monde — Driver n'ecarte plus personne de
            // cette liste — et la compensation n'a plus d'objet.
            var proches = await locations.FindNearbyDriversAsync(
                new FindNearbyDriversRequest
                {
                    Center = new GeoPoint { Latitude = lat, Longitude = lng },
                    RadiusMeters = RayonMetres,
                    Limit = Plafond,
                },
                cancellationToken: cancellationToken);

            if (proches.Drivers.Count == 0)
            {
                return Results.Ok(new { positions = Array.Empty<object>() });
            }

            // LE VEHICULE ET LA DISPONIBILITE VIENNENT DE DRIVER, PAS DE LOCATION.
            // L'index geographique ne sait ni avec quoi roule chacun, ni qui est
            // libre, et n'a pas a le savoir : c'est la passerelle qui recolle.
            //
            // PAS FilterAvailableDrivers, ET C'EST TOUT LE CHANGEMENT DU 3 OCTOBRE :
            // ce filtre ecartait ceux qu'il faut desormais afficher en eteint. Pas
            // GetDriverSummaries non plus, qui rend un nom et un telephone reserves
            // au back-office.
            var demande = new GetDriverPinsRequest();
            demande.DriverIds.AddRange(proches.Drivers.Select(d => d.DriverId));

            var pastilles = await drivers.GetDriverPinsAsync(
                demande,
                cancellationToken: cancellationToken);

            var parId = pastilles.Pins.ToDictionary(p => p.DriverId, StringComparer.Ordinal);

            // LA PROJECTION EST LA RÈGLE, PAS UNE COMMODITÉ D'AFFICHAGE.
            //
            // Ajouter un champ ici, même anodin en apparence — une distance, un
            // rang, un identifiant opaque — rouvre la question tranchée au
            // point 24, et doit passer par le référentiel AVANT le code. C'est
            // ce qui a été fait trois fois : le 30 septembre pour le type de
            // véhicule, puis deux fois le 3 octobre — le décompte des écartés le
            // matin, la disponibilité par pastille l'après-midi. À chaque fois :
            // le point 24, puis la matrice de visibilité, puis seulement ces
            // lignes.
            //
            // CE QUI N'EST TOUJOURS PAS RENDU, ET QUI BORNE TOUT LE RESTE :
            // l'identifiant. Il permettrait de reconnaître le même livreur d'un
            // jour sur l'autre, donc de suivre ses horaires et ses trajets.
            // Deux livreurs à moto restent indistinguables ici, et rien ne relie
            // une position du jour à celle de la veille. Ni le nom, ni la
            // plaque, ni le téléphone ne passent non plus.
            //
            // LE TYPE DE VÉHICULE EST SEMI-IDENTIFIANT, et c'est le prix assumé :
            // quatre de ses cinq valeurs sont rares à Cotonou, donc un tricycle
            // au milieu des motos se repère. Le point 24 pèse ce que cela ouvre,
            // et il pèse aussi ce que l'après-midi du 3 octobre y a ajouté : ce
            // tricycle est maintenant visible MÊME QUAND IL TRAVAILLE.
            return Results.Ok(new
            {
                positions = proches.Drivers
                    .Where(driver => driver.Position?.Point is not null
                        && parId.ContainsKey(driver.DriverId))
                    .Take(Plafond)
                    .Select(driver => new
                    {
                        latitude = driver.Position.Point.Latitude,
                        longitude = driver.Position.Point.Longitude,
                        vehicleType = parId[driver.DriverId].VehicleType.ToString(),

                        // CE QUI DECIDE DU DESSIN : relief ou eteint. Il ne dit pas
                        // POURQUOI quelqu'un est indisponible — mission, deconnexion
                        // recente, dossier redevenu invalide — et c'est voulu.
                        disponible = parId[driver.DriverId].Available,
                    }),
            });
        });

        return app;
    }
}
