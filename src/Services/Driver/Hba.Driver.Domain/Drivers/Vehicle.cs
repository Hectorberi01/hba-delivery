using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Vehicule declare par le livreur. La capacite sert au dispatch : un colis de
/// vingt kilos ne part pas sur une moto.
/// </summary>
public sealed class Vehicle : ValueObject
{
    private Vehicle(
        VehicleType type,
        string plate,
        int capacityGrams,
        string brand,
        string model,
        string color,
        int passengerCapacity)
    {
        Type = type;
        Plate = plate;
        CapacityGrams = capacityGrams;
        Brand = brand;
        Model = model;
        Color = color;
        PassengerCapacity = passengerCapacity;
    }

    public VehicleType Type { get; }

    /// <summary>Immatriculation. Vide tant que le livreur ne l'a pas declaree.</summary>
    public string Plate { get; }
    public int CapacityGrams { get; }

    /// <summary>Marque, telle que le livreur la declare. Vide tant qu'il ne l'a pas dite.</summary>
    public string Brand { get; } = string.Empty;
    public string Model { get; } = string.Empty;

    /// <summary>
    /// Couleur, en clair.
    ///
    /// ELLE N'EST PAS DECORATIVE : c'est ce qui permet au client d'identifier
    /// le vehicule qui arrive, bien avant la plaque. « La voiture bleue » se
    /// lit a trente metres, une immatriculation non.
    /// </summary>
    public string Color { get; } = string.Empty;

    /// <summary>
    /// Nombre de personnes transportables, conducteur non compris.
    ///
    /// ZERO N'EST PAS « INCONNU », C'EST « AUCUNE ». Un vehicule dont personne
    /// n'a declare la capacite passagers ne peut en prendre aucun : c'est le
    /// repli sur. Voir <see cref="PeutTransporterDesPersonnes"/>, qui exige les
    /// deux — un type autorise ET au moins une place.
    /// </summary>
    public int PassengerCapacity { get; }

    /// <summary>
    /// Les types qui ne portent pas d'immatriculation.
    /// </summary>
    ///
    /// <remarks>
    /// UN SEUL A CE JOUR, ET L'ENSEMBLE EXISTE QUAND MEME : ecrire
    /// « type == Bicycle » aux trois endroits qui posent la question
    /// garantirait qu'un quatrieme oublie le jour ou un second type sans moteur
    /// arrive. C'est ici, et nulle part ailleurs.
    /// </remarks>
    public static readonly IReadOnlySet<VehicleType> SansPlaque = new HashSet<VehicleType> { VehicleType.Bicycle };

    /// <summary>Ce vehicule doit-il porter une immatriculation ?</summary>
    public bool ExigeUnePlaque => !SansPlaque.Contains(Type);

    /// <summary>
    /// Ce que chaque type de vehicule a le droit de faire.
    /// </summary>
    private static readonly IReadOnlySet<DriverCapability> Toutes =
        new HashSet<DriverCapability>
        {
            DriverCapability.PackageDelivery,
            DriverCapability.FoodDelivery,
            DriverCapability.PassengerTransport,
        };

    private static readonly IReadOnlySet<DriverCapability> SansPassagers =
        new HashSet<DriverCapability>
        {
            DriverCapability.PackageDelivery,
            DriverCapability.FoodDelivery,
        };

    private static readonly IReadOnlySet<DriverCapability> Aucune =
        new HashSet<DriverCapability>();

    private static readonly IReadOnlyDictionary<VehicleType, IReadOnlySet<DriverCapability>> Capacites =
        new Dictionary<VehicleType, IReadOnlySet<DriverCapability>>
        {
            [VehicleType.Motorcycle] = Toutes,
            [VehicleType.Car] = Toutes,
            [VehicleType.Tricycle] = Toutes,
            [VehicleType.Van] = SansPassagers,
            [VehicleType.Bicycle] = SansPassagers,
        };

    /// <summary>Ce que ce type de vehicule autorise.</summary>
    ///
    /// <remarks>
    /// UN TYPE INCONNU N'AUTORISE RIEN. Rendre « tout » par defaut ouvrirait le
    /// transport de personnes a une valeur d'enum ajoutee sans qu'on y pense —
    /// exactement le genre d'oubli qui ne se voit qu'apres.
    /// </remarks>
    public static IReadOnlySet<DriverCapability> CapacitesDe(VehicleType type) =>
        Capacites.TryGetValue(type, out var capacites) ? capacites : Aucune;

    /// <summary>Ce vehicule-ci autorise-t-il cette activite ?</summary>
    public bool Autorise(DriverCapability capacite) => CapacitesDe(Type).Contains(capacite);

    /// <summary>
    /// Peut-il reellement prendre quelqu'un ?
    /// </summary>
    public bool PeutTransporterDesPersonnes =>
        Autorise(DriverCapability.PassengerTransport) && PassengerCapacity > 0;

    /// <summary>
    /// Le livreur a-t-il declare son vehicule ?
    /// </summary>
    public bool EstDeclare => !ExigeUnePlaque || !string.IsNullOrWhiteSpace(Plate);

    /// <summary>
    /// Vehicule par defaut a l'inscription : une moto, sans immatriculation ni
    /// capacite declaree.
    ///
    /// LE ZEMIDJAN EST LE MODE PAR DEFAUT A COTONOU, comme pour la grille
    /// tarifaire. Un profil qui naitrait sans vehicule ne pourrait recevoir
    /// aucune offre, et le livreur ne comprendrait pas pourquoi.
    /// </summary>
    public static Vehicle Unknown =>
        new(VehicleType.Motorcycle, string.Empty, 0, string.Empty, string.Empty, string.Empty, 0);

    public static Vehicle Create(
        VehicleType type,
        string? plate,
        int capacityGrams,
        string? brand = null,
        string? model = null,
        string? color = null,
        int passengerCapacity = 0)
    {
        if (capacityGrams < 0)
        {
            throw new DomainException(
                DriverErrorCodes.InvalidVehicleCapacity,
                "La capacite d'un vehicule ne peut pas etre negative.");
        }

        if (passengerCapacity < 0)
        {
            throw new DomainException(
                DriverErrorCodes.InvalidVehicleCapacity,
                "Un vehicule ne peut pas transporter un nombre negatif de personnes.");
        }

        // DECLARER DES PLACES DANS UN VELO EST REFUSE, PAS IGNORE. Le taire
        // laisserait croire la declaration acceptee, et le livreur chercherait
        // longtemps pourquoi il ne recoit aucune course de personnes.
        if (passengerCapacity > 0 && !CapacitesDe(type).Contains(DriverCapability.PassengerTransport))
        {
            throw new DomainException(
                DriverErrorCodes.VehicleCannotCarryPassengers,
                "Ce type de vehicule ne transporte pas de personnes.");
        }

        return new Vehicle(
            type,
            plate?.Trim() ?? string.Empty,
            capacityGrams,
            brand?.Trim() ?? string.Empty,
            model?.Trim() ?? string.Empty,
            color?.Trim() ?? string.Empty,
            passengerCapacity);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Plate;
        yield return CapacityGrams;
        yield return Brand;
        yield return Model;
        yield return Color;
        yield return PassengerCapacity;
    }
}
