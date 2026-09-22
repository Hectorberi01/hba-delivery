using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;

namespace Hba.Delivery.Application.Authorization;

/// <summary>
/// Application de la matrice de visibilité, côté service. Le BFF peut filtrer ce
/// qu'il veut : c'est ici que la décision fait foi.
/// </summary>
public static class DeliveryAccess
{
    /// <summary>
    /// Périmètre de lecture imposé à l'appelant. Un client ne voit que ses
    /// livraisons, un commerçant que celles de ses points de collecte, un
    /// partenaire que les siennes, un livreur que celles qui lui sont affectées.
    /// Le back-office voit tout.
    /// </summary>
    public static DeliveryQueryFilter ScopeFor(ICallerContext caller, DeliveryQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(filter);

        if (!caller.IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }

        if (caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            return filter;
        }

        if (caller.IsInRole(HbaRoles.Customer))
        {
            return filter with { CustomerId = caller.SubjectId, MerchantId = null, PartnerId = null, DriverId = null };
        }

        if (caller.IsInRole(HbaRoles.MerchantOwner) || caller.IsInRole(HbaRoles.MerchantStaff))
        {
            return filter with { MerchantId = Require(caller.MerchantId, "merchant_id"), CustomerId = null, PartnerId = null, DriverId = null };
        }

        if (caller.IsInRole(HbaRoles.Partner))
        {
            return filter with { PartnerId = Require(caller.PartnerId, "partner_id"), CustomerId = null, MerchantId = null, DriverId = null };
        }

        if (caller.IsInRole(HbaRoles.Driver))
        {
            return filter with { DriverId = Require(caller.DriverId, "driver_id"), CustomerId = null, MerchantId = null, PartnerId = null };
        }

        throw new ForbiddenException("Aucun rôle connu ne donne accès aux livraisons.");
    }

    /// <summary>
    /// Droit de consulter une livraison précise. Échoue en NotFound plutôt qu'en
    /// Forbidden hors périmètre : rien ne doit révéler l'existence d'une
    /// livraison appartenant à quelqu'un d'autre.
    /// </summary>
    public static void EnsureCanRead(DeliveryAggregate delivery, ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(caller);

        if (IsWithinScope(delivery, caller))
        {
            return;
        }

        throw new NotFoundException("Livraison", delivery.Id.ToString());
    }

    /// <summary>Droit d'agir : mêmes périmètres, mais merchant_staff est limité.</summary>
    public static void EnsureCanCancel(DeliveryAggregate delivery, ICallerContext caller)
    {
        EnsureCanRead(delivery, caller);

        if (caller.IsInRole(HbaRoles.MerchantStaff) && !caller.IsInRole(HbaRoles.MerchantOwner))
        {
            throw new ForbiddenException("Un employé de commerçant ne peut pas annuler une livraison.");
        }

        if (caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Un livreur n'annule pas une livraison ; il déclare un incident.");
        }
    }

    public static bool IsWithinScope(DeliveryAggregate delivery, ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsAuthenticated)
        {
            return false;
        }

        if (caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            return true;
        }

        if (caller.IsInRole(HbaRoles.Customer))
        {
            return delivery.CustomerId == caller.SubjectId;
        }

        if (caller.IsInRole(HbaRoles.MerchantOwner) || caller.IsInRole(HbaRoles.MerchantStaff))
        {
            return delivery.MerchantId is not null && delivery.MerchantId == caller.MerchantId;
        }

        if (caller.IsInRole(HbaRoles.Partner))
        {
            return delivery.PartnerId == caller.PartnerId;
        }

        if (caller.IsInRole(HbaRoles.Driver))
        {
            return delivery.Driver is not null && delivery.Driver.DriverId == caller.DriverId;
        }

        return false;
    }

    private static string Require(string? value, string claim)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ForbiddenException($"Le jeton ne porte pas le claim {claim}.")
            : value;
}
