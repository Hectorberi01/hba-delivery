using System.Globalization;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Domain.Accounts.Events;
using Hba.Billing.Domain.Exceptions;
using Hba.Billing.Domain.ValueObjects;

namespace Hba.Billing.Domain.Accounts;

/// <summary>
/// Le compte de facturation d'un donneur d'ordre professionnel.
/// </summary>
///
/// <remarks>
/// UNE SEULE RÈGLE DE DÉBIT, DANS LES DEUX MODES :
///
///     solde + plafond de crédit ≥ montant
///
/// En prépayé le plafond vaut zéro, donc le solde doit rester positif. En
/// postpayé il descend sous zéro, et ce solde négatif EST l'encours de la
/// période. Il n'y a donc pas deux modules, ni deux journaux, ni deux règles :
/// il y a un modèle et un paramètre. Écrire deux chemins aurait produit deux
/// façons de se tromper, et un jour un écart entre les deux que personne
/// n'aurait su expliquer.
///
/// CE QUE CET AGRÉGAT NE PROMET PAS, ET QUI EST ESSENTIEL À SAVOIR :
///
/// 1. IL N'EMPÊCHE PAS DEUX DÉBITS CONCURRENTS. Deux courses créées à la même
///    seconde sur le même compte liraient toutes deux le solde avant que
///    l'autre ne l'écrive, et le plafond serait dépassé. C'est un verrou
///    PESSIMISTE de ligne — `SELECT … FOR UPDATE` — qui sérialise, dans la
///    couche Infrastructure. Le domaine porte la RÈGLE ; la base porte la
///    SÉRIALISATION. Le jeton optimiste employé ailleurs dans ce dépôt ne
///    convient pas ici : sur un compte chargé, les tentatives échoueraient et
///    se répéteraient en cascade.
///
/// 2. IL N'EMPÊCHE PAS LE DOUBLE DÉBIT D'UN REJEU. C'est la contrainte
///    d'unicité sur la clé d'idempotence qui l'interdit. Vérifier en mémoire
///    supposerait de charger l'historique entier du compte à chaque course, et
///    donnerait l'illusion d'une garantie que le domaine ne peut pas offrir.
///
/// Ces deux points sont écrits ici parce qu'un lecteur qui croirait l'agrégat
/// suffisant écrirait un service faux sans jamais voir d'erreur en test.
/// </remarks>
public sealed class BillingAccount : AggregateRoot
{
    private BillingAccount()
    {
    }

    private BillingAccount(
        Guid id,
        string ownerType,
        string ownerId,
        SettlementMode mode,
        MoneyXof creditLimit,
        MoneyXof lowBalanceThreshold,
        DateTimeOffset createdAt)
        : base(id)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        SettlementMode = mode;
        CreditLimit = creditLimit;
        Balance = MoneyXof.Zero;
        LowBalanceThreshold = lowBalanceThreshold;
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
    }

    /// <summary>« merchant » ou « partner ».</summary>
    public string OwnerType { get; private set; } = string.Empty;

    /// <summary>Identifiant du titulaire chez Directory ou Identity.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    public SettlementMode SettlementMode { get; private set; }

    /// <summary>Zéro en prépayé, fixé par `finance` en postpayé.</summary>
    public MoneyXof CreditLimit { get; private set; } = null!;

    /// <summary>
    /// Solde courant. NÉGATIF = ENCOURS.
    /// </summary>
    ///
    /// <remarks>
    /// C'EST UN CACHE, ET LA VÉRITÉ EST AILLEURS : dans la somme des
    /// mouvements. Il est porté ici parce qu'un débit doit décider en lisant
    /// UNE ligne, pas en additionnant dix mille. Un contrôle périodique
    /// recalculera la somme et signalera l'écart — sans lui, un compte est
    /// invérifiable.
    /// </remarks>
    public MoneyXof Balance { get; private set; } = null!;

    public MoneyXof LowBalanceThreshold { get; private set; } = null!;

    public AccountStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Ce que le compte peut encore porter : solde + plafond.</summary>
    public MoneyXof Available => Balance.Add(CreditLimit);

    /// <summary>
    /// Ouvre un compte. TOUJOURS EN PRÉPAYÉ, et ce n'est pas une limite
    /// technique : le postpayé est un plafond que `finance` accorde ensuite, à
    /// un compte qui existe et dont on a vu le comportement. Naître postpayé
    /// reviendrait à faire crédit à quelqu'un qu'on ne connaît pas encore.
    /// </summary>
    public static BillingAccount Open(
        Guid id,
        string ownerType,
        string ownerId,
        MoneyXof lowBalanceThreshold,
        Actor actor,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(lowBalanceThreshold);

        if (lowBalanceThreshold.Amount < 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                "Un seuil d'alerte ne peut pas être négatif.");
        }

        var compte = new BillingAccount(
            id,
            ownerType.Trim(),
            ownerId.Trim(),
            Accounts.SettlementMode.Prepaid,
            MoneyXof.Zero,
            lowBalanceThreshold,
            createdAt);

        compte.Raise(new BillingAccountOpened(
            id,
            compte.OwnerType,
            compte.OwnerId,
            compte.SettlementMode,
            actor,
            createdAt));

        return compte;
    }

    /// <summary>
    /// Débite une course. Rend le mouvement à enregistrer.
    /// </summary>
    ///
    /// <remarks>
    /// LA CLÉ D'IDEMPOTENCE EST L'IDENTIFIANT DE LA COURSE, tiré par Delivery
    /// AVANT l'appel. C'est ce qui permet à un rejeu de `CreateDelivery` de
    /// retrouver le même débit au lieu d'en faire un second.
    ///
    /// LE REFUS EST IMMÉDIAT ET IL EST LE POINT DE TOUT CE SERVICE. Un donneur
    /// d'ordre au plafond doit l'apprendre AVANT que la course n'existe :
    /// c'est pour cela que Delivery appelle Billing en synchrone plutôt que de
    /// créer la course puis de publier un événement. Un `201 Created` suivi
    /// d'un échec silencieux serait pire que le refus.
    /// </remarks>
    public AccountMovement Debit(
        MoneyXof amount,
        string reference,
        string idempotencyKey,
        Actor actor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(amount);

        EnsureActive();

        if (amount.Amount <= 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                "Un débit porte sur un montant strictement positif.");
        }

        // LA RÈGLE, ET ELLE TIENT EN UNE LIGNE POUR LES DEUX MODES.
        if (Available.CompareTo(amount) < 0)
        {
            var manquant = amount.Subtract(Available).Amount;

            throw new DomainException(
                BillingErrorCodes.InsufficientBalance,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Solde insuffisant : il manque {manquant} XOF pour cette course."));
        }

        var avant = Balance;
        Balance = Balance.Subtract(amount);

        var mouvement = AccountMovement.Create(
            Id,
            MovementKind.Debit,
            amount.Negate(),
            Balance,
            reference,
            idempotencyKey,
            occurredAt);

        Raise(new AccountDebited(
            Id,
            mouvement.Id,
            amount.Amount,
            Balance.Amount,
            mouvement.Reference,
            actor,
            occurredAt));

        AnnoncerLeSeuilSiFranchi(avant, actor, occurredAt);

        return mouvement;
    }

    /// <summary>
    /// Crédite le compte : recharge, remboursement, règlement de facture ou
    /// ajustement.
    /// </summary>
    ///
    /// <remarks>
    /// UN COMPTE SUSPENDU SE CRÉDITE ENCORE. La suspension interdit de
    /// dépenser, pas de payer : un compte suspendu pour impayé qu'on ne
    /// pourrait plus régler serait suspendu pour toujours.
    /// </remarks>
    public AccountMovement Credit(
        MovementKind kind,
        MoneyXof amount,
        string reference,
        string idempotencyKey,
        Actor actor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(amount);

        if (kind == MovementKind.Debit)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                "Un débit passe par Debit, qui vérifie le plafond.");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidAmount,
                "Un crédit porte sur un montant strictement positif.");
        }

        Balance = Balance.Add(amount);

        var mouvement = AccountMovement.Create(
            Id,
            kind,
            amount,
            Balance,
            reference,
            idempotencyKey,
            occurredAt);

        Raise(new AccountCredited(
            Id,
            mouvement.Id,
            kind,
            amount.Amount,
            Balance.Amount,
            mouvement.Reference,
            actor,
            occurredAt));

        return mouvement;
    }

    /// <summary>
    /// Annule un débit qui n'a pas abouti : rend l'argent, exactement.
    /// </summary>
    ///
    /// <remarks>
    /// C'EST LA COMPENSATION DU DÉBIT ORPHELIN, ET ELLE EXISTE PARCE QUE LA
    /// CRÉATION D'UNE COURSE N'EST PAS UNE TRANSACTION DISTRIBUÉE. Delivery
    /// débite AVANT de créer la course ; si l'écriture de la course échoue
    /// ensuite, le donneur d'ordre a payé une course qui n'existe pas.
    ///
    /// UNE ANNULATION N'EST PAS UN CRÉDIT LIBRE, ET C'EST TOUTE LA DIFFÉRENCE
    /// D'AUTORISATION. Créditer, c'est décider d'un montant : réservé au
    /// back-office financier, sinon un commerçant se donnerait du solde. Annuler
    /// un débit, c'est RENDRE CE QUI A ÉTÉ PRIS : le montant n'est pas choisi,
    /// il est lu sur l'écriture qu'on annule. Le titulaire peut donc la
    /// demander pour lui-même sans qu'il y ait de porte à forcer — le pire qu'il
    /// puisse faire est de récupérer son propre argent.
    ///
    /// LE MOUVEMENT ANNULÉ DOIT ÊTRE UN DÉBIT DE CE COMPTE. Sans ces deux
    /// vérifications, une clé mal choisie ferait rembourser le débit d'un autre
    /// titulaire sur le compte de celui qui appelle.
    ///
    /// LE SENS EST « REFUND » ET NON « ADJUSTMENT ». Un ajustement corrige une
    /// erreur d'écriture ; ici l'écriture était juste, c'est la contrepartie qui
    /// manque. Le relevé doit les distinguer, sinon personne ne saura, six mois
    /// plus tard, si le compte a été corrigé ou remboursé.
    /// </remarks>
    public AccountMovement ReverseDebit(AccountMovement debit, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(debit);
        ArgumentNullException.ThrowIfNull(actor);

        if (debit.AccountId != Id)
        {
            throw new DomainException(
                BillingErrorCodes.MovementNotOnThisAccount,
                "Ce mouvement n'appartient pas à ce compte.");
        }

        if (debit.Kind != MovementKind.Debit)
        {
            throw new DomainException(
                BillingErrorCodes.NotADebit,
                "Seul un débit s'annule par un remboursement.");
        }

        // LE MONTANT D'UN DÉBIT EST NÉGATIF : on le rend à l'endroit.
        return Credit(
            MovementKind.Refund,
            debit.Amount.Negate(),
            debit.Reference,
            AccountMovement.ReversalKey(debit.IdempotencyKey),
            actor,
            occurredAt);
    }

    /// <summary>
    /// Corrige une erreur, dans un sens ou dans l'autre.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUN MOUVEMENT N'EST MODIFIÉ NI SUPPRIMÉ, JAMAIS. Une erreur se
    /// corrige par une écriture de sens inverse, qui laisse les deux traces.
    /// C'est ce qui rend un compte vérifiable ; une ligne retouchée romprait la
    /// vérification en silence, ce qui est la pire des deux façons de se
    /// tromper.
    ///
    /// UN AJUSTEMENT NÉGATIF NE VÉRIFIE PAS LE PLAFOND. Il corrige une écriture
    /// passée, il ne dépense rien : le refuser laisserait un compte faux, sans
    /// aucun moyen de le remettre juste.
    /// </remarks>
    public AccountMovement Adjust(
        MoneyXof signedAmount,
        string reference,
        string idempotencyKey,
        Actor actor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(signedAmount);

        var avant = Balance;
        Balance = Balance.Add(signedAmount);

        var mouvement = AccountMovement.Create(
            Id,
            MovementKind.Adjustment,
            signedAmount,
            Balance,
            reference,
            idempotencyKey,
            occurredAt);

        Raise(new AccountCredited(
            Id,
            mouvement.Id,
            MovementKind.Adjustment,
            signedAmount.Amount,
            Balance.Amount,
            mouvement.Reference,
            actor,
            occurredAt));

        AnnoncerLeSeuilSiFranchi(avant, actor, occurredAt);

        return mouvement;
    }

    /// <summary>
    /// Accorde un plafond de crédit : le compte devient postpayé.
    /// </summary>
    ///
    /// <remarks>
    /// C'EST LA SEULE DIFFÉRENCE ENTRE LES DEUX MODES. Pas un autre agrégat,
    /// pas un autre journal : un nombre qui cesse d'être zéro.
    ///
    /// NON IMPLÉMENTÉ CÔTÉ SERVICE, ET C'EST VOULU. La décision du 29 septembre
    /// 2026 ne met en service que le prépayé ; cette méthode existe parce que
    /// le modèle la porte, mais rien ne l'appelle tant que la clôture
    /// mensuelle, la facture et la relance n'existent pas. L'écrire sans elles
    /// donnerait un encours que personne ne recouvre.
    /// </remarks>
    public void GrantCreditLimit(MoneyXof limit, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(limit);

        if (limit.Amount < 0)
        {
            throw new DomainException(
                BillingErrorCodes.InvalidCreditLimit,
                "Un plafond de crédit ne peut pas être négatif.");
        }

        CreditLimit = limit;

        // « Accounts.SettlementMode » EN TOUTES LETTRES : la propriété et le
        // type portent le même nom. C# sait le résoudre, mais le lecteur
        // hésite, et un jour quelqu'un « corrigera » l'un des deux.
        SettlementMode = limit.Amount > 0
            ? Accounts.SettlementMode.Postpaid
            : Accounts.SettlementMode.Prepaid;

        Raise(new CreditLimitGranted(Id, limit.Amount, SettlementMode, actor, occurredAt));
    }

    public void Suspend(Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == AccountStatus.Suspended)
        {
            return;
        }

        Status = AccountStatus.Suspended;
        Raise(new AccountStatusChanged(Id, Status, actor, occurredAt));
    }

    public void Reactivate(Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == AccountStatus.Active)
        {
            return;
        }

        Status = AccountStatus.Active;
        Raise(new AccountStatusChanged(Id, Status, actor, occurredAt));
    }

    private void EnsureActive()
    {
        if (Status != AccountStatus.Active)
        {
            throw new DomainException(
                BillingErrorCodes.AccountSuspended,
                "Ce compte est suspendu : aucune course ne peut être commandée.");
        }
    }

    /// <summary>
    /// Émet l'alerte UNIQUEMENT si ce mouvement a fait franchir le seuil.
    /// </summary>
    private void AnnoncerLeSeuilSiFranchi(MoneyXof avant, Actor actor, DateTimeOffset occurredAt)
    {
        var etaitAuDessus = avant.CompareTo(LowBalanceThreshold) >= 0;
        var estEnDessous = Balance.CompareTo(LowBalanceThreshold) < 0;

        if (etaitAuDessus && estEnDessous)
        {
            Raise(new LowBalanceReached(
                Id,
                Balance.Amount,
                LowBalanceThreshold.Amount,
                actor,
                occurredAt));
        }
    }
}
