using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Security;
using Hba.Ride.Domain.Exceptions;
using Hba.Ride.Domain.Results;
using Hba.Ride.Domain.Rides;

namespace Hba.Ride.Application.Authorization;

/// <summary>
/// Qui a le droit d'agir sur une course, vérifié côté service (ADR 0007).
/// </summary>
///
/// <remarks>
/// LE REFUS EST UN <c>Result</c>, PAS UNE EXCEPTION, puisqu'on est dans Ride. Il
/// porte le code <c>FORBIDDEN</c> — le même que <c>ForbiddenException</c> ailleurs
/// — et la frontière le traduira en 403 comme partout. Le dehors ne voit pas la
/// différence.
///
/// L'IDENTIFIANT DU CHAUFFEUR NE VIENT JAMAIS DU CORPS DE LA REQUÊTE. Il est lu
/// dans le jeton que ce service a lui-même validé : sans cela, n'importe quel
/// chauffeur pourrait démarrer la course d'un autre en changeant un champ.
/// </remarks>
public static class RideAccess
{
    public const string Forbidden = "FORBIDDEN";

    /// <summary>Le client propriétaire de la course, et personne d'autre.</summary>
    public static Result? RefusSiPasLeClient(ICallerContext caller, RideAggregate ride)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(ride);

        if (EstLeClient(caller, ride) || EstBackOffice(caller))
        {
            return null;
        }

        return Result.Refus(Forbidden, "Cette course n'est pas la vôtre.");
    }

    /// <summary>
    /// Le chauffeur affecté, et personne d'autre.
    /// </summary>
    ///
    /// <remarks>
    /// LE BACK-OFFICE EST ADMIS, ET CE N'EST PAS UNE FACILITÉ : un chauffeur dont
    /// le téléphone est tombé en panne laisse une course bloquée, et quelqu'un
    /// doit pouvoir la terminer. Le passager, lui, n'est PAS admis à déclarer
    /// l'arrivée du chauffeur — il déclarerait ce qu'il souhaite.
    /// </remarks>
    public static Result? RefusSiPasLeChauffeur(ICallerContext caller, RideAggregate ride)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(ride);

        if (EstBackOffice(caller))
        {
            return null;
        }

        if (caller.IsInRole(HbaRoles.Driver)
            && !string.IsNullOrWhiteSpace(caller.DriverId)
            && string.Equals(caller.DriverId, ride.DriverId, StringComparison.Ordinal))
        {
            return null;
        }

        return Result.Refus(Forbidden, "Cette course n'est pas la vôtre.");
    }

    /// <summary>
    /// Qui peut annuler : le client ou le chauffeur affecté.
    /// </summary>
    ///
    /// <remarks>
    /// LES DEUX PEUVENT, ET LA SUITE N'EST PAS LA MÊME. Un client qui annule et
    /// un chauffeur qui renonce ne produisent pas les mêmes conséquences —
    /// remboursement, pénalité, réouverture de la recherche. L'agrégat ne
    /// distingue pas encore les deux : il enregistre l'acteur dans l'événement,
    /// ce qui laisse la distinction possible sans la préjuger.
    /// </remarks>
    public static Result? RefusSiNePeutAnnuler(ICallerContext caller, RideAggregate ride)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(ride);

        if (EstLeClient(caller, ride) || EstBackOffice(caller))
        {
            return null;
        }

        return RefusSiPasLeChauffeur(caller, ride);
    }

    /// <summary>
    /// Qui peut LIRE une course : le client, le chauffeur affecté, le back-office.
    /// </summary>
    ///
    /// <remarks>
    /// LIRE N'EST PAS AGIR, ET LA LISTE N'EST DONC PAS LA MÊME. Le chauffeur
    /// affecté doit voir la course qu'il exécute — adresse, destination, état —
    /// alors qu'il n'a rien à y annuler au nom du passager. Réutiliser
    /// <see cref="RefusSiPasLeClient"/> pour la lecture lui cacherait sa propre
    /// mission ; réutiliser <see cref="RefusSiNePeutAnnuler"/> ouvrirait la
    /// lecture à la logique de l'annulation, qui pourra changer sans rapport.
    /// </remarks>
    public static Result? RefusSiNePeutLire(ICallerContext caller, RideAggregate ride)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(ride);

        if (EstLeClient(caller, ride) || EstBackOffice(caller))
        {
            return null;
        }

        return RefusSiPasLeChauffeur(caller, ride);
    }

    private static bool EstLeClient(ICallerContext caller, RideAggregate ride)
        => caller.IsInRole(HbaRoles.Customer)
           && string.Equals(caller.SubjectId, ride.CustomerId, StringComparison.Ordinal);

    private static bool EstBackOffice(ICallerContext caller)
        => caller.IsInRole(HbaRoles.Admin)
           || caller.IsInRole(HbaRoles.Ops)
           || caller.IsInRole(HbaRoles.Support);
}
