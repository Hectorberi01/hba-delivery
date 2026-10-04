using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.Exceptions;
using Hba.Payment.Domain.Payments.Events;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Domain.Payments;

/// <summary>
/// Intention de paiement pour une MISSION : une livraison, ou une course.
///
/// « MISSION », PAS « LIVRAISON », DEPUIS LE 1er OCTOBRE 2026. Cet agregat
/// portait DeliveryId en dur et refusait un identifiant vide : une course de
/// personnes ne pouvait pas etre payee du tout.
///
/// ELLE NAIT EN ATTENTE ET NE SE RESOUT JAMAIS TOUTE SEULE. La creation ouvre
/// une transaction chez le fournisseur et rend une adresse de paiement ; ce qui
/// se passe ensuite arrive par le webhook verifie, jamais par le retour de
/// l'application cliente. Un client qui revient sur l'ecran « merci » n'a rien
/// prouve : il a seulement ferme un navigateur.
/// </summary>
public sealed class PaymentIntent : AggregateRoot
{
    private PaymentIntent()
    {
    }

    private PaymentIntent(
        Guid id,
        ServiceType serviceType,
        Guid referenceId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        PaymentMethod method,
        PaymentPurpose purpose,
        string? driverId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        Purpose = purpose;
        DriverId = driverId;
        ServiceType = serviceType;
        ReferenceId = referenceId;
        PayerId = payerId;
        PayerPhone = payerPhone;
        Amount = amount;
        Method = method;
        Status = PaymentStatus.Pending;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>Pourquoi ce paiement a lieu : la mission, ou un pourboire.</summary>
    public PaymentPurpose Purpose { get; private set; }

    /// <summary>
    /// Le livreur a crediter. RENSEIGNE POUR UN POURBOIRE, NUL AUTREMENT.
    /// </summary>
    ///
    /// <remarks>
    /// IL EST FIGE A LA CREATION, PAS RELU AU MOMENT DU WEBHOOK. Le webhook
    /// arrive des minutes plus tard : relire alors qui a porte la mission ferait
    /// dependre le credit d'un etat qui a pu changer. Celui qui a ete servi est
    /// celui qui touche.
    ///
    /// LE PRIX DE LA MISSION, LUI, N'EN A PAS BESOIN : il est credite par
    /// l'evenement de fin de mission, qui porte deja son chauffeur.
    /// </remarks>
    public string? DriverId { get; private set; }

    /// <summary>La nature de la mission payee.</summary>
    public ServiceType ServiceType { get; private set; }
    public Guid ReferenceId { get; private set; }

    /// <summary>Identifiant du compte qui paie. C'est un client, jamais un partenaire.</summary>
    public string PayerId { get; private set; } = string.Empty;

    /// <summary>Numero au format E.164, transmis au fournisseur pour le rapprochement.</summary>
    public string PayerPhone { get; private set; } = string.Empty;

    public MoneyXof Amount { get; private set; } = null!;

    public PaymentStatus Status { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>
    /// Reference de la transaction chez le fournisseur. JAMAIS EXPOSEE HORS DU
    /// SERVICE ET DU BACK-OFFICE FINANCE, comme le dit le contrat.
    /// </summary>
    public string? ProviderReference { get; private set; }

    /// <summary>
    /// Adresse de la page de paiement du fournisseur. C'est elle que
    /// l'application ouvre, et c'est la que le payeur choisit son operateur,
    /// saisit son numero et valide avec son code PIN.
    /// </summary>
    public string? RedirectUrl { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SucceededAt { get; private set; }

    /// <summary>
    /// Fin de validite de la page de paiement, a titre indicatif. ELLE NE SERT
    /// PAS A REFUSER UN PAIEMENT : si l'argent arrive apres, il est arrive. Ne
    /// pas l'encaisser creerait une livraison payee que personne ne livre.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsSettled => Status is PaymentStatus.Succeeded or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded;

    public static PaymentIntent Create(
        Guid id,
        ServiceType serviceType,
        Guid referenceId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        PaymentMethod method,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (referenceId == Guid.Empty)
        {
            throw new DomainException(
                PaymentErrorCodes.MissingReference, "Un paiement nomme la mission qu'il regle.");
        }

        // LA NATURE EST EXIGEE, PAS DEVINEE. Sans elle, l'encaissement partirait
        // vers un consommateur indetermine : Delivery et Ride ecoutent le meme
        // topic et ne se distinguent que par ce champ.
        if (serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                PaymentErrorCodes.MissingReference,
                "Un paiement dit de quelle nature est la mission : livraison ou course.");
        }

        if (string.IsNullOrWhiteSpace(payerId))
        {
            throw new DomainException(PaymentErrorCodes.MissingPayer, "Un paiement nomme celui qui paie.");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainException(PaymentErrorCodes.InvalidAmount, "Un paiement porte sur un montant strictement positif.");
        }

        return new PaymentIntent(
            id,
            serviceType,
            referenceId,
            payerId,
            payerPhone ?? string.Empty,
            amount,
            method,
            PaymentPurpose.Mission,
            null,
            createdAt,
            createdAt.Add(lifetime));
    }

    /// <summary>
    /// Ouvre le paiement d'un POURBOIRE, apres la mission (point 42).
    /// </summary>
    ///
    /// <remarks>
    /// UNE INTENTION A PART, ET PAS UN SUPPLEMENT SUR LA PREMIERE. Celle de la
    /// mission est close depuis longtemps quand celle-ci s'ouvre, et le
    /// fournisseur ne sait pas augmenter une transaction payee. C'est aussi ce
    /// qui rend le pourboire annulable sans toucher a la course.
    ///
    /// LE LIVREUR EST OBLIGATOIRE ICI. Un pourboire sans destinataire serait un
    /// encaissement sans contrepartie : l'argent entrerait et personne ne saurait
    /// a qui le rendre.
    ///
    /// AUCUNE BORNE SUR LE MONTANT, au-dela d'etre positif. Le plafond est une
    /// decision commerciale que personne n'a prise ; l'inventer ici la cacherait
    /// dans un agregat.
    /// </remarks>
    public static PaymentIntent CreateTip(
        Guid id,
        ServiceType serviceType,
        Guid referenceId,
        string payerId,
        string payerPhone,
        string driverId,
        MoneyXof amount,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (referenceId == Guid.Empty || serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                PaymentErrorCodes.MissingReference,
                "Un pourboire nomme la mission qu'il recompense.");
        }

        if (string.IsNullOrWhiteSpace(payerId))
        {
            throw new DomainException(PaymentErrorCodes.MissingPayer, "Un pourboire nomme celui qui paie.");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainException(
                PaymentErrorCodes.InvalidAmount,
                "Un pourboire porte sur un montant strictement positif.");
        }

        return new PaymentIntent(
            id,
            serviceType,
            referenceId,
            payerId,
            payerPhone ?? string.Empty,
            amount,

            // MOBILE MONEY, COMME LA MISSION. La carte existe au contrat et la
            // page hebergee la propose ; rien ne permet encore de la choisir
            // avant d'y arriver.
            PaymentMethod.MobileMoney,
            PaymentPurpose.Tip,
            driverId.Trim(),
            createdAt,
            createdAt.Add(lifetime));
    }

    /// <summary>
    /// Attache la transaction ouverte chez le fournisseur.
    ///
    /// UNE SEULE FOIS. Deux transactions pour une meme intention, c'est un
    /// client qui peut payer deux fois la meme course, et un rapprochement
    /// comptable qui ne tombe jamais juste.
    /// </summary>
    /// <summary>
    /// Rattache la transaction ouverte chez le fournisseur, et la page ou le
    /// payeur doit se rendre.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE S'EST APPELEE AttachProviderReference PENDANT UNE MATINEE, le
    /// 3 octobre 2026, quand la demande devait partir directement sur le
    /// telephone du payeur et qu'il n'y avait plus de page a ouvrir. Le
    /// fournisseur refuse cette poussee sur ce compte ; la page est revenue, et
    /// l'adresse avec elle.
    /// </remarks>
    public void AttachProviderCheckout(string providerReference, string redirectUrl)
    {
        if (ProviderReference is not null)
        {
            throw new DomainException(
                PaymentErrorCodes.CheckoutAlreadyAttached,
                "Cette intention porte deja une transaction chez le fournisseur.");
        }

        ProviderReference = providerReference;
        RedirectUrl = redirectUrl;
    }

    /// <summary>
    /// L'argent est arrive.
    ///
    /// IDEMPOTENT : le fournisseur rejoue son webhook jusqu'a neuf fois tant
    /// qu'il n'a pas recu de 2xx, et rien ne garantit qu'il ne le rejouera pas
    /// apres. Un deuxieme appel ne doit donc ni echouer, ni republier un
    /// evenement — sans quoi Delivery verrait deux paiements pour une course.
    ///
    /// UN ECHEC PEUT DEVENIR UN SUCCES, et ce n'est pas une anomalie : le
    /// client dont le premier essai est refuse recommence sur la meme page de
    /// paiement. Le chemin inverse, lui, est interdit plus bas.
    /// </summary>
    public void MarkSucceeded(Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == PaymentStatus.Succeeded)
        {
            return;
        }

        if (IsSettled)
        {
            throw new InvalidStateTransitionException(
                nameof(PaymentIntent),
                Status.ToString(),
                nameof(PaymentStatus.Succeeded),
                actor.Kind);
        }

        Status = PaymentStatus.Succeeded;
        SucceededAt = occurredAt;
        FailureReason = null;

        Raise(new PaymentIntentSucceeded(Id, ServiceType, ReferenceId, Amount, actor, occurredAt));
    }

    /// <summary>
    /// Le paiement a echoue.
    ///
    /// UNE INTENTION PAYEE NE REDEVIENT JAMAIS EN ECHEC. Les webhooks
    /// n'arrivent pas forcement dans l'ordre : un « declined » du premier essai
    /// peut se presenter apres l'« approved » du second. Laisser passer cette
    /// transition annulerait une course deja reglee.
    /// </summary>
    public void MarkFailed(string reason, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (IsSettled)
        {
            return;
        }

        if (Status == PaymentStatus.Failed)
        {
            return;
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;

        Raise(new PaymentIntentFailed(Id, ServiceType, ReferenceId, reason, actor, occurredAt));
    }

    /// <summary>
    /// L'argent a ete rendu. ON LE CONSTATE, ON NE LE DECLENCHE PAS.
    /// </summary>
    ///
    /// <remarks>
    /// IL N'Y A PAS DE COMMANDE DE REMBOURSEMENT, ET CE N'EST PAS UN OUBLI.
    /// FedaPay n'expose aucune API de remboursement — verifie le 30 septembre
    /// 2026 : tableau de bord uniquement, et MTN Mobile Money seulement. Un
    /// remboursement est donc un geste humain chez le fournisseur ; cette
    /// methode est ce qui permet au systeme de l'APPRENDRE, par la relecture de
    /// la transaction que le webhook declenche de toute facon.
    ///
    /// SEULE UNE INTENTION PAYEE SE REMBOURSE. Rembourser ce qui n'a jamais ete
    /// encaisse n'a pas de sens, et le statut du fournisseur ne devrait jamais
    /// le dire — mais si cela arrivait, on ne veut pas ecrire un etat que la
    /// suite du systeme lirait comme « le client a ete rembourse ».
    ///
    /// REJOUABLE : le webhook est rejoue jusqu'a neuf fois, et un deuxieme
    /// passage ne doit ni echouer ni republier le fait.
    /// </remarks>
    public void MarkRefunded(bool partiel, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var cible = partiel ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;

        if (Status == cible)
        {
            return;
        }

        if (Status != PaymentStatus.Succeeded
            && Status is not (PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidStateTransitionException(
                nameof(PaymentIntent),
                Status.ToString(),
                cible.ToString(),
                actor.Kind);
        }

        Status = cible;

        Raise(new PaymentIntentRefunded(Id, ServiceType, ReferenceId, Amount, partiel, actor, occurredAt));
    }
}
