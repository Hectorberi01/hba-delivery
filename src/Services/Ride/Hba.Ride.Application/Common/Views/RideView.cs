using Hba.Ride.Domain.Rides;

namespace Hba.Ride.Application.Common.Views;

/// <summary>
/// Une course telle qu'elle sort du service.
/// </summary>
///
/// <remarks>
/// L'IDENTIFIANT DU CHAUFFEUR EST ICI, ET LA PASSERELLE DÉCIDE S'IL SORT. La
/// matrice de visibilité réserve l'identité du chauffeur à partir de
/// l'affectation : avant, le client ne doit rien voir. Ce filtrage appartient à
/// la frontière cliente, pas à cette vue, qui sert aussi au back-office et au
/// chauffeur lui-même.
/// </remarks>
public sealed record RideView(
    Guid Id,
    string CustomerId,
    string? DriverId,
    double PickupLatitude,
    double PickupLongitude,
    double DestinationLatitude,
    double DestinationLongitude,
    VehicleType VehicleType,
    RideStatus Status,
    int EstimatedDistanceMeters,
    int EstimatedDurationSeconds,
    long EstimatedPrice,
    long? FinalPrice,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DriverAssignedAt,
    DateTimeOffset? DriverArrivedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,

    /// <summary>
    /// L'intention de paiement, pour la traçabilité. L'ADRESSE DE PAIEMENT N'EST
    /// PAS ICI : elle expire, et un écran de suivi qui l'afficherait enverrait le
    /// passager sur une page morte.
    /// </summary>
    string? PaymentIntentId,

    /// <summary>
    /// Ce qui serait retenu si le passager annulait MAINTENANT. Zéro tant qu'aucun
    /// chauffeur n'est affecté, et zéro pour toujours si la grille de la zone ne
    /// prévoit pas de retenue.
    ///
    /// LE SERVICE APPLIQUE LA RÈGLE, L'APPLICATION NE LA REJOUE PAS : c'est le
    /// même appel qui remplit l'événement d'annulation, donc le chiffre affiché
    /// est exactement celui qui sera prélevé.
    /// </summary>
    long CancellationFeeDue,

    /// <summary>
    /// Le chauffeur affecté, ou null. LA PASSERELLE DÉCIDE CE QUI SORT : la matrice
    /// de visibilité réserve son identité à partir de l'affectation, et c'est
    /// justement à ce moment que cet objet apparaît.
    /// </summary>
    AssignedDriverView? Driver)
{
    public static RideView From(RideAggregate ride)
    {
        ArgumentNullException.ThrowIfNull(ride);

        return new RideView(
            ride.Id,
            ride.CustomerId,
            ride.DriverId,
            ride.Pickup.Latitude,
            ride.Pickup.Longitude,
            ride.Destination.Latitude,
            ride.Destination.Longitude,
            ride.VehicleType,
            ride.Status,
            ride.Estimate.DistanceMeters,
            ride.Estimate.DurationSeconds,
            ride.Estimate.Price.Amount,
            ride.FinalPrice?.Amount,
            ride.RequestedAt,
            ride.DriverAssignedAt,
            ride.DriverArrivedAt,
            ride.StartedAt,
            ride.CompletedAt,
            ride.CancelledAt,
            ride.CancellationReason,
            ride.PaymentIntentId,
            ride.RetenueDue(),
            AssignedDriverView.From(ride.Driver));
    }
}

/// <summary>Une page d'historique, et le total.</summary>
public sealed record RidePageView(IReadOnlyList<RideView> Rides, int Total);

/// <summary>
/// Ce que rend une demande de course : la course, et où la payer.
/// </summary>
///
/// <remarks>
/// LES DEUX ENSEMBLE, PARCE QUE LE PRIX EST ENCAISSÉ AVANT LA RECHERCHE
/// (point 35). Rendre la course seule laisserait le passager sans savoir où en
/// est son paiement, donc une course qui ne part jamais — et c'est exactement
/// l'écran bloqué dont le côté colis a déjà souffert.
///
/// IL N'Y A PLUS D'ADRESSE DE PAIEMENT à rendre depuis le 3 octobre 2026 : la
/// demande est poussée sur le téléphone du passager dès cet appel, et il tape
/// son code dans l'invite de son opérateur. L'identifiant de l'intention reste,
/// parce que c'est par lui que le suivi apprend si le paiement a abouti.
/// </remarks>
public sealed record RideRequestedView(
    RideView Ride,
    string PaymentIntentId,
    string PaymentRedirectUrl);

/// <summary>
/// Le chauffeur affecté, tel qu'il sort du service.
/// </summary>
///
/// <remarks>
/// <paramref name="Vehicule"/> EST CALCULÉ PAR LE DOMAINE, pas assemblé ici : les
/// trois champs qui le composent sont facultatifs, et recoller « marque modèle
/// couleur » à la main produirait « voiture   » dès que deux manquent. Voir
/// AssignedDriver.Vehicule.
/// </remarks>
public sealed record AssignedDriverView(
    string DriverId,
    string DisplayName,
    string Phone,
    VehicleType VehicleType,
    string VehiclePlate,
    string Vehicule)
{
    public static AssignedDriverView? From(AssignedDriver? chauffeur)
        => chauffeur is null
            ? null
            : new AssignedDriverView(
                chauffeur.DriverId,
                chauffeur.DisplayName,
                chauffeur.Phone,
                chauffeur.VehicleType,
                chauffeur.VehiclePlate,
                chauffeur.Vehicule());
}
