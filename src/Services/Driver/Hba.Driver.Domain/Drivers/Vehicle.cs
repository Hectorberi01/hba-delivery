using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Vehicule declare par le livreur. La capacite sert au dispatch : un colis de
/// vingt kilos ne part pas sur une moto.
/// </summary>
public sealed class Vehicle : ValueObject
{
    private Vehicle(VehicleType type, string plate, int capacityGrams)
    {
        Type = type;
        Plate = plate;
        CapacityGrams = capacityGrams;
    }

    public VehicleType Type { get; }

    /// <summary>Immatriculation. Vide tant que le livreur ne l'a pas declaree.</summary>
    public string Plate { get; }

    public int CapacityGrams { get; }

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
    public static readonly IReadOnlySet<VehicleType> SansPlaque =
        new HashSet<VehicleType> { VehicleType.Bicycle };

    /// <summary>Ce vehicule doit-il porter une immatriculation ?</summary>
    public bool ExigeUnePlaque => !SansPlaque.Contains(Type);

    /// <summary>
    /// Le livreur a-t-il declare son vehicule ?
    /// </summary>
    ///
    /// <remarks>
    /// LA PLAQUE FAISAIT FOI, ET ELLE NE PEUT PLUS. C'etait le seul signal
    /// disponible : le type NAIT a « Motorcycle » (voir <see cref="Unknown"/>),
    /// donc il ne distingue pas un motard declare d'un livreur qui n'a rien
    /// dit. Un velo, lui, n'a pas de plaque — il serait reste « non declare »
    /// pour toujours, et son dossier n'aurait jamais pu partir a l'examen.
    ///
    /// CE QUI REND CETTE REGLE SURE : aucun vehicule ne NAIT sans plaque. Le
    /// defaut est une moto, qui en exige une ; un type sans plaque ne peut donc
    /// avoir ete pose que par une declaration.
    /// </remarks>
    public bool EstDeclare => !ExigeUnePlaque || !string.IsNullOrWhiteSpace(Plate);

    /// <summary>
    /// Vehicule par defaut a l'inscription : une moto, sans immatriculation ni
    /// capacite declaree.
    ///
    /// LE ZEMIDJAN EST LE MODE PAR DEFAUT A COTONOU, comme pour la grille
    /// tarifaire. Un profil qui naitrait sans vehicule ne pourrait recevoir
    /// aucune offre, et le livreur ne comprendrait pas pourquoi.
    /// </summary>
    public static Vehicle Unknown => new(VehicleType.Motorcycle, string.Empty, 0);

    public static Vehicle Create(VehicleType type, string? plate, int capacityGrams)
    {
        if (capacityGrams < 0)
        {
            throw new DomainException(
                DriverErrorCodes.InvalidVehicleCapacity,
                "La capacite d'un vehicule ne peut pas etre negative.");
        }

        return new Vehicle(type, plate?.Trim() ?? string.Empty, capacityGrams);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Plate;
        yield return CapacityGrams;
    }
}
