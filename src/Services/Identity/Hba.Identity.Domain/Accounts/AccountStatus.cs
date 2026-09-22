namespace Hba.Identity.Domain.Accounts;

/// <summary>
/// États d'un compte. Les valeurs sont persistées : ne jamais les décaler.
/// Un compte n'est jamais effacé — un livreur suspendu garde son historique de
/// courses, et le back-office doit pouvoir l'instruire.
/// </summary>
public enum AccountStatus
{
    Active = 1,
    Suspended = 2,
}
