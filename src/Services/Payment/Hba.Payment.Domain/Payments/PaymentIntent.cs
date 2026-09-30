using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.Exceptions;
using Hba.Payment.Domain.Payments.Events;
using Hba.Payment.Domain.ValueObjects;

namespace Hba.Payment.Domain.Payments;

/// <summary>
/// Intention de paiement pour une livraison.
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
        Guid deliveryId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        PaymentMethod method,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        DeliveryId = deliveryId;
        PayerId = payerId;
        PayerPhone = payerPhone;
        Amount = amount;
        Method = method;
        Status = PaymentStatus.Pending;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid DeliveryId { get; private set; }

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

    /// <summary>Adresse de la page de paiement. Rendue une fois, au client.</summary>
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
        Guid deliveryId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        PaymentMethod method,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (deliveryId == Guid.Empty)
        {
            throw new DomainException(PaymentErrorCodes.MissingDeliveryId, "Un paiement nomme la livraison qu'il regle.");
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
            deliveryId,
            payerId,
            payerPhone ?? string.Empty,
            amount,
            method,
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

        Raise(new PaymentIntentSucceeded(Id, DeliveryId, Amount, actor, occurredAt));
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

        Raise(new PaymentIntentFailed(Id, DeliveryId, reason, actor, occurredAt));
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

        Raise(new PaymentIntentRefunded(Id, DeliveryId, Amount, partiel, actor, occurredAt));
    }
}
