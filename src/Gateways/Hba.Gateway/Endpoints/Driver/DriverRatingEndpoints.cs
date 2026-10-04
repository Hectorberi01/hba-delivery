using Hba.BuildingBlocks.Security;
using Hba.Contracts.Rating.V1;

namespace Hba.Gateway.Endpoints.Driver;

/// <summary>
/// Ce que le livreur sait de sa propre reputation.
/// </summary>
///
/// <remarks>
/// UNE MOYENNE SANS DETAIL EST UNE SANCTION SANS MOTIF. Le point 41 pose que le
/// livreur doit pouvoir voir ce qu'on lui reproche, pas seulement combien : les
/// deux routes vont ensemble, et la seconde n'est pas un confort.
///
/// IL NE LIT QUE LES SIENS, et c'est Rating qui le force a partir du jeton. La
/// passerelle ne passe aucun identifiant pour la liste.
///
/// L'IDENTIFIANT VIENT DE DriverEndpoints.DriverIdOf, QUI JETTE QUAND LE JETON
/// NE PORTE PAS driver_id. Une copie locale tombant sur le sujet du jeton
/// laisserait passer un identifiant d'utilisateur la ou Rating attend un
/// livreur : la lecture repondrait « aucun avis » au lieu de dire que le jeton
/// est mal forme, et on chercherait le bogue dans Rating.
/// </remarks>
public static class DriverRatingEndpoints
{
    public static IEndpointRouteBuilder MapDriverRatingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/driver/v1").RequireAuthorization(HbaPolicies.Driver);

        group.MapGet("/rating", async (
            HttpContext http,
            RatingService.RatingServiceClient rating,
            CancellationToken cancellationToken) =>
        {
            var vue = await rating.GetDriverRatingAsync(
                new GetDriverRatingRequest { DriverId = DriverEndpoints.DriverIdOf(http) },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                // « enough » DECIDE DE L'AFFICHAGE, PAS L'APPLICATION. Le seuil vit
                // dans Rating : trois ecrans avec trois seuils finiraient par se
                // contredire — le livreur lirait « 4,0 » et le client « pas encore
                // de note ».
                average = vue.Average,
                count = vue.Count,
                enough = vue.Enough,
            });
        });

        group.MapGet("/reviews", async (
            RatingService.RatingServiceClient rating,
            CancellationToken cancellationToken,
            int limit = 20) =>
        {
            var reponse = await rating.ListDriverReviewsAsync(
                new ListDriverReviewsRequest { Limit = limit },
                cancellationToken: cancellationToken);

            return Results.Ok(new { reviews = reponse.Reviews.Select(Lisible) });
        });

        return app;
    }

    /// <summary>
    /// L'avis tel que le livreur le lit.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUN IDENTIFIANT DE CLIENT. Le livreur apprend ce qu'on lui reproche, pas
    /// QUI le lui reproche : une note nominative se regle, et un avis qui se
    /// regle ne sert plus a rien.
    ///
    /// LA MISSION EST NOMMEE, ELLE. Sans elle, un livreur lirait « deux etoiles »
    /// sans savoir de quel trajet on parle — donc sans pouvoir y repondre.
    /// </remarks>
    private static object Lisible(Review avis) => new
    {
        serviceType = avis.ServiceType == Hba.Contracts.Common.V1.ServiceType.Ride ? "ride" : "delivery",
        referenceId = avis.ReferenceId,
        stars = avis.Stars,
        comment = string.IsNullOrWhiteSpace(avis.Comment) ? null : avis.Comment,
        submittedAt = avis.SubmittedAt?.ToDateTimeOffset(),
    };
}
