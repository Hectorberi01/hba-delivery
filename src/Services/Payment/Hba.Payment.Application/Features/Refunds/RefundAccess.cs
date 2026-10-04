using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Payment.Application.Features.Refunds;

/// <summary>
/// Qui touche a la file des remboursements, verifie cote service (ADR 0007).
/// </summary>
internal static class RefundAccess
{
    /// <summary>
    /// Trancher un remboursement : admin et finance, personne d'autre.
    /// </summary>
    ///
    /// <remarks>
    /// PAS TOUT LE BACK-OFFICE, ET LA DIFFERENCE EST DE L'ARGENT. Le support repond
    /// aux clients et ops supervise les courses ; decider qu'un client est rembourse
    /// — ou qu'il ne l'est pas — appartient a la finance. C'est la meme frontiere que
    /// pour les grilles tarifaires et les versements aux chauffeurs.
    /// </remarks>
    internal static void EnsureCanResolve(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsInRole(HbaRoles.Admin) && !caller.IsInRole(HbaRoles.Finance))
        {
            throw new ForbiddenException("Seuls admin et finance tranchent un remboursement.");
        }
    }

    /// <summary>
    /// Lire la file : tout le back-office.
    ///
    /// LA LECTURE EST PLUS LARGE QUE LA DECISION, et c'est utile : le support a
    /// besoin de savoir ou en est le remboursement d'un client qui appelle, sans
    /// pouvoir le trancher lui-meme.
    /// </summary>
    internal static void EnsureCanRead(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("La file des remboursements est reservee au back-office.");
        }
    }
}
