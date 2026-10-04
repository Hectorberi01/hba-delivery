using Hba.Dispatch.Domain.Dispatching;

namespace Hba.Dispatch.Application.Common.Views;

/// <summary>
/// Offre telle que l'application livreur la recoit. AUCUNE ADRESSE DE
/// DESTINATION : le referentiel l'interdit avant acceptation.
/// </summary>
public sealed record OfferView(
    Guid Id,

    /// <summary>
    /// La mission : une livraison, ou une course. SON TYPE EST JUSTE A COTE, et
    /// les deux ne se lisent jamais l'un sans l'autre.
    /// </summary>
    Guid ReferenceId,
    ServiceType ServiceType,

    /// <summary>
    /// Le vehicule attendu. UTILE AU CHAUFFEUR QUI EN POSSEDE DEUX : il doit
    /// savoir lequel la mission demande.
    /// </summary>
    VehicleType VehicleType,
    string DriverId,
    OfferStatus Status,
    int WaveNumber,
    DateTimeOffset SentAt,
    DateTimeOffset ExpiresAt,
    string PickupLandmark,
    int DistanceToPickupMeters,
    int TripDistanceMeters,
    long DriverEarningXof,
    double PickupLatitude,
    double PickupLongitude)
{
    public static OfferView From(DispatchAggregate dispatch, Offer offer)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(offer);

        return new OfferView(
            offer.Id,
            dispatch.ReferenceId,
            dispatch.ServiceType,
            dispatch.VehicleType,
            offer.DriverId,
            offer.Status,
            offer.WaveNumber,
            offer.SentAt,
            offer.ExpiresAt,
            dispatch.PickupLandmark,
            offer.DistanceToPickupMeters,
            dispatch.TripDistanceMeters,
            dispatch.DriverEarningXof,
            dispatch.Pickup.Latitude,
            dispatch.Pickup.Longitude);
    }
}

/// <summary>
/// Resultat d'une acceptation. <paramref name="RejectionCode"/> est renseigne
/// quand elle echoue — le contrat nomme OFFER_EXPIRED, ALREADY_TAKEN,
/// DRIVER_ALREADY_RESERVED et DRIVER_NOT_AVAILABLE.
/// </summary>
public sealed record AcceptOfferResult(bool Accepted, string? RejectionCode, OfferView? Offer);
