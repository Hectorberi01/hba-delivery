namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Etat du dossier du livreur. Les valeurs et les transitions sont celles du
/// referentiel acteurs : PENDING_VERIFICATION vers VERIFIED, REJECTED ou
/// SUSPENDED.
/// </summary>
public enum VerificationStatus
{
    Unspecified = 0,
    PendingVerification = 1,
    Verified = 2,
    Rejected = 3,
    Suspended = 4,
}

/// <summary>
/// Etat de travail du livreur : OFFLINE vers AVAILABLE vers RESERVED vers
/// ON_MISSION vers AVAILABLE.
/// </summary>
public enum OperationalStatus
{
    Unspecified = 0,
    Offline = 1,
    Available = 2,

    /// <summary>Une offre lui est reservee. Il ne peut pas en recevoir une autre.</summary>
    Reserved = 3,

    OnMission = 4,
}

public enum VehicleType
{
    Unspecified = 0,
    Motorcycle = 1,
    Car = 2,
    Van = 3,
}
