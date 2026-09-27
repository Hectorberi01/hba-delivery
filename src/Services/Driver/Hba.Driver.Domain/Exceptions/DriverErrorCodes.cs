namespace Hba.Driver.Domain.Exceptions;

/// <summary>
/// Codes metier stables du service Driver. Ce sont eux que les appelants
/// lisent, pas les messages en francais.
/// </summary>
public static class DriverErrorCodes
{
    public const string MissingPhone = "MISSING_PHONE";

    public const string MissingDisplayName = "MISSING_DISPLAY_NAME";

    public const string DriverNotVerified = "DRIVER_NOT_VERIFIED";

    public const string DriverSuspended = "DRIVER_SUSPENDED";

    public const string DriverNotAvailable = "DRIVER_NOT_AVAILABLE";

    public const string DriverAlreadyReserved = "DRIVER_ALREADY_RESERVED";

    public const string MissingRejectionReason = "MISSING_REJECTION_REASON";

    public const string InvalidVehicleCapacity = "INVALID_VEHICLE_CAPACITY";

    // --- Dossier du livreur (ADR 0021) ---

    public const string UnknownDocumentType = "UNKNOWN_DOCUMENT_TYPE";

    public const string MissingObjectKey = "MISSING_OBJECT_KEY";

    public const string EmptyDocument = "EMPTY_DOCUMENT";

    public const string MissingVehiclePlate = "MISSING_VEHICLE_PLATE";

    /// <summary>Le dossier ne peut pas partir a l'examen : il lui manque une piece.</summary>
    public const string IncompleteApplication = "INCOMPLETE_APPLICATION";

    /// <summary>Le dossier est deja en cours d'examen, ou deja valide.</summary>
    public const string ApplicationNotEditable = "APPLICATION_NOT_EDITABLE";

    /// <summary>
    /// Deux ecritures concurrentes sur le meme dossier : celle-ci a perdu.
    /// C'est le jeton xmin qui tranche (ADR 0020) ; l'appelant peut relire et
    /// recommencer.
    /// </summary>
    public const string ConcurrentModification = "CONCURRENT_MODIFICATION";
}
