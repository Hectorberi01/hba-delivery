using Hba.BuildingBlocks.Domain;

namespace Hba.Delivery.Domain.ValueObjects;

/// <summary>
/// Adresse utilisable au Bénin. L'adressage formel étant peu fiable, une adresse
/// est un point GPS, un repère écrit et un téléphone joignable sur place. Les
/// trois sont obligatoires : sans repère ni téléphone, le livreur ne trouve pas.
/// </summary>
public sealed class Location : ValueObject
{
    // EF Core ne sait pas passer un type possédé imbriqué à un paramètre de
    // constructeur : il matérialise d'abord l'objet, puis rattache la
    // navigation. D'où ce constructeur sans paramètre, réservé au chargement.
    // Le domaine, lui, passe toujours par Create, qui valide.
    private Location()
    {
    }

    private Location(GeoPoint point, string landmark, string phone, string contactName, string? notes)
    {
        Point = point;
        Landmark = landmark;
        Phone = phone;
        ContactName = contactName;
        Notes = notes;
    }

    public GeoPoint Point { get; private set; } = null!;

    /// <summary>Repère écrit, ex. « Carré 442, en face de la pharmacie ».</summary>
    public string Landmark { get; private set; } = string.Empty;

    /// <summary>Téléphone de la personne présente sur place, format E.164.</summary>
    public string Phone { get; private set; } = string.Empty;

    public string ContactName { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public static Location Create(GeoPoint point, string landmark, string phone, string contactName, string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (string.IsNullOrWhiteSpace(landmark))
        {
            throw new DomainException("MISSING_LANDMARK", "Un repère écrit est obligatoire : l'adressage formel n'est pas fiable.");
        }

        if (!PhoneNumber.IsValid(phone))
        {
            throw new DomainException("INVALID_PHONE", $"Téléphone invalide : {phone}. Format attendu : E.164.");
        }

        if (string.IsNullOrWhiteSpace(contactName))
        {
            throw new DomainException("MISSING_CONTACT_NAME", "Le nom du contact sur place est obligatoire.");
        }

        return new Location(point, landmark.Trim(), PhoneNumber.Normalize(phone), contactName.Trim(), notes?.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Point;
        yield return Landmark;
        yield return Phone;
        yield return ContactName;
        yield return Notes;
    }
}
