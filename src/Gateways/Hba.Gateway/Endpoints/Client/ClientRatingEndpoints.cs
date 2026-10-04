using Hba.BuildingBlocks.Security;
using Hba.Contracts.Rating.V1;

namespace Hba.Gateway.Endpoints.Client;

/// <summary>
/// Les avis du client sur ses livreurs. Point 41, tranche le 3 octobre 2026.
/// </summary>
///
/// <remarks>
/// LE LIVREUR N'APPARAIT NULLE PART DANS CES ROUTES, ni en entree ni en sortie.
/// En entree parce que Rating le tient de l'evenement de fin de mission — le
/// laisser au client, c'est le laisser noter quelqu'un qui ne l'a pas servi. En
/// sortie parce que le point 24 l'interdit : le client n'a droit a aucun
/// identifiant de livreur avant l'affectation, et rien ne justifie de le lui
/// rendre apres.
///
/// AUCUNE ROUTE POUR LIRE LA NOTE D'UN LIVREUR. La question « le client voit-il
/// la note avant l'affectation » reste ouverte au point 41, et le silence n'est
/// pas une autorisation.
/// </remarks>
public static class ClientRatingEndpoints
{
    public static IEndpointRouteBuilder MapClientRatingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/client/v1/reviews").RequireAuthorization(HbaPolicies.Customer);

        // CE QUE CE CLIENT PEUT ENCORE NOTER.
        //
        // LE SERVICE REPREND LE SUJET DU JETON, et la route ne lui passe aucun
        // identifiant : un client ne lit que les siennes, et c'est verifie cote
        // service (ADR 0007), pas ici.
        group.MapGet("/pending", async (
            RatingService.RatingServiceClient rating,
            CancellationToken cancellationToken,
            int limit = 10) =>
        {
            var reponse = await rating.ListPendingReviewsAsync(
                new ListPendingReviewsRequest { Limit = limit },
                cancellationToken: cancellationToken);

            return Results.Ok(new { reviews = reponse.Reviews.Select(Lisible) });
        });

        // NOTER UNE MISSION.
        //
        // JAMAIS BLOQUANT, ET C'EST LE POINT 41 : rien dans l'application ne doit
        // dependre de cet appel. Un echec ici ne doit empecher ni de commander,
        // ni de consulter.
        group.MapPost("/", async (
            SubmitReviewDto body,
            RatingService.RatingServiceClient rating,
            CancellationToken cancellationToken) =>
        {
            var avis = await rating.SubmitReviewAsync(
                new SubmitReviewRequest
                {
                    ServiceType = Nature(body.ServiceType),
                    ReferenceId = body.ReferenceId,
                    Stars = body.Stars,
                    Comment = body.Comment ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(avis));
        });

        return app;
    }

    /// <summary>
    /// La nature de la mission, ecrite en toutes lettres par l'application.
    /// </summary>
    ///
    /// <remarks>
    /// EN TEXTE, PAS EN NUMERO : « ride » et « delivery » se lisent dans une
    /// trace reseau et survivent a une renumerotation de l'enumere. C'est le meme
    /// choix que pour les types de vehicule de cette passerelle.
    ///
    /// CE QUI N'EST PAS RECONNU DEVIENT UNSPECIFIED, et Rating refuse : supposer
    /// la livraison ferait noter un colis qui porte l'identifiant d'une course.
    /// </remarks>
    private static Hba.Contracts.Common.V1.ServiceType Nature(string? nom)
        => nom?.Trim().ToUpperInvariant() switch
        {
            "RIDE" or "COURSE" => Hba.Contracts.Common.V1.ServiceType.Ride,
            "DELIVERY" or "COLIS" => Hba.Contracts.Common.V1.ServiceType.Delivery,
            _ => Hba.Contracts.Common.V1.ServiceType.Unspecified,
        };

    /// <summary>
    /// L'avis tel que l'application cliente le lit.
    /// </summary>
    ///
    /// <remarks>
    /// NI IDENTIFIANT DE LIVREUR, NI IDENTIFIANT DE CLIENT. Le premier est
    /// interdit par le point 24 ; le second ne lui apprendrait rien, puisqu'il
    /// s'agit de lui.
    ///
    /// ZERO ETOILE VEUT DIRE « PAS ENCORE NOTEE ». La route le traduit en nul,
    /// parce qu'un zero dans du JSON se lit comme une note et non comme une
    /// absence.
    /// </remarks>
    private static object Lisible(Review avis) => new
    {
        serviceType = avis.ServiceType == Hba.Contracts.Common.V1.ServiceType.Ride ? "ride" : "delivery",
        referenceId = avis.ReferenceId,
        stars = avis.Stars == 0 ? (int?)null : avis.Stars,
        comment = string.IsNullOrWhiteSpace(avis.Comment) ? null : avis.Comment,
        missionCompletedAt = avis.MissionCompletedAt?.ToDateTimeOffset(),
        submittedAt = avis.SubmittedAt?.ToDateTimeOffset(),
    };
}

/// <summary>Ce que le client envoie pour noter.</summary>
public sealed record SubmitReviewDto(
    string ServiceType,
    string ReferenceId,
    int Stars,
    string? Comment = null);
