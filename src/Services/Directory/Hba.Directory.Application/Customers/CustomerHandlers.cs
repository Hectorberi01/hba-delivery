using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
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
/// Le téléphone est-il déjà porté par une AUTRE fiche ?
/// </summary>
///
/// <remarks>
/// LE CONSTAT DU 28 SEPTEMBRE 2026, ET IL VALAIT PLUS QUE LA PANNE QU'IL A
/// EXPLIQUEE. « IX_customers_phone » est unique depuis la première migration.
/// Aucune ligne de code ne le savait : les deux chemins de création
/// inséraient, Postgres refusait avec un 23505, et le résultat dépendait de
/// l'appelant — « Erreur interne du service » pour la route de rattrapage,
/// RIEN DU TOUT pour le consommateur Kafka, dont l'échec ne se voyait que dans
/// une colonne d'inbox que personne ne lisait.
///
/// C'est ce qui a fait croire pendant deux jours à un événement perdu. Il ne
/// l'était pas : il est arrivé, et il a échoué.
///
/// UNE CONTRAINTE DE BASE N'EST PAS UNE REGLE DE DOMAINE tant que le domaine
/// ne la connaît pas. Elle protège les données, elle n'explique rien.
/// </remarks>
internal static class TelephoneUnique
{
    public static async Task VerifierAsync(
        ICustomerRepository customers,
        Guid compte,
        string telephone,
        CancellationToken cancellationToken)
    {
        var porteur = await customers
            .FindIdByPhoneAsync(telephone, cancellationToken)
            .ConfigureAwait(false);

        if (porteur is null || porteur == compte)
        {
            return;
        }

        // L'IDENTIFIANT DE L'AUTRE FICHE EST DANS LE MESSAGE, ET C'EST ASSUME.
        // Ce n'est pas une donnée personnelle — c'est un UUID — et sans lui,
        // celui qui lit l'erreur doit fouiller la base pour savoir de quelle
        // fiche on parle. Le nom et l'adresse de l'autre client, eux, n'y
        // figurent pas.
        throw new DomainException(
            "PHONE_ALREADY_PROFILED",
            $"Une autre fiche porte déjà ce téléphone : {porteur}. "
            + "Un numéro ne peut identifier qu'un seul profil.");
    }
}

/// <summary>
/// Rattrapage explicite, demandé par le client lui-même. Voir le commentaire
/// de EnsureCustomerProfileCommand : cette voie existe parce que l'événement
/// d'inscription peut se perdre, et elle ne lit QUE le jeton.
/// </summary>
public sealed class EnsureCustomerProfileHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<EnsureCustomerProfileHandler> logger)
    : ICommandHandler<EnsureCustomerProfileCommand, CustomerView>
{
    public async Task<CustomerView> HandleAsync(
        EnsureCustomerProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.IsInRole(HbaRoles.Customer))
        {
            // UN LIVREUR OU UN COMMERCANT NE SE FABRIQUE PAS UNE FICHE CLIENT
            // ICI. Qu'un livreur puisse aussi commander reste une question
            // ouverte ; tant qu'elle n'est pas tranchee, cette route ne la
            // tranche pas a sa place.
            throw new ForbiddenException("Seul un client crée sa propre fiche.");
        }

        // MEME RESOLUTION QUE PARTOUT AILLEURS : l'identifiant de la fiche est
        // le sujet du jeton. Passer null interdit de viser quelqu'un d'autre.
        var id = DirectoryAccess.ResolveCustomerId(caller, null);

        var existant = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existant is not null)
        {
            return DirectoryViewMapper.ToView(existant);
        }

        // LE TELEPHONE VIENT DU JETON, ET SANS LUI ON N'INVENTE RIEN. Une fiche
        // sans telephone joignable ne sert a personne, et le domaine la
        // refuserait de toute facon : mieux vaut un refus qui nomme la cause.
        var telephone = caller.Phone;

        if (string.IsNullOrWhiteSpace(telephone))
        {
            throw new DomainException(
                "MISSING_PHONE_CLAIM",
                "Le jeton ne porte pas de téléphone : reconnectez-vous pour en obtenir un à jour.");
        }

        // AVANT D'INSERER, PAS APRES. Laisser Postgres trancher rendrait un
        // 23505 nu, que l'intercepteur traduit en « Erreur interne du
        // service » : vrai, inutile, et impossible a corriger pour qui le lit.
        await TelephoneUnique
            .VerifierAsync(customers, id, PhoneNumber.Normalize(telephone), cancellationToken)
            .ConfigureAwait(false);

        var customer = Customer.CreateFromAccount(
            id,
            caller.DisplayName ?? string.Empty,
            telephone,
            caller.Email,
            Actor.Customer(id.ToString()),
            clock.UtcNow);

        customers.Add(customer);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // UN AVERTISSEMENT, PAS UNE INFORMATION, ET C'EST VOULU. Chaque ligne
        // de ce genre est un profil que l'evenement AccountRegistered aurait du
        // creer et n'a pas cree : si elles se multiplient, c'est la chaine
        // Identity -> Kafka -> Directory qu'il faut reparer, pas ce rattrapage
        // qu'il faut elargir.
        logger.LogWarning(
            "Profil client {AccountId} cree par RATTRAPAGE : l'evenement d'inscription ne l'avait pas cree.",
            id);

        return DirectoryViewMapper.ToView(customer);
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

        // LE MEME CONTROLE ICI, ET C'EST LE PLUS IMPORTANT DES DEUX. Le
        // rattrapage a quelqu'un devant lui pour lire l'erreur ; ce
        // consommateur-ci n'a personne. Sans ce controle, la collision
        // remontait en DbUpdateException, l'inbox notait « echec » et le
        // profil n'existait jamais — sans qu'aucun ecran ne dise pourquoi.
        await TelephoneUnique
            .VerifierAsync(
                customers,
                command.AccountId,
                PhoneNumber.Normalize(command.Phone),
                cancellationToken)
            .ConfigureAwait(false);

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
