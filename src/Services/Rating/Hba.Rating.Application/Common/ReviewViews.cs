using Hba.Rating.Domain.Reviews;

namespace Hba.Rating.Application.Common;

/// <summary>Un avis, tel qu'il sort du service.</summary>
public sealed record ReviewView(
    Guid Id,
    MissionType MissionType,
    Guid ReferenceId,
    string CustomerId,
    Guid DriverId,
    int? Stars,
    string? Comment,
    DateTimeOffset MissionCompletedAt,
    DateTimeOffset? SubmittedAt)
{
    public static ReviewView From(Review review)
    {
        ArgumentNullException.ThrowIfNull(review);

        return new ReviewView(
            review.Id,
            review.MissionType,
            review.ReferenceId,
            review.CustomerId,
            review.DriverId,
            review.Stars,
            review.Comment,
            review.MissionCompletedAt,
            review.SubmittedAt);
    }
}

/// <summary>La reputation d'un livreur, telle qu'elle peut etre montree.</summary>
///
/// <remarks>
/// LE SEUIL EST ICI, PAS DANS LES ECRANS. Trois applications avec trois seuils
/// finiraient par se contredire : le livreur lirait « 4,0 » et le client
/// « pas encore de note ». Le service tranche une fois pour toutes.
/// </remarks>
public sealed record DriverRatingView(Guid DriverId, double Average, int Count, bool Enough)
{
    /// <summary>En dessous, la moyenne ne se montre pas.</summary>
    ///
    /// <remarks>
    /// CINQ AVIS, ET LE CHIFFRE VIENT DU POINT 41 : « dix courses ne font pas une
    /// moyenne, et afficher 2,0 sur un seul avis est plus faux que de n'afficher
    /// rien ». Un client mecontent sur un seul trajet ne doit pas pouvoir fixer
    /// la reputation de quelqu'un.
    ///
    /// IL EST VOLONTAIREMENT BAS. Trop haut, un livreur neuf reste invisible
    /// pendant des semaines, ce qui le penalise exactement quand il en a le plus
    /// besoin.
    /// </remarks>
    public const int AvisMinimum = 5;

    public static DriverRatingView From(Guid driverId, double moyenne, int nombre) =>
        new(driverId, Math.Round(moyenne, 2), nombre, nombre >= AvisMinimum);
}
