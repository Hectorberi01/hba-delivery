using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Directory.Application.Authorization;
using Hba.Directory.Application.Ports;
using Hba.Directory.Application.Views;
using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hba.Directory.Application.Customers;

/// <summary>Conversion partagée d'une adresse reçue en objet-valeur du domaine.</summary>
internal static class AddressFactory
{
    public static Address From(AddressInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Address.Create(
            GeoPoint.Create(input.Latitude, input.Longitude),
            input.Landmark,
            input.Phone,
            input.ContactName,
            input.Notes);
    }
}

public sealed class GetCustomerHandler(
    ICustomerRepository customers,
    ICallerContext caller) : IQueryHandler<GetCustomerQuery, CustomerView>
{
    public async Task<CustomerView> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = DirectoryAccess.ResolveCustomerId(caller, query.CustomerId);

        var customer = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        return DirectoryViewMapper.ToView(customer);
    }
}

/// <summary>
/// Socle des commandes qui portent sur le profil de l'appelant : charger,
/// appliquer, enregistrer.
/// </summary>
public abstract class CustomerCommandHandlerBase(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
{
    protected IClock Clock => clock;

    protected ICallerContext Caller => caller;

    protected async Task<CustomerView> ApplyAsync(
        Action<Customer> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var id = DirectoryAccess.ResolveCustomerId(caller, null);

        var customer = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", id.ToString());

        apply(customer);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DirectoryViewMapper.ToView(customer);
    }
}

public sealed class UpdateCustomerHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : CustomerCommandHandlerBase(customers, unitOfWork, caller, clock),
      ICommandHandler<UpdateCustomerCommand, CustomerView>
{
    public Task<CustomerView> HandleAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            customer => customer.UpdateProfile(command.DisplayName, command.Email, Caller.ToActor(), Clock.UtcNow),
            cancellationToken);
    }
}

public sealed class AddFavoriteAddressHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : CustomerCommandHandlerBase(customers, unitOfWork, caller, clock),
      ICommandHandler<AddFavoriteAddressCommand, CustomerView>
{
    public Task<CustomerView> HandleAsync(AddFavoriteAddressCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            customer => customer.AddFavoriteAddress(
                command.Label,
                AddressFactory.From(command.Address),
                command.SetAsDefault,
                Clock.UtcNow),
            cancellationToken);
    }
}

public sealed class UpdateFavoriteAddressHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : CustomerCommandHandlerBase(customers, unitOfWork, caller, clock),
      ICommandHandler<UpdateFavoriteAddressCommand, CustomerView>
{
    public Task<CustomerView> HandleAsync(UpdateFavoriteAddressCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            customer => customer.UpdateFavoriteAddress(
                command.AddressId,
                command.Label,
                AddressFactory.From(command.Address),
                command.SetAsDefault),
            cancellationToken);
    }
}

public sealed class RemoveFavoriteAddressHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : CustomerCommandHandlerBase(customers, unitOfWork, caller, clock),
      ICommandHandler<RemoveFavoriteAddressCommand, CustomerView>
{
    public Task<CustomerView> HandleAsync(RemoveFavoriteAddressCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            customer => customer.RemoveFavoriteAddress(command.AddressId),
            cancellationToken);
    }
}

/// <summary>
/// Consommée depuis le topic d'Identity. Idempotente : un événement rejoué ne
/// crée pas un second profil.
/// </summary>
public sealed class CreateCustomerProfileHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ILogger<CreateCustomerProfileHandler> logger) : ICommandHandler<CreateCustomerProfileCommand, Unit>
{
    public async Task<Unit> HandleAsync(CreateCustomerProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await customers.ExistsAsync(command.AccountId, cancellationToken).ConfigureAwait(false))
        {
            return Unit.Value;
        }

        var customer = Customer.CreateFromAccount(
            command.AccountId,
            command.DisplayName,
            command.Phone,
            command.Email,
            // L'auteur est le compte lui-même : c'est son inscription.
            Actor.Customer(command.AccountId.ToString()),
            command.RegisteredAt);

        customers.Add(customer);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Profil client créé pour le compte {AccountId}.", command.AccountId);

        return Unit.Value;
    }
}
