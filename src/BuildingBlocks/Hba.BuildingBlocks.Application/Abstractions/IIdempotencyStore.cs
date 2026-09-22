namespace Hba.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Rejeu des requêtes portant une Idempotency-Key. Indispensable pour l'app
/// livreur, qui met ses actions en file quand la connexion est instable, et pour
/// les partenaires B2B, chez qui la clé est obligatoire.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Renvoie l'identifiant de ressource déjà produit pour cette clé, ou null.
    /// </summary>
    Task<string?> TryGetResultAsync(string scope, string idempotencyKey, CancellationToken cancellationToken);

    Task RememberAsync(string scope, string idempotencyKey, string resourceId, CancellationToken cancellationToken);
}
