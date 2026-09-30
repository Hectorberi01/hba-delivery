using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Accounts.Events;
using Hba.Identity.Domain.Exceptions;
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

    /// <summary>Quand le titulaire a demandé la suppression, ou null.</summary>
    public DateTimeOffset? DeletionRequestedAt { get; private set; }

    /// <summary>
    /// Le jour où l'effacement aura lieu si rien ne l'annule, ou null.
    /// </summary>
    ///
    /// <remarks>
    /// LA DATE EST STOCKEE, ELLE N'EST PAS RECALCULEE. « DeletionRequestedAt
    /// plus trente jours » donnerait une echeance qui BOUGE le jour ou le delai
    /// change en configuration : un client a qui l'on a promis le 28 octobre
    /// verrait sa date se decaler toute seule. Ce qui a ete annonce est ce qui
    /// s'applique.
    /// </remarks>
    public DateTimeOffset? DeletionScheduledFor { get; private set; }

    public bool IsPendingDeletion => Status == AccountStatus.PendingDeletion;

    /// <summary>
    /// Échecs consécutifs de mot de passe. L'OTP a son propre compteur, porté
    /// par le défi lui-même.
    /// </summary>
    public int FailedPasswordAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// Consentement à recevoir des messages WhatsApp. FAUX PAR DEFAUT, ET CE
    /// N'EST PAS NEGOCIABLE : Meta exige un consentement explicite avant tout
    /// message de gabarit, et un défaut à vrai serait un consentement présumé,
    /// c'est-à-dire pas un consentement.
    ///
    /// Il porte sur le titulaire du compte. Le destinataire d'un colis n'en a
    /// jamais donné — il n'a pas de compte — donc son code de remise part par
    /// SMS, quel que soit le coût.
    /// </summary>
    public bool WhatsAppOptIn { get; private set; }

    /// <summary>Date du consentement en cours. Nul s'il n'a jamais été accordé.</summary>
    public DateTimeOffset? WhatsAppOptInAt { get; private set; }
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
        DateTimeOffset now,
        bool whatsAppOptIn = false)
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

        if (whatsAppOptIn)
        {
            // Passe par la méthode plutôt que par le champ : le consentement
            // recueilli à l'inscription doit laisser la même trace auditable
            // que celui accordé plus tard.
            account.SetWhatsAppConsent(true, actor, now);
        }

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
                IdentityErrorCodes.InvalidRoles,
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
                IdentityErrorCodes.InvalidRoles,
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
            throw new DomainException(IdentityErrorCodes.DriverAlreadyLinked, "Ce compte est déjà rattaché à un autre livreur.");
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
            throw new DomainException(IdentityErrorCodes.InvalidRoles, "Un compte doit porter au moins un rôle.");
        }

        if (requested.Contains(Domain.Roles.Partner))
        {
            throw new ForbiddenException(
                "Le rôle partner appartient à un client OAuth, pas à un compte de personne.");
        }

        // LE RÔLE SERVICE NON PLUS, ET SON OUBLI ÉTAIT UN EFFET DE BORD DE SA
        // PROPRE CRÉATION, LE 30 SEPTEMBRE 2026.
        //
        // Il a été ajouté à Roles.All ce jour-là — il faut bien qu'un rôle émis
        // par IssueServiceToken soit connu — et retiré de BackOffice. Mais
        // Normalize accepte tout ce qui est dans All, et cette porte est restée
        // ouverte : un administrateur pouvait se l'attribuer, ou l'attribuer à
        // quelqu'un.
        //
        // CE N'EST PAS UN RÔLE DE PLUS, C'EST UN CONTOURNEMENT DE TOUS LES
        // AUTRES. « service » est ce que présente le système quand il agit pour
        // son propre compte : BillingAccess.EnsureCanReverse l'accepte là où il
        // refuse le titulaire, DeleteOwnerMedia lui confie l'effacement de
        // pièces d'identité, et CallerContext.ToActor en fait un Actor.Service.
        // Posé sur un compte de personne, il donne à cette personne les droits
        // que le référentiel réserve aux services.
        //
        // Il ne s'obtient que par IssueServiceToken, contre un secret de
        // service, et ne se pose jamais sur un compte.
        if (requested.Contains(Domain.Roles.Service))
        {
            throw new ForbiddenException(
                "Le rôle service appartient au système, pas à un compte de personne.");
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

    /// <summary>
    /// Le titulaire demande la suppression de son compte.
    /// </summary>
    ///
    /// <remarks>
    /// UN COMPTE SUSPENDU NE PEUT PAS SE SUPPRIMER. La suspension est en cours
    /// d'instruction — impayé, signalement, fraude — et laisser disparaître le
    /// dossier au milieu reviendrait à donner l'effacement comme issue à qui
    /// cherche à s'y soustraire. Le refus le dit, et renvoie au support.
    ///
    /// DEUX DEMANDES DE SUITE NE REPOUSSENT PAS L'ECHEANCE. La seconde rend la
    /// première telle quelle : sinon un client qui appuie deux fois croirait
    /// avoir confirmé alors qu'il aurait repoussé son propre effacement.
    /// </remarks>
    public void RequestDeletion(DateTimeOffset scheduledFor, Actor actor, DateTimeOffset now)
    {
        if (Status == AccountStatus.Suspended)
        {
            throw new ForbiddenException(
                "Ce compte est suspendu : sa suppression passe par le support.");
        }

        if (Status == AccountStatus.PendingDeletion)
        {
            return;
        }

        if (scheduledFor <= now)
        {
            throw new DomainException(
                IdentityErrorCodes.DeletionDateInPast,
                "Un effacement déjà échu ne laisserait aucune place à l'annulation.");
        }

        Status = AccountStatus.PendingDeletion;
        StatusReason = "Suppression demandée par le titulaire.";
        DeletionRequestedAt = now;
        DeletionScheduledFor = scheduledFor;

        Raise(new AccountDeletionRequested(Id, scheduledFor, actor, now));
    }

    /// <summary>Le titulaire revient sur sa demande.</summary>
    public void CancelDeletion(Actor actor, DateTimeOffset now)
    {
        if (Status != AccountStatus.PendingDeletion)
        {
            return;
        }

        Status = AccountStatus.Active;
        StatusReason = "Suppression annulée par le titulaire.";
        DeletionRequestedAt = null;
        DeletionScheduledFor = null;

        Raise(new AccountDeletionCancelled(Id, actor, now));
    }

    /// <summary>
    /// Marque le compte comme effacé, juste avant que sa ligne ne disparaisse.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE NE SUPPRIME RIEN : elle lève l'événement que les AUTRES services
    /// attendent — Directory pour le profil et les adresses, Media pour la
    /// photo. La suppression de la ligne, elle, se fait dans la même
    /// transaction par le dépôt, et c'est l'Outbox qui garantit que l'une ne
    /// parte pas sans l'autre.
    ///
    /// ELLE EXIGE QUE L'ECHEANCE SOIT ECHUE. Sans ce garde-fou, une erreur
    /// d'ordonnancement effacerait un compte dont le titulaire a encore vingt
    /// jours pour changer d'avis, et rien ne le signalerait — la ligne n'existe
    /// plus pour en témoigner.
    /// </remarks>
    public void MarkErased(Actor actor, DateTimeOffset now)
    {
        if (Status != AccountStatus.PendingDeletion || DeletionScheduledFor is null)
        {
            throw new DomainException(
                IdentityErrorCodes.DeletionNotRequested,
                "Ce compte n'a pas demandé sa suppression.");
        }

        if (DeletionScheduledFor > now)
        {
            throw new DomainException(
                IdentityErrorCodes.DeletionNotDue,
                $"L'effacement est prévu le {DeletionScheduledFor:yyyy-MM-dd} : il reste au titulaire "
                + "le temps de changer d'avis.");
        }

        Raise(new AccountErased(Id, DeletionRequestedAt ?? now, actor, now));
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

    /// <summary>
    /// Accorde ou retire le consentement WhatsApp. Idempotent : redonner un
    /// consentement déjà donné n'émet aucun événement, pour que le journal
    /// d'audit garde la date du consentement RÉEL et non celle du dernier appel.
    /// </summary>
    public void SetWhatsAppConsent(bool granted, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (WhatsAppOptIn == granted)
        {
            return;
        }

        WhatsAppOptIn = granted;

        // Le retrait efface la date : il ne reste aucune trace d'un consentement
        // en cours, seulement l'événement qui dit qu'il a existé puis cessé.
        WhatsAppOptInAt = granted ? now : null;

        Raise(new AccountWhatsAppConsentChanged(Id, granted, actor, now));
    }

    /// <summary>
    /// Le compte peut-il recevoir son code de connexion par WhatsApp ?
    /// Un compte suspendu ne se connecte pas du tout, la question ne se pose
    /// donc que pour un compte actif qui a consenti.
    /// </summary>
    public bool CanReceiveWhatsApp => WhatsAppOptIn && Status == AccountStatus.Active;

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
            throw new DomainException(IdentityErrorCodes.UnknownRole, $"Rôles inconnus : {string.Join(", ", unknown)}.");
        }

        return normalized;
    }

    private static string NormalizeName(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? "Sans nom" : displayName.Trim();
}
