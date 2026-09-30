using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Driver.V1;

namespace Hba.Gateway.Endpoints.Client;

/// <summary>
/// Les livreurs autour du client : POSITION ET VÉHICULE, RIEN D'AUTRE.
///
/// POURQUOI CETTE ROUTE EXISTE (point 24, tranché le 28 septembre 2026). La
/// matrice de visibilité du référentiel était MUETTE sur les positions des
/// livreurs côté client. Le silence n'étant pas une autorisation, la question a
/// été posée avant d'écrire quoi que ce soit, et tranchée : le client voit les
/// positions réelles. Un écran vide, sur un marché où le maillage est encore
/// mince, laisse croire qu'il n'y a personne — et cette impression coûte des
/// courses.
///
/// CE QUE CETTE DÉCISION N'A PAS LEVÉ, ET C'EST TOUT L'OBJET DE CE FICHIER. La
/// ligne « Téléphone du livreur : pendant la mission » de la matrice tient
/// toujours : aucune identité de livreur avant DRIVER_ASSIGNED. La réponse ne
/// porte donc ni identifiant, ni nom, ni plaque, ni téléphone.
///
/// LE TYPE DE VÉHICULE, LUI, EST SORTI LE 30 SEPTEMBRE 2026, par une révision
/// du point 24 puis du référentiel des acteurs et de la matrice de visibilité —
/// dans cet ordre, avant le code. Il passe parce qu'il désigne une CATÉGORIE et
/// non un homme : des centaines de livreurs partagent « moto ». C'est la carte
/// du client qui le réclamait, une pastille de moto valant mieux que quatre
/// points identiques quand il s'agit de choisir d'attendre ou de commander.
///
/// UN IDENTIFIANT STABLE SUFFIRAIT À ANNULER LA RÈGLE, même sans nom : il
/// permettrait de reconnaître le même livreur d'un jour sur l'autre, de suivre
/// ses horaires et ses trajets. C'est exactement ce que la matrice refuse.
///
/// LA MISE EN FORME SE FAIT ICI, ET PAS DANS LE SERVICE, pour une raison
/// précise : <c>FindAvailableNearby</c> rend des identifiants parce que le
/// dispatch en a besoin pour ouvrir une vague. C'est à la frontière du client
/// qu'ils doivent disparaître, pas dans un service dont un autre appelant
/// légitime dépend.
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

            var reponse = await drivers.FindAvailableNearbyAsync(
                new FindAvailableNearbyRequest
                {
                    Center = new GeoPoint { Latitude = lat, Longitude = lng },
                    RadiusMeters = RayonMetres,
                    Limit = Plafond,
                },
                cancellationToken: cancellationToken);

            // LA PROJECTION EST LA RÈGLE, PAS UNE COMMODITÉ D'AFFICHAGE.
            //
            // Ajouter un champ ici, même anodin en apparence — une distance, un
            // rang, un identifiant opaque — rouvre la question tranchée au
            // point 24, et doit passer par le référentiel AVANT le code. C'est
            // ce qui a été fait le 30 septembre 2026 pour le type de véhicule :
            // le point 24 a été révisé, puis le référentiel des acteurs et la
            // matrice de visibilité, et seulement ensuite cette ligne.
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
            // au milieu des motos se repère. Le point 24 pèse ce que cela ouvre.
            return Results.Ok(new
            {
                positions = reponse.Drivers
                    .Where(driver => driver.Position is not null)
                    .Select(driver => new
                    {
                        latitude = driver.Position.Latitude,
                        longitude = driver.Position.Longitude,
                        vehicleType = driver.VehicleType.ToString(),
                    }),
            });
        });

        return app;
    }
}
