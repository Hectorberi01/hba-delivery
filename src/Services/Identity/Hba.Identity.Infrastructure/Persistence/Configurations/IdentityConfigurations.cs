using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.Partners;
using Hba.Identity.Domain.Sessions;
using Hba.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hba.Identity.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("accounts");
        builder.HasKey(a => a.Id);

        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.Roles);

        builder.Property(a => a.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // Objets-valeurs à un seul champ : une conversion, pas une table jointe.
        // EF n'appelle jamais un convertisseur avec null, d'où des expressions
        // non nullables.
        builder.Property(a => a.Phone)
            .HasColumnName("phone")
            .HasMaxLength(20)
            .HasConversion(phone => phone!.Value, value => PhoneNumber.Create(value));

        builder.Property(a => a.Email)
            .HasColumnName("email")
            .HasMaxLength(200)
            .HasConversion(email => email!.Value, value => EmailAddress.Create(value));

        builder.Property(a => a.Password)
            .HasColumnName("password_hash")
            .HasMaxLength(256)
            .HasConversion(hash => hash!.Encoded, value => PasswordHash.FromEncoded(value));

        // Propriété privée : EF la trouve par son nom.
        builder.Property<string>("RolesRaw").HasColumnName("roles").HasMaxLength(200).IsRequired();

        builder.Property(a => a.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.StatusReason).HasMaxLength(500);
        builder.Property(a => a.MerchantId).HasColumnName("merchant_id").HasMaxLength(64);
        builder.Property(a => a.DriverId).HasColumnName("driver_id").HasMaxLength(64);

        // Un numéro et une adresse identifient un compte : l'unicité est garantie
        // par la base, pas seulement par une vérification dans le code.
        builder.HasIndex(a => a.Phone).IsUnique().HasFilter("phone IS NOT NULL");
        builder.HasIndex(a => a.Email).IsUnique().HasFilter("email IS NOT NULL");
        builder.HasIndex(a => a.MerchantId);
        builder.HasIndex(a => a.DriverId).IsUnique().HasFilter("driver_id IS NOT NULL");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Ignore(t => t.DomainEvents);

        builder.Property(t => t.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        // Seule l'empreinte est stockée. L'index unique évite en plus qu'une
        // collision passe inaperçue.
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(88).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.DeviceId).HasColumnName("device_id").HasMaxLength(128);
        builder.Property(t => t.RevokedReason).HasMaxLength(200);

        builder.HasIndex(t => t.SessionId);
        builder.HasIndex(t => new { t.AccountId, t.ExpiresAt });
    }
}

internal sealed class PartnerClientConfiguration : IEntityTypeConfiguration<PartnerClient>
{
    public void Configure(EntityTypeBuilder<PartnerClient> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("partner_clients");
        builder.HasKey(p => p.Id);

        builder.Ignore(p => p.DomainEvents);
        builder.Ignore(p => p.Scopes);

        builder.Property(p => p.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Source).HasMaxLength(32).IsRequired();

        builder.Property(p => p.ClientId).HasColumnName("client_id").HasMaxLength(64).IsRequired();
        builder.HasIndex(p => p.ClientId).IsUnique();

        builder.Property(p => p.SecretHash)
            .HasColumnName("client_secret_hash")
            .HasMaxLength(256)
            .IsRequired()
            .HasConversion(hash => hash.Encoded, value => PasswordHash.FromEncoded(value));

        builder.Property(p => p.WebhookUrl).HasColumnName("webhook_url").HasMaxLength(500);

        // Chiffré et non haché : la Partner API doit pouvoir le relire pour
        // signer ses appels sortants.
        builder.Property(p => p.ProtectedWebhookSecret)
            .HasColumnName("webhook_secret_protected")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property<string>("ScopesRaw").HasColumnName("scopes").HasMaxLength(500).IsRequired();
    }
}
