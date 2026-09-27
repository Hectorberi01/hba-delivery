namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>
/// Les services internes qui ont le droit de demander un jeton.
///
/// PAS UNE TABLE, ET C'EST VOULU. Un partenaire B2B est une entreprise : il
/// naît, change de nom, se fait couper. Un service est un morceau du
/// déploiement, au même titre que la chaîne de connexion à sa base : ses
/// identifiants viennent de la configuration de l'hôte, et changer la liste
/// veut dire déployer, pas écrire en base.
/// </summary>
public interface IServiceClientRegistry
{
    /// <summary>Vrai si ce couple identifiant/secret est celui d'un service déclaré.</summary>
    bool Verify(string? clientId, string? clientSecret);
}
