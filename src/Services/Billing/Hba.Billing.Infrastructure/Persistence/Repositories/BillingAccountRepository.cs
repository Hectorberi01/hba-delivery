using Hba.Billing.Application.Common.Interfaces;
using Hba.Billing.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Hba.Billing.Infrastructure.Persistence.Repositories;

internal sealed class BillingAccountRepository(BillingDbContext context) : IBillingAccountRepository
{
    public Task<BillingAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.BillingAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<BillingAccount?> FindByOwnerAsync(
        string ownerType,
        string ownerId,
        CancellationToken cancellationToken)
        => context.BillingAccounts
            .FirstOrDefaultAsync(a => a.OwnerType == ownerType && a.OwnerId == ownerId, cancellationToken);

    /// <summary>
    /// Charge le compte en posant un verrou de ligne, tenu jusqu'au commit.
    /// </summary>
    ///
    /// <remarks>
    /// « FOR UPDATE » ET NON « FOR UPDATE SKIP LOCKED », contrairement a
    /// l'Outbox : ici on veut ATTENDRE son tour, pas passer au suivant. Deux
    /// courses du meme commercant doivent se suivre ; sauter la seconde
    /// reviendrait a la refuser sans raison.
    ///
    /// DU SQL BRUT, PARCE QU'EF CORE NE SAIT PAS POSER CE VERROU. Il n'a aucune
    /// notion de verrouillage pessimiste : ni « FromSql » ni une option de
    /// requete ne l'expriment. La requete reste suivie par le ChangeTracker —
    /// c'est le meme type d'entite, lu par le meme contexte — donc la
    /// modification qui suit est enregistree normalement.
    ///
    /// LE NOM DES COLONNES EST ECRIT ICI, ET C'EST LA FRAGILITE DE CE CHOIX.
    /// Renommer « owner_type » dans la configuration sans toucher a cette
    /// ligne casserait le verrou a l'execution, pas a la compilation. Les deux
    /// vivent dans le meme dossier pour que la relecture les rapproche.
    ///
    /// SANS TRANSACTION OUVERTE, CE VERROU NE VAUT RIEN : PostgreSQL le relache
    /// des la fin de l'instruction. L'appelant DOIT etre dans une transaction —
    /// c'est le cas des gestionnaires de commande, qui n'appellent
    /// SaveChangesAsync qu'a la fin.
    /// </remarks>
    public Task<BillingAccount?> GetForUpdateAsync(
        string ownerType,
        string ownerId,
        CancellationToken cancellationToken)
    {
        // ON REFUSE DE FAIRE SEMBLANT DE VERROUILLER.
        //
        // Sans transaction ouverte, PostgreSQL relache ce verrou a la fin de
        // l'instruction : la requete reussit, le compte est rendu, et la
        // garantie a disparu SANS AUCUN SIGNE. C'est exactement ce qui s'est
        // passe pendant une journee — le commentaire ci-dessus l'annoncait, et
        // aucun appelant n'ouvrait de transaction.
        //
        // UNE EXCEPTION PLUTOT QU'UN COMMENTAIRE. Un avertissement dans une
        // documentation XML ne protege rien ; cette ligne-ci fait echouer le
        // premier appel fautif, au premier essai, en nommant ce qu'il faut
        // faire. C'est la seule facon de ne pas reperdre cette garantie.
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "GetForUpdateAsync exige une transaction ouverte : sans elle, « FOR UPDATE » ne "
                + "verrouille rien et deux debits concurrents depassent le plafond en silence. "
                + "Enveloppez l'appel dans ITransactionRunner.ExecuteAsync.");
        }

        // « SELECT *, xmin » ET NON « SELECT * » : SANS CETTE COLONNE, CETTE
        // REQUETE ECHOUE A CHAQUE APPEL.
        //
        // xmin est une colonne SYSTEME de PostgreSQL, et « SELECT * » ne rend
        // jamais les colonnes systeme. Or l'agregat la mappe comme jeton de
        // concurrence, et EF Core exige que TOUTES les colonnes mappees soient
        // presentes dans le resultat d'un FromSql : il leve
        // « The required column 'xmin' was not present in the results of a
        // 'FromSql' operation ».
        //
        // LE DEBIT N'A DONC JAMAIS FONCTIONNE CONTRE UNE VRAIE BASE, et rien ne
        // pouvait le dire plus tot : cela compile, cela passe la revue, et le
        // fournisseur en memoire n'a pas de colonne systeme a oublier. C'est le
        // premier passage de ce projet de tests d'integration qui l'a trouve —
        // les deux debits concurrents echouaient tous les deux, en 29 ms, sans
        // meme atteindre l'attente qui les fait se chevaucher.
        //
        // L'OUTBOX FAIT UN « SELECT * » ET S'EN PORTE BIEN : son message n'a pas
        // de jeton de concurrence. C'est la combinaison FromSql + xmin qui
        // casse, pas FromSql.
        return context.BillingAccounts
            .FromSql($"""
                SELECT *, xmin FROM billing.billing_accounts
                 WHERE owner_type = {ownerType}
                   AND owner_id = {ownerId}
                   FOR UPDATE
                """)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(BillingAccount account) => context.BillingAccounts.Add(account);

    public Task<AccountMovement?> GetMovementAsync(Guid id, CancellationToken cancellationToken)
        => context.AccountMovements.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<AccountMovement?> FindMovementByKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
        => context.AccountMovements
            .FirstOrDefaultAsync(m => m.IdempotencyKey == idempotencyKey, cancellationToken);

    public void AddMovement(AccountMovement movement) => context.AccountMovements.Add(movement);
}
