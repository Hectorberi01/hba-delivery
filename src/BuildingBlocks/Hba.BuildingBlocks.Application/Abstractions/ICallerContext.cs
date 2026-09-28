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

    /// <summary>Nom affiché, tel que le jeton le porte.</summary>
    ///
    /// <remarks>
    /// CE N'EST PAS UNE AUTORISATION, C'EST UNE IDENTITE. Un service qui doit
    /// ECRIRE le nom ou le téléphone de l'appelant les lit ici, dans le jeton
    /// qu'il a lui-même validé — jamais dans le corps d'une requête, où
    /// l'appelant écrirait ceux d'un autre.
    /// </remarks>
    string? DisplayName { get; }

    /// <summary>Téléphone du compte, tel que le jeton le porte.</summary>
    string? Phone { get; }

    /// <summary>Courriel du compte, absent tant que le client n'en a pas donné.</summary>
    string? Email { get; }

    string? TraceId { get; }

    string? CorrelationId { get; }

    /// <summary>Traduction en acteur de domaine, pour l'audit.</summary>
    Actor ToActor();

    bool IsInRole(string role);
}
