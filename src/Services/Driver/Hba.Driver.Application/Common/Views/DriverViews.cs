using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Common.Views;

public sealed record DriverView(
    Guid Id,
    string DisplayName,
    string Phone,
    VehicleType VehicleType,
    string VehiclePlate,
    int VehicleCapacityGrams,
    VerificationStatus VerificationStatus,
    OperationalStatus OperationalStatus,
    string StatusReason,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? VerifiedAt,

    // Le vehicule, en clair. CE QUI PERMET AU CLIENT DE RECONNAITRE CE QUI
    // ARRIVE : « la voiture bleue » se lit a trente metres, une plaque non.
    string VehicleBrand,
    string VehicleModel,
    string VehicleColor,
    int VehiclePassengerCapacity,

    // VIDE VEUT DIRE « LE COLIS » : c'est le socle, il ne se declare pas.
    IReadOnlyCollection<DriverCapability> Capabilities)
{
    public static DriverView From(DriverAggregate driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        return new DriverView(
            driver.Id,
            driver.DisplayName,
            driver.Phone,
            driver.Vehicle.Type,
            driver.Vehicle.Plate,
            driver.Vehicle.CapacityGrams,
            driver.VerificationStatus,
            driver.OperationalStatus,
            driver.StatusReason ?? string.Empty,
            driver.RegisteredAt,
            driver.VerifiedAt,
            driver.Vehicle.Brand,
            driver.Vehicle.Model,
            driver.Vehicle.Color,
            driver.Vehicle.PassengerCapacity,
            driver.Capabilities);
    }
}

/// <summary>Une page de l'annuaire, et le total du filtre.</summary>
public sealed record DriverPageView(IReadOnlyList<DriverView> Drivers, int Total);

/// <summary>
/// Ce que le client et le commercant ont le droit de voir pendant la mission :
/// nom, vehicule, telephone. RIEN DE PLUS NE DOIT TRANSITER — ni le dossier,
/// ni l'etat operationnel, ni la position historique.
/// </summary>
public sealed record DriverPublicProfileView(
    Guid DriverId,
    string DisplayName,
    string Phone,
    VehicleType VehicleType,
    string VehiclePlate,

    /// <summary>
    /// Marque, modele et couleur. DECLARATIFS, ET VIDES S'ILS N'ONT PAS ETE
    /// DECLARES : ils servent au passager a reconnaitre le vehicule qui arrive,
    /// pas a autoriser quoi que ce soit.
    /// </summary>
    string VehicleBrand,
    string VehicleModel,
    string VehicleColor)
{
    public static DriverPublicProfileView From(DriverAggregate driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        return new DriverPublicProfileView(
            driver.Id,
            driver.DisplayName,
            driver.Phone,
            driver.Vehicle.Type,
            driver.Vehicle.Plate,
            driver.Vehicle.Brand,
            driver.Vehicle.Model,
            driver.Vehicle.Color);
    }
}

/// <summary>
/// Un livreur qui peut recevoir une offre. NI DISTANCE NI COORDONNEES.
/// </summary>
///
/// <remarks>
/// ELLE REMPLACE NearbyDriverView, QUI PORTAIT UNE POSITION. Depuis que les positions
/// ont quitte ce service, Driver ne peut plus rien dire du lieu : la distance et les
/// coordonnees viennent de Location, et c'est l'appelant qui les recolle a ce verdict.
///
/// LE VEHICULE RESTE, pour la carte du client. Point 24, revise le 30 septembre 2026 :
/// c'est le seul attribut du livreur qui sorte avant l'affectation, et il sort SANS son
/// identifiant — c'est la passerelle qui le jette, pas ce service.
/// </remarks>
public sealed record EligibleDriverView(Guid DriverId, VehicleType VehicleType);

/// <summary>
/// Une pastille de la carte du client : avec quoi il roule, et s'il peut
/// prendre une mission maintenant.
/// </summary>
///
/// <remarks>
/// NI NOM, NI PLAQUE, NI TELEPHONE, et ce n'est pas un oubli a combler : cette
/// vue sort vers un client, pas vers le back-office. L'identifiant ne sert qu'a
/// recoller la pastille a sa position, et la passerelle le jette avant l'app.
/// </remarks>
public sealed record DriverPinView(Guid DriverId, VehicleType VehicleType, bool Available);

/// <summary>
/// Qui est ce livreur, pour la carte de l'exploitation.
/// </summary>
///
/// <remarks>
/// ELLE NE PORTE PAS DE POSITION, ET C'EST LA MOITIE D'UN ECRAN. L'autre moitie vient
/// de Location ; la passerelle les assemble. Ce decoupage est ce qui permet a Location
/// de tenir des points sans tenir de noms.
///
/// LE TELEPHONE N'Y EST PAS. La carte sert a voir ou se trouvent les gens ; appeler
/// quelqu'un passe par l'annuaire, qui est une autre lecture et une autre decision.
/// </remarks>
public sealed record DriverSummaryView(
    Guid DriverId,
    string DisplayName,
    VerificationStatus VerificationStatus,
    OperationalStatus OperationalStatus,
    VehicleType VehicleType);

/// <summary>
/// Reponse a « CE livreur peut-il recevoir une offre ».
/// </summary>
///
/// <remarks>
/// ELLE PORTE LE MOTIF DU REFUS, ET C'EST TOUT L'INTERET. Un booleen seul laisserait
/// l'exploitation devant un bouton grise sans explication ; or les motifs appellent
/// des gestes differents — valider un dossier, attendre la fin d'une course, rappeler
/// quelqu'un.
///
/// ELLE NE DIT PLUS « LE TELEPHONE EST ETEINT ». Ce motif-la — DRIVER_POSITION_STALE —
/// appartient desormais a celui qui croise : il interroge Location, et conclut
/// lui-meme. Driver qui le prononcerait pretendrait savoir ou se trouve quelqu'un, ce
/// qu'il ne sait plus depuis le 1er octobre 2026.
/// </remarks>
public sealed record DriverAvailabilityView(
    Guid DriverId,
    string DisplayName,
    bool Offerable,
    string? Reason,
    VerificationStatus VerificationStatus,
    OperationalStatus OperationalStatus);
