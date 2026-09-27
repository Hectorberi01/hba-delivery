using Hba.Identity.Domain.Partners;

namespace Hba.Identity.Domain.Interfaces;

/// <summary>
/// Accès aux clients partenaires (systèmes tiers appelant l'API HBA en OAuth2).
///
/// PARTENAIRE N'EST PAS COMMERCANT : un partenaire est un système, un commerçant
/// est une entreprise. Ce dépôt ne connaît que le premier.
/// </summary>
public interface IPartnerClientRepository
{
    Task<PartnerClient?> GetByIdAsync(Guid partnerId, CancellationToken cancellationToken);

    Task<PartnerClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken);

    void Add(PartnerClient client);
}
