namespace Hba.Identity.Domain.Accounts;

/// <summary>
/// États d'un compte. Les valeurs sont persistées : ne jamais les décaler.
/// </summary>
///
/// <remarks>
/// CE COMMENTAIRE DISAIT « UN COMPTE N'EST JAMAIS EFFACÉ », ET CE N'EST PLUS
/// VRAI DEPUIS LE 28 SEPTEMBRE 2026. Ce qu'il visait tient toujours :
/// l'ADMINISTRATION n'efface pas un compte — un livreur suspendu garde son
/// historique de courses, et le back-office doit pouvoir l'instruire. Mais le
/// TITULAIRE, lui, peut demander que le sien disparaisse, et il disparaît
/// vraiment, au terme d'un délai de grâce. La règle exacte est donc : un compte
/// n'est effacé que par celui à qui il appartient.
/// </remarks>
public enum AccountStatus
{
    Active = 1,
    Suspended = 2,

    /// <summary>
    /// Le titulaire a demandé la suppression ; la ligne existe encore, le temps
    /// du délai de grâce.
    /// </summary>
    ///
    /// <remarks>
    /// CE N'EST PAS UNE SUSPENSION, ET LES CONFONDRE CASSERAIT L'ANNULATION. Un
    /// compte suspendu ne reçoit plus de jeton ; celui-ci le doit, puisque se
    /// reconnecter est précisément le geste qui annule la demande. Voir
    /// <see cref="Account.EnsureCanAuthenticate"/>, qui ne bloque que la
    /// suspension.
    /// </remarks>
    PendingDeletion = 3,
}
