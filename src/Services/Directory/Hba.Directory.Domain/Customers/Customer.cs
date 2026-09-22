using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.Customers.Events;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Domain.Customers;

/// <summary>
/// Profil d'un client donneur d'ordre. L'identifiant est celui du compte
/// Identity : un compte, un profil, et aucune duplication de l'identité.
///
/// Directory ne détient ni mot de passe, ni jeton, ni rôle. Il ne détient pas
/// non plus le destinataire d'une livraison : celui-ci n'est pas un utilisateur
/// et ne doit jamais se voir créer un profil implicitement.
/// </summary>
public sealed class Customer : AggregateRoot
{
    /// <summary>
    /// Au-delà, la liste devient un annuaire personnel et l'écran illisible.
    /// </summary>
    public const int MaxFavoriteAddresses = 15;

    private readonly List<FavoriteAddress> _favoriteAddresses = [];

    private Customer()
    {
    }

    private Customer(Guid id, string displayName, string phone, string? email, DateTimeOffset createdAt)
        : base(id)
    {
        DisplayName = displayName;
        Phone = phone;
        Email = email;
        CreatedAt = createdAt;
    }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Recopié depuis Identity à la création. Identity reste la référence.</summary>
    public string Phone { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    /// <summary>
    /// Exposée directement plutôt qu'en copie figée : EF Core écrit dans le
    /// champ, et une copie par lecture ne protégerait de rien puisque les
    /// modifications passent par les méthodes de l'agrégat.
    /// </summary>
    public IReadOnlyList<FavoriteAddress> FavoriteAddresses => _favoriteAddresses;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Créé à la réception de l'événement AccountRegistered d'Identity. C'est le
    /// seul chemin : aucun profil ne naît d'une demande de livraison.
    /// </summary>
    public static Customer CreateFromAccount(
        Guid accountId,
        string displayName,
        string phone,
        string? email,
        Actor actor,
        DateTimeOffset createdAt)
    {
        if (!PhoneNumber.IsValid(phone))
        {
            throw new DomainException("INVALID_PHONE", $"Téléphone invalide : {phone}.");
        }

        var customer = new Customer(
            accountId,
            string.IsNullOrWhiteSpace(displayName) ? "Sans nom" : displayName.Trim(),
            PhoneNumber.Normalize(phone),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            createdAt);

        customer.Raise(new CustomerProfileCreated(accountId, customer.Phone, actor, createdAt));

        return customer;
    }

    public void UpdateProfile(string? displayName, string? email, Actor actor, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            DisplayName = displayName.Trim();
        }

        if (email is not null)
        {
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        }

        Raise(new CustomerProfileUpdated(Id, actor, now));
    }

    public FavoriteAddress AddFavoriteAddress(string label, Address address, bool setAsDefault, DateTimeOffset now)
    {
        if (_favoriteAddresses.Count >= MaxFavoriteAddresses)
        {
            throw new DomainException(
                "TOO_MANY_ADDRESSES",
                $"Un client ne garde pas plus de {MaxFavoriteAddresses} adresses favorites.");
        }

        // La première adresse enregistrée devient la principale : sans cela, un
        // client qui n'en a qu'une n'en aurait aucune par défaut.
        var isDefault = setAsDefault || _favoriteAddresses.Count == 0;

        var favorite = FavoriteAddress.Create(label, address, isDefault, now);

        if (isDefault)
        {
            ClearDefaults();
            favorite.MarkDefault(true);
        }

        _favoriteAddresses.Add(favorite);

        return favorite;
    }

    public void UpdateFavoriteAddress(Guid addressId, string label, Address address, bool setAsDefault)
    {
        var favorite = FindAddress(addressId);
        favorite.Update(label, address);

        if (setAsDefault)
        {
            ClearDefaults();
            favorite.MarkDefault(true);
        }
    }

    public void RemoveFavoriteAddress(Guid addressId)
    {
        var favorite = FindAddress(addressId);
        var wasDefault = favorite.IsDefault;

        _favoriteAddresses.Remove(favorite);

        // Retirer l'adresse principale ne doit pas laisser le client sans
        // adresse par défaut s'il lui en reste.
        if (wasDefault && _favoriteAddresses.Count > 0)
        {
            _favoriteAddresses[0].MarkDefault(true);
        }
    }

    private FavoriteAddress FindAddress(Guid addressId)
        => _favoriteAddresses.FirstOrDefault(a => a.Id == addressId)
           ?? throw new NotFoundException("Adresse favorite", addressId.ToString());

    private void ClearDefaults()
    {
        foreach (var existing in _favoriteAddresses)
        {
            existing.MarkDefault(false);
        }
    }
}
