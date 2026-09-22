using System.Security.Claims;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Microsoft.AspNetCore.Http;

namespace Hba.BuildingBlocks.Security;

/// <summary>
/// Contexte d'appel reconstruit à partir du JWT validé par CE service. Jamais à
/// partir d'un en-tête posé par un BFF.
/// </summary>
internal sealed class CallerContext(IHttpContextAccessor accessor) : ICallerContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string SubjectId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User?.FindFirst("sub")?.Value
                               ?? string.Empty;

    public IReadOnlySet<string> Roles =>
        User?.FindAll(HbaClaims.Roles).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    public string? MerchantId => User?.FindFirst(HbaClaims.MerchantId)?.Value;

    public string? PartnerId => User?.FindFirst(HbaClaims.PartnerId)?.Value;

    public string? DriverId => User?.FindFirst(HbaClaims.DriverId)?.Value;

    public string? TraceId => accessor.HttpContext?.TraceIdentifier;

    public string? CorrelationId => accessor.HttpContext?.Request.Headers["hba-correlation-id"].FirstOrDefault();

    public bool IsInRole(string role) => Roles.Contains(role);

    public Actor ToActor()
    {
        if (!IsAuthenticated)
        {
            throw new ForbiddenException("Appel non authentifié.");
        }

        if (IsInRole(HbaRoles.Driver))
        {
            return Actor.Driver(DriverId ?? SubjectId);
        }

        if (IsInRole(HbaRoles.MerchantOwner) || IsInRole(HbaRoles.MerchantStaff))
        {
            return Actor.Merchant(MerchantId ?? SubjectId);
        }

        if (IsInRole(HbaRoles.Partner))
        {
            return Actor.Partner(PartnerId ?? SubjectId);
        }

        if (Roles.Overlaps(HbaRoles.BackOffice))
        {
            return Actor.Admin(SubjectId);
        }

        if (IsInRole(HbaRoles.Customer))
        {
            return Actor.Customer(SubjectId);
        }

        throw new ForbiddenException("Aucun rôle connu dans le jeton.");
    }
}
