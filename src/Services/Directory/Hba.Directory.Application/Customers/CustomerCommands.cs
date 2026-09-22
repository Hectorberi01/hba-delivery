using Hba.BuildingBlocks.Application.Messaging;
using Hba.Directory.Application.Views;

namespace Hba.Directory.Application.Customers;

public sealed record AddressInput(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record GetCustomerQuery(string? CustomerId) : IQuery<CustomerView>;

public sealed record UpdateCustomerCommand(string? DisplayName, string? Email) : ICommand<CustomerView>;

public sealed record AddFavoriteAddressCommand(string Label, AddressInput Address, bool SetAsDefault)
    : ICommand<CustomerView>;

public sealed record UpdateFavoriteAddressCommand(
    Guid AddressId,
    string Label,
    AddressInput Address,
    bool SetAsDefault) : ICommand<CustomerView>;

public sealed record RemoveFavoriteAddressCommand(Guid AddressId) : ICommand<CustomerView>;

/// <summary>
/// Déclenchée par l'événement AccountRegistered d'Identity. C'est le seul
/// chemin de création d'un profil client.
/// </summary>
public sealed record CreateCustomerProfileCommand(
    Guid AccountId,
    string DisplayName,
    string Phone,
    string? Email,
    DateTimeOffset RegisteredAt) : ICommand<Unit>;
