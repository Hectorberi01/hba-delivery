using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Application.Ports;
using Hba.Identity.Application.Views;

namespace Hba.Identity.Application.Queries;

/// <summary>
/// Identifiant vide = l'appelant lui-même. Renseigné, c'est une consultation
/// du back-office.
/// </summary>
public sealed record GetPrincipalQuery(string? SubjectId) : IQuery<PrincipalView>;

public sealed class GetPrincipalHandler(
    IAccountRepository accounts,
    ICallerContext caller) : IQueryHandler<GetPrincipalQuery, PrincipalView>
{
    public async Task<PrincipalView> HandleAsync(GetPrincipalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var target = string.IsNullOrWhiteSpace(query.SubjectId) ? caller.SubjectId : query.SubjectId;

        if (!string.Equals(target, caller.SubjectId, StringComparison.Ordinal)
            && !caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Vous ne pouvez consulter que votre propre compte.");
        }

        if (!Guid.TryParse(target, out var id))
        {
            throw new NotFoundException("Compte", target ?? string.Empty);
        }

        var account = await accounts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Compte", target);

        return AccountViewMapper.ToPrincipal(account);
    }
}
