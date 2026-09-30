using Hba.Billing.Domain.Accounts;

namespace Hba.Billing.Application.Common.Interfaces;

public interface IBillingAccountRepository
{
    Task<BillingAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<BillingAccount?> FindByOwnerAsync(
        string ownerType,
        string ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Charge le compte EN LE VERROUILLANT, pour la durée de la transaction.
    /// </summary>
    ///
    /// <remarks>
    /// C'EST LE POINT OÙ CE GENRE DE MODULE SE CASSE, et il n'y en a pas
    /// d'autre. Deux courses créées à la même seconde sur le même compte
    /// liraient toutes deux le solde avant que l'autre ne l'écrive : les deux
    /// passeraient, et le plafond serait dépassé sans qu'aucune erreur ne soit
    /// levée nulle part.
    ///
    /// UN VERROU PESSIMISTE, ET NON LE JETON OPTIMISTE EMPLOYÉ AILLEURS DANS CE
    /// DÉPÔT. Sur le compte d'un donneur d'ordre chargé, les tentatives
    /// optimistes échoueraient et se répéteraient en cascade ; un verrou de
    /// ligne tenu quelques millisecondes coûte moins cher. La sérialisation est
    /// PAR COMPTE : deux donneurs d'ordre différents ne se bloquent jamais.
    ///
    /// APPELER CETTE MÉTHODE HORS D'UNE TRANSACTION NE VERROUILLE RIEN. Le
    /// verrou est tenu jusqu'au commit ; sans transaction ouverte, il est
    /// relâché aussitôt et la garantie disparaît en silence.
    /// </remarks>
    Task<BillingAccount?> GetForUpdateAsync(
        string ownerType,
        string ownerId,
        CancellationToken cancellationToken);

    void Add(BillingAccount account);

    Task<AccountMovement?> GetMovementAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Retrouve un mouvement par sa clé d'idempotence, ou null.
    /// </summary>
    ///
    /// <remarks>
    /// LA CLÉ EST UNIQUE DANS TOUTE LA TABLE, pas seulement par compte : la
    /// contrainte d'unicité de <c>account_movements</c> est globale. Un
    /// identifiant de compte en paramètre laisserait croire le contraire.
    ///
    /// SERT À L'ANNULATION D'UN DÉBIT : Delivery ne connaît que la clé qu'il a
    /// donnée — l'identifiant de la course — et non l'identifiant du mouvement.
    /// </remarks>
    Task<AccountMovement?> FindMovementByKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    void AddMovement(AccountMovement movement);
}
