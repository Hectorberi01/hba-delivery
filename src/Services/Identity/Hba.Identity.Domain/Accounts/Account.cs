using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Accounts.Events;
using Hba.Identity.Domain.ValueObjects;

namespace Hba.Identity.Domain.Accounts;

/// <summary>
/// Compte d'accès. Identity ne sait rien d'autre d'une personne : ni adresse
/// favorite, ni point de collecte, ni document KYC. Ces choses appartiennent à
/// Directory et à Driver.
/// </summary>
public sealed class Account : AggregateRoot
{
    private readonly List<string> _roles = [];

    private Account()
    {
    }

    private Account(Guid id, string displayName, DateTimeOffset createdAt) : base(id)
    {
        DisplayName = displayName;
        CreatedAt = createdAt;
        Status = AccountStatus.Active;
    }

    public PhoneNumber? Phone { get; private set; }

    public EmailAddress? Email { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public IReadOnlyList<string> Roles => _roles.AsReadOnly();

    /// <summary>
    /// Forme persistée des rôles : une colonne texte, pas une table de jointure
    /// pour au plus quatre valeurs figées. EF Core lit et écrit cette propriété
    /// privée ; le domaine ne s'en sert jamais.
    /// </summary>
    private string RolesRaw
    {
        get => string.Join(',', _roles);
        set
        {
            _roles.Clear();

            if (!string.IsNullOrWhiteSpace(value))
            {
                _roles.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    public AccountStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    /// <summary>Renseigné pour merchant_owner et merchant_staff.</summary>
    public string? MerchantId { get; private set; }

    /// <summary>Renseigné une fois le profil livreur créé côté service Driver.</summary>
    public string? DriverId { get; private set; }

    public PasswordHash? Password { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>
    /// Échecs consécutifs de mot de passe. L'OTP a son propre compteur, porté
    /// par le défi lui-même.
    /// </summary>
    public int FailedPasswordAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public bool HasPassword => Password is not null;

    public bool IsLocked(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;

    /// <summary>
    /// Compte créé par vérification de numéro. Seuls customer et driver naissent
    /// ainsi : aucun rôle du back-office ni du portail commerçant ne peut
    /// apparaître par simple possession d'une carte SIM.
    /// </summary>
    public static Account RegisterWithPhone(
        Guid id,
        PhoneNumber phone,
        string displayName,
        string role,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(phone);

        if (!Domain.Roles.SelfServiceByOtp.Contains(role))
        {
            throw new ForbiddenException(
                $"Le rôle {role} ne peut pas être obtenu par vérification de numéro.");
        }

        var account = new Account(id, NormalizeName(displayName), now) { Phone = phone };
        account._roles.Add(role);

        // L'acteur est le compte lui-même : personne d'autre n'est intervenu.
        var actor = Actor.Human(
            role == Domain.Roles.Driver ? ActorKind.Driver : ActorKind.Customer,
            id.ToString());

        account.Raise(new AccountRegistered(
            id,
            phone.Value,
            null,
            account.DisplayName,
            account.Roles,
            null,
            actor,
            now));

        return account;
    }

    /// <summary>Compte du back-office, créé par un administrateur.</summary>
    public static Account CreateBackOffice(
        Guid id,
        EmailAddress email,
        string displayName,
        IEnumerable<string> roles,
        PasswordHash password,
        Actor actor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(actor);

        var requested = Normalize(roles);

        if (requested.Count == 0 || !requested.All(Domain.Roles.BackOffice.Contains))
        {
            throw new DomainException(
                "INVALID_ROLES",
                "Un compte de back-office porte uniquement des rôles admin, ops, support ou finance.");
        }

        var account = new Account(id, NormalizeName(displayName), now)
        {
            Email = email,
            Password = password,
        };
        account._roles.AddRange(requested);

        account.Raise(new AccountRegistered(
            id, null, email.Value, account.DisplayName, account.Roles, null, actor, now));

        return account;
    }

    /// <summary>Compte rattaché à un commerçant, créé par un administrateur.</summary>
    public static Account CreateMerchantUser(
        Guid id,
        string merchantId,
        EmailAddress? email,
        PhoneNumber? phone,
        string displayName,
        string role,
        PasswordHash password,
        Actor actor,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantId);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(actor);

        if (!Domain.Roles.Merchant.Contains(role))
        {
            throw new DomainException(
                "INVALID_ROLES",
                "Un compte de commerçant porte merchant_owner ou merchant_staff.");
        }

        if (email is null && phone is null)
        {
            throw new DomainException(
                "MISSING_LOGIN",
                "Un compte de commerçant a besoin d'un e-mail ou d'un téléphone pour se connecter.");
        }

        var account = new Account(id, NormalizeName(displayName), now)
        {
            Email = email,
            Phone = phone,
            MerchantId = merchantId,
            Password = password,
        };
        account._roles.Add(role);

        account.Raise(new AccountRegistered(
            id, phone?.Value, email?.Value, account.DisplayName, account.Roles, merchantId, actor, now));

        return account;
    }

    /// <summary>
    /// Rattache le profil livreur créé par le service Driver. Le compte existe
    /// avant le profil : on s'inscrit, puis on dépose son KYC.
    /// </summary>
    public void LinkDriverProfile(string driverId, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (!_roles.Contains(Domain.Roles.Driver))
        {
            throw new ForbiddenException("Ce compte n'est pas un compte livreur.");
        }

        if (DriverId == driverId)
        {
            return;
        }

        if (DriverId is not null)
        {
            throw new DomainException("DRIVER_ALREADY_LINKED", "Ce compte est déjà rattaché à un autre livreur.");
        }

        DriverId = driverId;
        Raise(new AccountLinkedToDriver(Id, driverId, actor, now));
    }

    public void SetRoles(IEnumerable<string> roles, string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(actor);

        var requested = Normalize(roles);

        if (requested.Count == 0)
        {
            throw new DomainException("INVALID_ROLES", "Un compte doit porter au moins un rôle.");
        }

        if (requested.Contains(Domain.Roles.Partner))
        {
            throw new ForbiddenException(
                "Le rôle partner appartient à un client OAuth, pas à un compte de personne.");
        }

        if (requested.Contains(Domain.Roles.MerchantOwner) || requested.Contains(Domain.Roles.MerchantStaff))
        {
            if (MerchantId is null)
            {
                throw new DomainException(
                    "MISSING_MERCHANT",
                    "Un rôle de commerçant exige que le compte soit rattaché à un commerçant.");
            }
        }

        if (requested.SequenceEqual(_roles))
        {
            return;
        }

        var previous = _roles.ToList();
        _roles.Clear();
        _roles.AddRange(requested);

        Raise(new AccountRolesChanged(Id, previous, Roles, reason, actor, now));
    }

    public void Suspend(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == AccountStatus.Suspended)
        {
            return;
        }

        Status = AccountStatus.Suspended;
        StatusReason = reason;

        Raise(new AccountStatusChanged(Id, AccountStatus.Active, AccountStatus.Suspended, reason, actor, now));
    }

    public void Reactivate(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == AccountStatus.Active)
        {
            return;
        }

        Status = AccountStatus.Active;
        StatusReason = reason;
        FailedPasswordAttempts = 0;
        LockedUntil = null;

        Raise(new AccountStatusChanged(Id, AccountStatus.Suspended, AccountStatus.Active, reason, actor, now));
    }

    public void SetPassword(PasswordHash password, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!_roles.Any(Domain.Roles.PasswordBased.Contains))
        {
            throw new ForbiddenException(
                "Ce compte se connecte par code SMS. Un mot de passe n'aurait aucun usage et élargirait la surface d'attaque.");
        }

        Password = password;
        FailedPasswordAttempts = 0;
        LockedUntil = null;

        Raise(new AccountPasswordChanged(Id, actor, now));
    }

    /// <summary>
    /// Vérifie le mot de passe et met à jour le compteur d'échecs. Au cinquième
    /// échec consécutif, le compte se verrouille quinze minutes.
    /// </summary>
    public bool TryPassword(string? candidate, DateTimeOffset now)
    {
        if (IsLocked(now))
        {
            throw new DomainException(
                "ACCOUNT_LOCKED",
                "Trop de tentatives. Réessayez dans quelques minutes.");
        }

        if (Password is null || !Password.Verify(candidate))
        {
            FailedPasswordAttempts++;

            if (FailedPasswordAttempts >= 5)
            {
                LockedUntil = now.AddMinutes(15);
            }

            return false;
        }

        FailedPasswordAttempts = 0;
        LockedUntil = null;
        return true;
    }

    /// <summary>Appelé après une authentification réussie, quel que soit le moyen.</summary>
    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        FailedPasswordAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>
    /// Un compte suspendu ne reçoit plus de jeton. Les jetons déjà émis restent
    /// valides jusqu'à expiration : c'est pour cela que la durée de vie d'un
    /// jeton d'accès est courte, et que la suspension révoque les sessions.
    /// </summary>
    public void EnsureCanAuthenticate()
    {
        if (Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException("Ce compte est suspendu.");
        }
    }

    private static List<string> Normalize(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var normalized = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unknown = normalized.Where(r => !Domain.Roles.All.Contains(r)).ToList();

        if (unknown.Count > 0)
        {
            throw new DomainException("UNKNOWN_ROLE", $"Rôles inconnus : {string.Join(", ", unknown)}.");
        }

        return normalized;
    }

    private static string NormalizeName(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? "Sans nom" : displayName.Trim();
}
