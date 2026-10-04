using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Domain.Interfaces;

namespace Hba.Identity.Application.Features.Partners.Queries;

/// <summary>
/// La liste des clients partenaires.
/// </summary>
///
/// <remarks>
/// admin SEULEMENT, COMME LA CREATION ET LA ROTATION. Le reste du back-office
/// n'a pas à savoir quels systèmes tiers appellent l'API, ni avec quelles
/// portées : c'est la carte des accès, et elle se lit comme telle.
///
/// AUCUN SECRET NE SORT D'ICI. La vue n'en porte pas, donc il n'y a rien à
/// filtrer — c'est le seul filtrage qui ne s'oublie jamais.
/// </remarks>
public sealed record ListPartnerClientsQuery(int Limit) : IQuery<IReadOnlyList<PartnerClientSummaryView>>;

public sealed class ListPartnerClientsHandler(
    IPartnerClientRepository partners,
    ICallerContext caller) : IQueryHandler<ListPartnerClientsQuery, IReadOnlyList<PartnerClientSummaryView>>
{
    private const int ParDefaut = 200;
    private const int Plafond = 500;

    public async Task<IReadOnlyList<PartnerClientSummaryView>> HandleAsync(
        ListPartnerClientsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Réservé au rôle admin.");
        }

        var plafond = query.Limit <= 0 ? ParDefaut : Math.Min(query.Limit, Plafond);

        var clients = await partners.ListAsync(plafond, cancellationToken).ConfigureAwait(false);

        return
        [
            .. clients.Select(client => new PartnerClientSummaryView(
                client.Id,
                client.Name,
                client.Source,
                client.ClientId,
                client.WebhookUrl,
                client.Scopes,
                client.RateLimitPerMinute,
                client.Enabled,
                client.CreatedAt,
                client.SecretRotatedAt)),
        ];
    }
}
