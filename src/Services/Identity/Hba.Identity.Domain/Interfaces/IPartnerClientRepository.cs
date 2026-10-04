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

    /// <summary>
    /// Tous les clients partenaires, triés par nom.
    /// </summary>
    ///
    /// <remarks>
    /// LES DESACTIVES EN FONT PARTIE. Un partenaire coupé existe toujours, et
    /// c'est justement de lui qu'on vient vérifier l'état.
    ///
    /// LE PLAFOND EST DANS LA SIGNATURE, comme partout ailleurs ici : une
    /// méthode sans limite finit toujours par être appelée sur une table qui a
    /// grossi.
    /// </remarks>
    Task<IReadOnlyList<PartnerClient>> ListAsync(int limit, CancellationToken cancellationToken);

    void Add(PartnerClient client);
}
