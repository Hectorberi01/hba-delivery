using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Common.Views;

/// <summary>
/// Une piece, telle qu'on la montre.
///
/// L'URL EST SIGNEE ET COURTE ; la cle de stockage, elle, ne sort jamais. Elle
/// serait devinable d'un livreur a l'autre, et le tableau de visibilite du
/// referentiel reserve les pieces a leur proprietaire et a l'admin.
/// </summary>
public sealed record DocumentView(
    DocumentType Type,
    DateTimeOffset UploadedAt,
    long SizeBytes,
    string ContentType,
    Uri ReadUrl,
    DateTimeOffset ReadUrlExpiresAt);

/// <summary>Le dossier complet, vu par son proprietaire ou par ops.</summary>
public sealed record DriverApplicationView(
    Guid DriverId,
    VerificationStatus VerificationStatus,
    string StatusReason,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<DocumentView> Documents,
    IReadOnlyList<DocumentType> MissingDocuments,

    /// <summary>
    /// Pieces exigees POUR CE VEHICULE, deposees ou non.
    ///
    /// ELLE A CESSE D'ETRE LA MEME POUR TOUT LE MONDE le 30 septembre 2026 :
    /// un velo n'exige ni permis ni carte grise. L'application l'affichait en
    /// dur, et un cycliste y aurait vu deux lignes impossibles a satisfaire.
    /// </summary>
    IReadOnlyList<DocumentType> RequiredDocuments,
    VehicleType VehicleType,
    string VehiclePlate,
    int VehicleCapacityGrams,
    Uri? ProfilePhotoUrl,

    /// <summary>
    /// Le vehicule a-t-il ete declare ? L'application le devinait par « la
    /// plaque n'est pas vide » ; un velo n'en a pas.
    /// </summary>
    bool VehicleDeclared,

    /// <summary>
    /// CALCULE PAR LE SERVICE, PAS PAR LE CLIENT. La regle « complet, ou
    /// rien » est metier ; chaque client qui la recalculerait finirait par en
    /// avoir sa propre version, et deux d'entre elles seraient fausses.
    /// </summary>
    bool CanSubmit);
