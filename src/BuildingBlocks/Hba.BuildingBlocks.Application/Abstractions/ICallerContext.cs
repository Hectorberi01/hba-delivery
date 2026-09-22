using Hba.BuildingBlocks.Domain;

namespace Hba.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Identité de l'appelant telle que le SERVICE l'a établie lui-même, à partir du
/// JWT propagé. Ce n'est jamais le BFF qui l'affirme : l'autorisation se vérifie
/// côté service.
/// </summary>
public interface ICallerContext
{
    bool IsAuthenticated { get; }

    /// <summary>Sujet du JWT.</summary>
    string SubjectId { get; }

    /// <summary>Rôles du référentiel acteurs : customer, driver, merchant_owner, …</summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>Renseigné pour merchant_owner / merchant_staff.</summary>
    string? MerchantId { get; }

    /// <summary>Renseigné pour partner. Sert au cloisonnement multi-partenaires.</summary>
    string? PartnerId { get; }

    /// <summary>Renseigné pour driver.</summary>
    string? DriverId { get; }

    string? TraceId { get; }

    string? CorrelationId { get; }

    /// <summary>Traduction en acteur de domaine, pour l'audit.</summary>
    Actor ToActor();

    bool IsInRole(string role);
}
