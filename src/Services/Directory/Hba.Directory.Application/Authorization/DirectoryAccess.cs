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

    /// <summary>
    /// Qui peut lire l'annuaire des clients, et ce qu'il y voit.
    ///
    /// PAS « LE BACK-OFFICE » : finance en est exclu, et ce n'est pas un
    /// oubli. Rattacher un paiement a un client se fait par identifiant ;
    /// parcourir l'annuaire et lire des adresses de domicile n'entre pas dans
    /// ce metier. HbaRoles.BackOffice inclut finance — s'en servir ici aurait
    /// ouvert la porte sans que personne ne l'ait decide.
    ///
    /// LE REFERENTIEL NE TRANCHAIT PAS. Sa matrice de visibilite n'a qu'une
    /// colonne « Admin » et aucune ligne pour les donnees du client. Ce qui
    /// suit est la decision prise le 27 septembre 2026, a reporter dans le
    /// referentiel — ce fichier n'en est que l'application.
    /// </summary>
    public static readonly IReadOnlySet<string> LecteursDeClients =
        new HashSet<string>(StringComparer.Ordinal) { HbaRoles.Admin, HbaRoles.Ops, HbaRoles.Support };

    /// <summary>
    /// Ce que l'appelant a le droit de voir d'un client.
    ///
    /// LES CHAMPS CACHES SONT ANNONCES, PAS OMIS. Un ecran qui n'affiche rien
    /// la ou il n'a pas le droit fait lire « ce client n'a pas d'adresse
    /// enregistree » — une information fausse, sur laquelle un operateur
    /// agira. On dit donc « masque », jamais « vide ».
    /// </summary>
    public sealed record VisibiliteClient(bool Email, bool Adresses);

    /// <summary>
    /// Verifie le droit de lire l'annuaire et rend ce que l'appelant y voit.
    /// </summary>
    public static VisibiliteClient EnsureCanReadCustomers(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.Roles.Overlaps(LecteursDeClients))
        {
            throw new ForbiddenException(
                "L'annuaire des clients est reserve a l'administration, aux operations et au support.");
        }

        // OPS SUPERVISE DES COURSES, IL NE CONSULTE PAS DES DOSSIERS. Il
        // obtient le telephone — rappeler un client dont la livraison se
        // passe mal fait partie du travail — mais ni l'adresse e-mail ni les
        // adresses enregistrees, qui ne servent a aucune supervision.
        var complet = caller.IsInRole(HbaRoles.Admin) || caller.IsInRole(HbaRoles.Support);

        return new VisibiliteClient(Email: complet, Adresses: complet);
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
