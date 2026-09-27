namespace Hba.Dispatch.Domain.Exceptions;

/// <summary>
/// Codes metier stables du service Dispatch.
///
/// LES QUATRE PREMIERS SONT UN CONTRAT AVEC L'APPLICATION LIVREUR : le
/// « rejection_code » de AcceptOfferResponse les nomme explicitement, et
/// l'ecran du livreur doit pouvoir distinguer « trop tard » de « tu portes
/// deja une course ».
/// </summary>
public static class DispatchErrorCodes
{
    public const string OfferExpired = "OFFER_EXPIRED";

    public const string AlreadyTaken = "ALREADY_TAKEN";

    public const string DriverAlreadyReserved = "DRIVER_ALREADY_RESERVED";

    public const string DriverNotAvailable = "DRIVER_NOT_AVAILABLE";

    public const string OfferNotFound = "OFFER_NOT_FOUND";

    public const string NotYourOffer = "NOT_YOUR_OFFER";

    public const string DispatchClosed = "DISPATCH_CLOSED";

    public const string NoWaveLeft = "NO_WAVE_LEFT";

    /// <summary>
    /// Ce livreur porte deja une offre en attente sur CETTE course. Le refus
    /// ne porte pas sur les offres resolues : quelqu'un qui a refuse par
    /// erreur, ou laisse expirer, peut se voir reproposer la course a la main.
    /// </summary>
    public const string AlreadyOffered = "ALREADY_OFFERED";
}
