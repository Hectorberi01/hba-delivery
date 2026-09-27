using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Application.Common.Interfaces;
using Hba.Identity.Application.Common.Views;
using Hba.Identity.Domain.Interfaces;

namespace Hba.Identity.Application.Features.Partners.Queries;

/// <summary>
/// Configuration webhook d'un partenaire, back-office uniquement.
///
/// ELLE REND LE SECRET EN CLAIR, ET C'EST LE SEUL ENDROIT QUI LE FAIT : le
/// partenaire doit pouvoir le recopier pour vérifier nos signatures. D'où le
/// contrôle de rôle avant toute lecture.
/// </summary>
public sealed record GetPartnerWebhookConfigQuery(Guid PartnerId) : IQuery<PartnerWebhookConfigView>;

public sealed class GetPartnerWebhookConfigHandler(
    IPartnerClientRepository partners,
    ISecretProtector protector,
    ICallerContext caller) : IQueryHandler<GetPartnerWebhookConfigQuery, PartnerWebhookConfigView>
{
    public async Task<PartnerWebhookConfigView> HandleAsync(
        GetPartnerWebhookConfigQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Réservé au back-office.");
        }

        var partner = await partners.GetByIdAsync(query.PartnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Partenaire", query.PartnerId.ToString());

        return new PartnerWebhookConfigView(
            partner.Id.ToString(),
            partner.WebhookUrl,
            protector.Unprotect(partner.ProtectedWebhookSecret),
            partner.RateLimitPerMinute,
            partner.Enabled);
    }
}
