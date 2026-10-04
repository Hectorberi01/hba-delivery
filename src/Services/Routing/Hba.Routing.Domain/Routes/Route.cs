using Hba.BuildingBlocks.Domain;
using Hba.Routing.Domain.Exceptions;

namespace Hba.Routing.Domain.Routes;

/// <summary>
/// Un trajet mesuré : ce qu'il fait, ce qu'il dure, et d'où vient le chiffre.
/// </summary>
///
/// <remarks>
/// LA PROVENANCE FAIT PARTIE DE LA MESURE, elle n'est pas une métadonnée de
/// journalisation. Huit kilomètres calculés par OSRM et huit kilomètres à vol
/// d'oiseau ne valent pas la même chose : la seconde mesure est sous-estimée
/// d'un tiers environ en ville, davantage quand le trajet contourne la lagune.
/// Un appelant qui fige un prix ferme doit pouvoir distinguer les deux, sinon il
/// facture moins que la course ne coûte et personne ne s'en aperçoit avant la
/// fin du mois.
/// </remarks>
public sealed class Route : ValueObject
{
    private Route(
        int distanceMeters,
        int durationSeconds,
        string encodedPolyline,
        string provider,
        bool fromFallback,
        DateTimeOffset calculatedAt)
    {
        DistanceMeters = distanceMeters;
        DurationSeconds = durationSeconds;
        EncodedPolyline = encodedPolyline;
        Provider = provider;
        FromFallback = fromFallback;
        CalculatedAt = calculatedAt;
    }

    public int DistanceMeters { get; }

    public int DurationSeconds { get; }

    /// <summary>Tracé encodé, vide s'il n'a pas été demandé.</summary>
    public string EncodedPolyline { get; }

    /// <summary>Qui a répondu : « osrm », « haversine ».</summary>
    public string Provider { get; }

    /// <summary>Vrai quand la mesure vient du repli à vol d'oiseau.</summary>
    public bool FromFallback { get; }

    public DateTimeOffset CalculatedAt { get; }

    public static Route Create(
        int distanceMeters,
        int durationSeconds,
        string provider,
        DateTimeOffset calculatedAt,
        string? encodedPolyline = null,
        bool fromFallback = false)
    {
        if (distanceMeters < 0 || durationSeconds < 0)
        {
            throw new DomainException(
                RoutingErrorCodes.InvalidRoute,
                "Distance et durée d'un trajet ne peuvent pas être négatives.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new DomainException(
                RoutingErrorCodes.InvalidRoute,
                "Une mesure dit toujours d'où elle vient.");
        }

        return new Route(
            distanceMeters,
            durationSeconds,
            encodedPolyline ?? string.Empty,
            provider.Trim(),
            fromFallback,
            calculatedAt);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DistanceMeters;
        yield return DurationSeconds;
        yield return EncodedPolyline;
        yield return Provider;
        yield return FromFallback;
    }
}
