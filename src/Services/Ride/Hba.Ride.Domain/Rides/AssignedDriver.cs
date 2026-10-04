using Hba.BuildingBlocks.Domain;

namespace Hba.Ride.Domain.Rides;

/// <summary>
/// Copie locale du strict nécessaire sur le chauffeur affecté.
/// </summary>
///
/// <remarks>
/// POURQUOI UNE COPIE PLUTÔT QU'UN APPEL À CHAQUE LECTURE. La passerelle
/// interrogeait Driver à CHAQUE lecture d'une course pour afficher le nom du
/// chauffeur : un écran de suivi qui rafraîchit toutes les cinq secondes faisait
/// donc cinq appels par minute et par passager, et Driver indisponible une seconde
/// effaçait le nom de l'écran. L'instantané supprime les deux.
///
/// IL EST AUSSI CE QUI PERMET D'ENVOYER UN SMS. « {driver} vient vous chercher,
/// appelez-le au {phone} » ne s'écrit pas avec un identifiant : le texte part au
/// moment de l'affectation, et aller chercher le profil à cet instant mettrait un
/// appel réseau dans la transaction qui affecte le chauffeur.
///
/// MARQUE, MODÈLE ET COULEUR SONT ICI, contrairement au colis. Un passager au bord
/// d'une rue de Cotonou cherche « la voiture blanche » ; une immatriculation ne se
/// lit pas à trente mètres. Ce sont des champs DÉCLARATIFS : ils peuvent être
/// vides, et ils n'autorisent rien.
///
/// RIEN D'AUTRE NE REMONTE ICI. Le passager ne voit pas plus, et Ride n'a pas à
/// connaître le dossier d'un chauffeur.
/// </remarks>
public sealed class AssignedDriver : ValueObject
{
    private AssignedDriver()
    {
    }

    private AssignedDriver(
        string driverId,
        string displayName,
        string phone,
        VehicleType vehicleType,
        string vehiclePlate,
        string vehicleBrand,
        string vehicleModel,
        string vehicleColor)
    {
        DriverId = driverId;
        DisplayName = displayName;
        Phone = phone;
        VehicleType = vehicleType;
        VehiclePlate = vehiclePlate;
        VehicleBrand = vehicleBrand;
        VehicleModel = vehicleModel;
        VehicleColor = vehicleColor;
    }

    public string DriverId { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public VehicleType VehicleType { get; private set; }

    public string VehiclePlate { get; private set; } = string.Empty;

    public string VehicleBrand { get; private set; } = string.Empty;

    public string VehicleModel { get; private set; } = string.Empty;

    public string VehicleColor { get; private set; } = string.Empty;

    /// <summary>
    /// Ce que le passager lit pour reconnaître le véhicule.
    /// </summary>
    ///
    /// <remarks>
    /// CALCULÉ ICI ET NON DANS UN MODÈLE DE SMS, parce que les champs sont
    /// facultatifs : « Toyota Corolla blanche » quand tout est déclaré, « voiture
    /// blanche » quand la marque manque, « voiture » quand rien n'est déclaré. Un
    /// modèle de message qui substituerait trois variables vides écrirait « vient
    /// vous chercher en   » — trois espaces et un vide.
    /// </remarks>
    public string Vehicule()
    {
        var morceaux = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(VehicleBrand)) { morceaux.Add(VehicleBrand.Trim()); }
        if (!string.IsNullOrWhiteSpace(VehicleModel)) { morceaux.Add(VehicleModel.Trim()); }

        if (morceaux.Count == 0)
        {
            morceaux.Add(Libelle(VehicleType));
        }

        if (!string.IsNullOrWhiteSpace(VehicleColor)) { morceaux.Add(VehicleColor.Trim()); }

        return string.Join(' ', morceaux);
    }

    public static AssignedDriver Create(
        string driverId,
        string displayName,
        string? phone,
        VehicleType vehicleType,
        string? vehiclePlate,
        string? vehicleBrand = null,
        string? vehicleModel = null,
        string? vehicleColor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new AssignedDriver(
            driverId.Trim(),
            displayName.Trim(),
            phone?.Trim() ?? string.Empty,
            vehicleType,
            vehiclePlate?.Trim() ?? string.Empty,
            vehicleBrand?.Trim() ?? string.Empty,
            vehicleModel?.Trim() ?? string.Empty,
            vehicleColor?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// SANS ACCENT, ET CE N'EST PAS UNE NEGLIGENCE. Ce libellé part dans un SMS :
    /// un seul « é » fait basculer le message de GSM-7 en UCS-2, qui ne tient plus
    /// que 70 caractères par segment au lieu de 160. Voir TemplateCatalog.
    /// </summary>
    private static string Libelle(VehicleType type) => type switch
    {
        VehicleType.Motorcycle => "moto",
        VehicleType.Car => "voiture",
        VehicleType.Tricycle => "tricycle",
        _ => "vehicule",
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DriverId;
        yield return DisplayName;
        yield return Phone;
        yield return VehicleType;
        yield return VehiclePlate;
        yield return VehicleBrand;
        yield return VehicleModel;
        yield return VehicleColor;
    }
}
