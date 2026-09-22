using Hba.BuildingBlocks.Domain;

namespace Hba.Directory.Domain.ValueObjects;

/// <summary>
/// Adresse utilisable au Bénin : point GPS, repère écrit, téléphone joignable
/// sur place. Sans repère ni téléphone, le livreur ne trouve pas — le repère
/// est donc obligatoire, comme dans le service Delivery.
/// </summary>
public sealed class Address : ValueObject
{
    // EF Core ne sait pas passer un type possédé imbriqué à un paramètre de
    // constructeur : il matérialise d'abord l'objet, puis rattache la
    // navigation. D'où ce constructeur sans paramètre, réservé au chargement.
    // Le domaine, lui, passe toujours par Create, qui valide.
    private Address()
    {
    }

    private Address(GeoPoint point, string landmark, string phone, string contactName, string? notes)
    {
        Point = point;
        Landmark = landmark;
        Phone = phone;
        ContactName = contactName;
        Notes = notes;
    }

    public GeoPoint Point { get; private set; } = null!;

    public string Landmark { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string ContactName { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public static Address Create(
        GeoPoint point,
        string landmark,
        string phone,
        string contactName,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (string.IsNullOrWhiteSpace(landmark))
        {
            throw new DomainException(
                "MISSING_LANDMARK",
                "Un repère écrit est obligatoire : l'adressage formel n'est pas fiable.");
        }

        if (!PhoneNumber.IsValid(phone))
        {
            throw new DomainException("INVALID_PHONE", $"Téléphone invalide : {phone}.");
        }

        if (string.IsNullOrWhiteSpace(contactName))
        {
            throw new DomainException("MISSING_CONTACT_NAME", "Le nom du contact sur place est obligatoire.");
        }

        return new Address(
            point,
            landmark.Trim(),
            PhoneNumber.Normalize(phone),
            contactName.Trim(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
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
