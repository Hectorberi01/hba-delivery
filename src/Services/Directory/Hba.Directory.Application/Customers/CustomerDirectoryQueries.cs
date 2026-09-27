using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Directory.Application.Authorization;
using Hba.Directory.Application.Ports;
using Hba.Directory.Application.Views;

namespace Hba.Directory.Application.Customers;

/// <summary>
/// L'annuaire des clients, pour le back-office.
///
/// CETTE ROUTE FAIT DE LA CONSOLE UN ANNUAIRE DE PERSONNES, et c'est la
/// raison de tout ce qui l'entoure : la recherche porte sur le telephone,
/// donc elle transforme un numero en un nom. Trois roles seulement y ont
/// acces, le numero est masque dans la liste, et la verification est dans le
/// service — pas a la passerelle, qui ne saurait pas quoi masquer.
/// </summary>
public sealed record ListCustomersQuery(string? Query, int PageSize, int Offset)
    : IQuery<CustomerDirectoryPage>;

public sealed class ListCustomersHandler(
    ICustomerRepository customers,
    ICallerContext caller) : IQueryHandler<ListCustomersQuery, CustomerDirectoryPage>
{
    private const int MaxPageSize = 100;

    public async Task<CustomerDirectoryPage> HandleAsync(
        ListCustomersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        DirectoryAccess.EnsureCanReadCustomers(caller);

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);
        var offset = Math.Max(0, query.Offset);

        var page = await customers
            .SearchAsync(query.Query, pageSize, offset, cancellationToken)
            .ConfigureAwait(false);

        var total = await customers.CountAsync(query.Query, cancellationToken).ConfigureAwait(false);

        var lignes = page
            .Select(c => new CustomerDirectoryRowView(
                c.Id,
                c.DisplayName,
                MasqueTelephone.Appliquer(c.Phone),
                c.CreatedAt))
            .ToList();

        return new CustomerDirectoryPage(lignes, total);
    }
}

/// <summary>
/// La fiche d'un client, mise en forme selon le role de l'appelant.
///
/// LA MISE EN FORME EST ICI, PAS DANS LA PASSERELLE. C'est la regle du depot —
/// toute autorisation se verifie cote service — et sa raison pratique : une
/// passerelle qui filtrerait les champs aurait deja recu la reponse complete,
/// et un appel gRPC direct au service contournerait tout.
/// </summary>
public sealed record GetCustomerFileQuery(string CustomerId) : IQuery<CustomerFileView>;

public sealed class GetCustomerFileHandler(
    ICustomerRepository customers,
    IPersonalDataReadLog lectures,
    ICallerContext caller) : IQueryHandler<GetCustomerFileQuery, CustomerFileView>
{
    public async Task<CustomerFileView> HandleAsync(
        GetCustomerFileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var visibilite = DirectoryAccess.EnsureCanReadCustomers(caller);

        if (!Guid.TryParse(query.CustomerId, out var id))
        {
            throw new DomainException("INVALID_ID", $"Identifiant invalide : {query.CustomerId}.");
        }

        var client = await customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Client", query.CustomerId);

        // UNE VARIABLE PLUTOT QU'UN TERNAIRE DE DEUX EXPRESSIONS DE
        // COLLECTION : « cond ? [..x] : [] » depend du typage par cible dans
        // un conditionnel, qui se resout mal selon le contexte. Une ligne de
        // plus, et plus de doute.
        IReadOnlyList<FavoriteAddressView> adresses = [];

        if (visibilite.Adresses)
        {
            adresses = [.. client.FavoriteAddresses.Select(DirectoryViewMapper.ToView)];
        }

        // APRES LA LECTURE, PAS AVANT. Un refus — role insuffisant, client
        // inexistant — ne laisse aucune ligne : il n'y a rien eu a voir, et
        // une ligne dirait le contraire a qui relira le journal.
        //
        // LA RECHERCHE N'EST PAS CONSIGNEE, decide le 27 septembre 2026 :
        // ouvrir une fiche designe quelqu'un, taper trois lettres non.
        await lectures
            .RecordAsync(PersonalDataReadKind.CustomerFile, client.Id.ToString(), cancellationToken)
            .ConfigureAwait(false);

        // LE TELEPHONE EST COMPLET ICI, MASQUE DANS LA LISTE. Ouvrir une
        // fiche est un geste par personne ; parcourir la liste ne l'est pas.
        return new CustomerFileView(
            client.Id,
            client.DisplayName,
            client.Phone,
            visibilite.Email ? client.Email : null,
            EmailMasque: !visibilite.Email,
            adresses,
            AdressesMasquees: !visibilite.Adresses,
            client.CreatedAt);
    }
}

/// <summary>
/// Qui a ouvert la fiche de ce client, et quand.
///
/// ADMIN SEULEMENT. Ops et support sont dans ce journal : leur donner la
/// main dessus reviendrait a les laisser verifier ce qu'on sait d'eux, ce qui
/// n'est pas le role d'une trace d'acces.
///
/// CE JOURNAL N'EST PAS COMPLET, ET L'ECRAN DOIT LE DIRE. Il ne porte que
/// les ouvertures de fiche, parce qu'il vit dans la base de Directory. La
/// lecture du cumul facture est tracee dans Delivery, celle d'un dossier KYC
/// dans Driver : chaque service garde son journal, comme il garde ses
/// donnees. Les reunir demandera une route par service et une fusion a la
/// passerelle — ce n'est pas fait.
/// </summary>
public sealed record GetCustomerAccessLogQuery(string CustomerId, int Limit)
    : IQuery<IReadOnlyList<PersonalDataReadEntry>>;

public sealed class GetCustomerAccessLogHandler(
    IPersonalDataReadReader journal,
    ICallerContext caller) : IQueryHandler<GetCustomerAccessLogQuery, IReadOnlyList<PersonalDataReadEntry>>
{
    public async Task<IReadOnlyList<PersonalDataReadEntry>> HandleAsync(
        GetCustomerAccessLogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Le journal des acces est reserve a l'administration.");
        }

        if (!Guid.TryParse(query.CustomerId, out var id))
        {
            throw new DomainException("INVALID_ID", $"Identifiant invalide : {query.CustomerId}.");
        }

        // CONSULTER LE JOURNAL N'EST PAS CONSIGNE, et c'est un choix a
        // assumer : une trace de la trace ouvrirait une recursion sans fin.
        // Le garde-fou est ailleurs — seul admin y accede.
        return await journal
            .ListForSubjectAsync(id.ToString(), query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }
}
