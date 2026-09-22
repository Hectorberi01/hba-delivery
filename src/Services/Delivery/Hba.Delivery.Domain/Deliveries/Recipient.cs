using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// Personne qui reçoit physiquement le colis. Ce n'est PAS un compte, et il ne
/// faut jamais lui en créer un implicitement : ses données sont figées dans la
/// livraison au moment de la demande.
/// </summary>
public sealed class Recipient : ValueObject
{
    private Recipient(string name, string phone)
    {
        Name = name;
        Phone = phone;
    }

    public string Name { get; }

    public string Phone { get; }

    public static Recipient Create(string name, string phone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("MISSING_RECIPIENT_NAME", "Le nom du destinataire est obligatoire.");
        }

        if (!PhoneNumber.IsValid(phone))
        {
            throw new DomainException(
                "INVALID_RECIPIENT_PHONE",
                "Le destinataire doit avoir un téléphone valide : c'est par là qu'il reçoit le code de remise.");
        }

        return new Recipient(name.Trim(), PhoneNumber.Normalize(phone));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Phone;
    }
}
