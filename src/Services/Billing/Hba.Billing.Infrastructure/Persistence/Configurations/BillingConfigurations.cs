using Hba.Billing.Domain.Accounts;
using Hba.Billing.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Billing.Infrastructure.Persistence.Configurations;

internal sealed class BillingAccountConfiguration : IEntityTypeConfiguration<BillingAccount>
{
    public void Configure(EntityTypeBuilder<BillingAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("billing_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.OwnerType).HasColumnName("owner_type").HasMaxLength(16).IsRequired();
        builder.Property(a => a.OwnerId).HasColumnName("owner_id").HasMaxLength(64).IsRequired();
        builder.Property(a => a.SettlementMode).HasColumnName("settlement_mode").IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        // Entiers de francs CFA : le XOF n'a pas de subdivision (ADR 0006).
        // LE SOLDE PEUT ETRE NEGATIF — c'est l'encours d'un compte postpaye —
        // et aucune contrainte de base ne doit l'interdire.
        builder.Property(a => a.Balance)
            .HasColumnName("balance_xof")
            .HasConversion(m => m.Amount, v => MoneyXof.From(v))
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(a => a.CreditLimit)
            .HasColumnName("credit_limit_xof")
            .HasConversion(m => m.Amount, v => MoneyXof.From(v))
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(a => a.LowBalanceThreshold)
            .HasColumnName("low_balance_threshold_xof")
            .HasConversion(m => m.Amount, v => MoneyXof.From(v))
            .HasColumnType("bigint")
            .IsRequired();

        // UN SEUL COMPTE PAR TITULAIRE, GARANTI PAR LA BASE. Deux comptes pour
        // un meme commercant, ce sont deux soldes : le jour ou l'un est a sec
        // et l'autre plein, personne ne saura lequel est le bon. Le controle
        // applicatif de OpenAccountHandler ne suffit pas — deux ouvertures
        // concurrentes le franchiraient toutes les deux.
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId }).IsUnique();

        // LE JETON OPTIMISTE EST LA, MAIS CE N'EST PAS LUI QUI PROTEGE LE
        // SOLDE. Le debit prend un verrou PESSIMISTE de ligne (voir
        // GetForUpdateAsync) parce que sur un compte charge, les tentatives
        // optimistes echoueraient en cascade. Ce jeton reste utile pour les
        // ecritures rares du back-office — suspension, plafond accorde — qui
        // ne passent pas par le verrou.
        builder.Property(a => a.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.Ignore(a => a.Available);
    }
}

internal sealed class AccountMovementConfiguration : IEntityTypeConfiguration<AccountMovement>
{
    public void Configure(EntityTypeBuilder<AccountMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("account_movements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(m => m.Kind).HasColumnName("kind").IsRequired();

        // MONTANT SIGNE : positif au credit, negatif au debit. La somme de
        // cette colonne doit refaire « balance_xof » du compte, a tout instant.
        builder.Property(m => m.Amount)
            .HasColumnName("amount_xof")
            .HasConversion(m => m.Amount, v => MoneyXof.From(v))
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(m => m.BalanceAfter)
            .HasColumnName("balance_after_xof")
            .HasConversion(m => m.Amount, v => MoneyXof.From(v))
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(m => m.Reference).HasColumnName("reference").HasMaxLength(128).IsRequired();
        builder.Property(m => m.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        // VOICI CE QUI INTERDIT REELLEMENT LE DOUBLE DEBIT.
        //
        // Le magasin d'idempotence de la couche Application evite le rejeu
        // ORDINAIRE : il repond avant meme de prendre le verrou. Mais deux
        // appels STRICTEMENT concurrents avec la meme cle passeraient tous
        // deux cette lecture, et c'est cette contrainte-ci qui rejette le
        // second a l'ecriture.
        //
        // UNE CEINTURE ET DES BRETELLES, DELIBEREMENT. Un double debit ne se
        // voit pas : le compte est juste un peu plus bas qu'il ne devrait, et
        // personne ne s'en apercoit avant le rapprochement du mois.
        builder.HasIndex(m => m.IdempotencyKey).IsUnique();

        // LE RELEVE SE LIT PAR COMPTE ET PAR DATE. Sans cet index, afficher
        // les mouvements d'un commercant balaierait la table entiere.
        builder.HasIndex(m => new { m.AccountId, m.CreatedAt });

        // PAS DE CLE ETRANGERE VERS LE COMPTE, ET C'EST UN CHOIX. Le mouvement
        // est une ecriture comptable : il survit a tout, y compris a un compte
        // qu'on voudrait un jour archiver. La coherence est tenue par le code
        // qui les ecrit ensemble, dans la meme transaction.
    }
}
