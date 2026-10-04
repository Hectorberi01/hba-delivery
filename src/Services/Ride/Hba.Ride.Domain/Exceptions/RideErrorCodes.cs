namespace Hba.Ride.Domain.Exceptions;

/// <summary>
/// Codes de refus du service Ride.
/// </summary>
///
/// <remarks>
/// CES CODES VOYAGENT JUSQU'AU TÉLÉPHONE. L'application distingue « la course
/// n'est pas la vôtre » de « il est trop tard pour annuler » pour ne pas dire la
/// même chose au passager ; un code renommé casse cet affichage sans casser la
/// compilation.
/// </remarks>
public static class RideErrorCodes
{
    /// <summary>Le refus générique d'une transition impossible.</summary>
    ///
    /// <remarks>
    /// LE MÊME CODE QUE LE RESTE DU DÉPÔT, ET C'EST VOULU. Delivery lève
    /// <c>InvalidStateTransitionException</c>, traduite en
    /// <c>FAILED_PRECONDITION</c> / 409. Ride rend un <c>Result</c>, mais avec
    /// le même code : le dehors ne doit rien voir de la différence.
    /// </remarks>
    public const string InvalidTransition = "INVALID_STATE_TRANSITION";

    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>Le chauffeur n'est pas encore sur place : la course ne part pas.</summary>
    public const string DriverNotArrived = "DRIVER_NOT_ARRIVED";

    /// <summary>Une course déjà close ne s'annule pas.</summary>
    public const string RideAlreadyClosed = "RIDE_ALREADY_CLOSED";

    /// <summary>Une course commencée ne s'annule plus : elle se termine.</summary>
    public const string RideAlreadyStarted = "RIDE_ALREADY_STARTED";

    public const string DriverAlreadyAssigned = "DRIVER_ALREADY_ASSIGNED";

    public const string InvalidCoordinates = "INVALID_COORDINATES";

    public const string InvalidAmount = "INVALID_AMOUNT";

    public const string SamePickupAndDestination = "SAME_PICKUP_AND_DESTINATION";
}
