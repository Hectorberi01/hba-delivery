using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;

namespace Hba.Delivery.Application.Queries;

/// <summary>
/// Ce qui a ete facture a un client.
///
/// ADMIN SEULEMENT, et ce n'est pas la meme regle que pour la liste de ses
/// courses. Ops et support ouvrent la fiche d'un client et voient ses courses
/// une par une — un litige se traite comme ca. Le CUMUL, lui, est un chiffre
/// d'argent : il ne change rien a la resolution d'un incident et dit sur le
/// client quelque chose qu'aucun des deux metiers n'a a connaitre. Decide le
/// 27 septembre 2026, avec la matrice du referentiel.
///
/// FACTURE, PAS ENCAISSE. Voir IDeliveryRepository.SumBilledForCustomerAsync :
/// ce total ignore impayes et remboursements, qui vivent dans Payment et s'y
/// agregent par payeur, pas par client. L'etiquette a l'ecran doit dire
/// « facture ».
/// </summary>
public sealed record GetCustomerBillingQuery(string CustomerId) : IQuery<CustomerBillingView>;

public sealed record CustomerBillingView(
    string CustomerId,
    int DeliveredCount,
    long BilledTotalXof);

public sealed class GetCustomerBillingHandler(
    IDeliveryRepository repository,
    IPersonalDataReadLog lectures,
    ICallerContext caller) : IQueryHandler<GetCustomerBillingQuery, CustomerBillingView>
{
    public async Task<CustomerBillingView> HandleAsync(
        GetCustomerBillingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Le cumul facture d'un client est reserve a l'administration.");
        }

        if (string.IsNullOrWhiteSpace(query.CustomerId))
        {
            throw new DomainException("MISSING_CUSTOMER_ID", "Le client doit etre nomme.");
        }

        var client = query.CustomerId.Trim();

        var (nombre, total) = await repository
            .SumBilledForCustomerAsync(client, cancellationToken)
            .ConfigureAwait(false);

        // CONSIGNE DANS LA BASE DE DELIVERY, pas dans celle de Directory. Le
        // journal a la meme forme partout, mais chaque service ecrit chez
        // lui : la regle « une base par service » ne souffre pas d'exception
        // pour un journal d'audit.
        await lectures
            .RecordAsync(PersonalDataReadKind.CustomerBilling, client, cancellationToken)
            .ConfigureAwait(false);

        return new CustomerBillingView(client, nombre, total);
    }
}
