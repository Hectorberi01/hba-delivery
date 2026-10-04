using Hba.Location.Domain.Positions;

namespace Hba.Location.Application.Common;

/// <summary>Une position telle qu'elle sort du service.</summary>
public sealed record PositionView(
    Guid DriverId,
    double Latitude,
    double Longitude,
    double? HeadingDegrees,
    double? SpeedMps,
    double? AccuracyMeters,
    DateTimeOffset CapturedAt,
    DateTimeOffset SeenAt)
{
    public static PositionView From(DriverPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        return new PositionView(
            position.DriverId,
            position.Point.Latitude,
            position.Point.Longitude,
            position.Motion.HeadingDegrees,
            position.Motion.SpeedMps,
            position.Motion.AccuracyMeters,
            position.CapturedAt,
            position.SeenAt);
    }
}

/// <summary>
/// Ce que rend la question « celui-la, ou est-il ? ».
/// </summary>
///
/// <remarks>
/// L'ABSENCE EST UNE REPONSE, PAS UNE ERREUR, d'ou cette vue plutot qu'un
/// <c>PositionView?</c> accompagne d'un refus. Un telephone eteint est un fait
/// ordinaire du metier ; lever une exception obligerait l'appelant a traiter en
/// catastrophe le cas le plus banal de la journee.
///
/// LA FENETRE EST RENDUE MEME QUAND RIEN N'EST TROUVE, et c'est la qu'elle sert le
/// plus : « aucune position » sans « depuis deux minutes » se lit comme un constat
/// definitif alors que c'est une fenetre glissante.
/// </remarks>
public sealed record PositionLookupView(
    PositionView? Position,
    int? DistanceMeters,
    int FreshnessSeconds)
{
    public bool Found => Position is not null;
}

/// <summary>Un chauffeur proche, et sa distance a vol d'oiseau.</summary>
public sealed record NearbyDriverView(PositionView Position, int DistanceMeters);

/// <summary>
/// Une liste de positions, et la fenetre qui la definit.
/// </summary>
///
/// <remarks>
/// LA FENETRE VOYAGE AVEC LA LISTE, TOUJOURS. « 14 chauffeurs » ne veut rien dire
/// sans « vus dans les 120 dernieres secondes » : sans elle, l'appelant afficherait
/// un effectif la ou il n'y a qu'un compte de points recents.
/// </remarks>
public sealed record PositionPageView(IReadOnlyList<PositionView> Positions, int FreshnessSeconds);

/// <summary>Des chauffeurs proches, et la fenetre qui definit « proche et recent ».</summary>
public sealed record NearbyPageView(IReadOnlyList<NearbyDriverView> Drivers, int FreshnessSeconds);
