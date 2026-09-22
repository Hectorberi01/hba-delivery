using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Domain.Customers;

/// <summary>
/// Adresse enregistrée par le client : « maison », « bureau », « chez maman ».
/// Elle sert à pré-remplir une demande de livraison, jamais à la valider : au
/// moment de la course, l'adresse est recopiée dans la livraison et figée.
/// </summary>
public sealed class FavoriteAddress : Entity
{
    private FavoriteAddress()
    {
    }

    private FavoriteAddress(Guid id, string label, Address address, bool isDefault, DateTimeOffset createdAt)
        : base(id)
    {
        Label = label;
        Address = address;
        IsDefault = isDefault;
        CreatedAt = createdAt;
    }

    public string Label { get; private set; } = string.Empty;

    public Address Address { get; private set; } = null!;

    public bool IsDefault { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static FavoriteAddress Create(string label, Address address, bool isDefault, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new DomainException("MISSING_LABEL", "Une adresse favorite doit porter un nom.");
        }

        return new FavoriteAddress(Guid.CreateVersion7(), label.Trim(), address, isDefault, createdAt);
    }

    internal void Update(string label, Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new DomainException("MISSING_LABEL", "Une adresse favorite doit porter un nom.");
        }

        Label = label.Trim();
        Address = address;
    }

    internal void MarkDefault(bool isDefault) => IsDefault = isDefault;
}
