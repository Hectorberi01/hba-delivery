using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Directory.Application.Authorization;

/// <summary>
/// Qui a le droit de voir et de modifier quoi. Vérifié dans le service, jamais
/// seulement au BFF.
/// </summary>
public static class DirectoryAccess
{
    /// <summary>
    /// Profil client visé : le sien par défaut, celui d'un autre uniquement
    /// depuis le back-office.
    /// </summary>
    public static Guid ResolveCustomerId(ICallerContext caller, string? requestedId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }

        if (string.IsNullOrWhiteSpace(requestedId))
        {
            return ParseSelf(caller);
        }

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice)
            && !string.Equals(requestedId, caller.SubjectId, StringComparison.Ordinal))
        {
            // NotFound et non Forbidden : rien ne doit confirmer l'existence du
            // profil de quelqu'un d'autre.
            throw new NotFoundException("Client", requestedId);
        }

        return Parse(requestedId);
    }

    /// <summary>Commerçant visé : celui du jeton, sauf pour le back-office.</summary>
    public static Guid ResolveMerchantId(ICallerContext caller, string? requestedId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }

        if (caller.Roles.Overlaps(HbaRoles.BackOffice) && !string.IsNullOrWhiteSpace(requestedId))
        {
            return Parse(requestedId);
        }

        var own = caller.MerchantId
            ?? throw new ForbiddenException("Le jeton ne porte pas de merchant_id.");

        if (!string.IsNullOrWhiteSpace(requestedId)
            && !string.Equals(requestedId, own, StringComparison.Ordinal))
        {
            throw new NotFoundException("Commerçant", requestedId);
        }

        return Parse(own);
    }

    /// <summary>
    /// Modifier un commerçant ou ses points de collecte : le propriétaire ou le
    /// back-office. Un employé prépare et remet, il ne réorganise pas.
    /// </summary>
    public static void EnsureCanManageMerchant(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.Roles.Overlaps(HbaRoles.BackOffice) || caller.IsInRole(HbaRoles.MerchantOwner))
        {
            return;
        }

        throw new ForbiddenException(
            "Seul le propriétaire du commerce, ou le back-office, modifie ces informations.");
    }

    public static void EnsureBackOffice(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            throw new ForbiddenException("Réservé au back-office.");
        }
    }

    private static Guid ParseSelf(ICallerContext caller) => Parse(caller.SubjectId);

    private static Guid Parse(string value)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new DomainException("INVALID_ID", $"Identifiant invalide : {value}.");
}
