using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.Exceptions;

namespace Hba.Payment.Domain.Payouts;

/// <summary>
/// Etat d'une demande de versement.
///
/// APPROUVEE ET VERSEE SONT DEUX CHOSES. « Approuvee » dit que la finance est
/// d'accord ; « versee » dit que l'argent est parti. Entre les deux il y a un
/// virement fait a la main, ailleurs, qui peut echouer ou attendre. Les
/// confondre ferait disparaitre du compte du livreur un argent qu'il n'a pas
/// recu.
/// </summary>
public enum PayoutStatus
{
    Unspecified = 0,

    /// <summary>Le livreur a demande. Rien n'est engage.</summary>
    Requested = 1,

    /// <summary>La finance est d'accord. L'argent n'est pas encore parti.</summary>
    Approved = 2,

    /// <summary>Le virement est fait, et sa reference est consignee.</summary>
    Paid = 3,

    /// <summary>Refusee, avec un motif que le livreur peut lire.</summary>
    Rejected = 4,
}

/// <summary>
/// Une demande de versement.
///
/// ELLE NE DEPLACE AUCUN ARGENT, ET C'EST TOUT CE QU'IL FAUT SAVOIR D'ELLE.
/// Le versement reel se fait ailleurs — un virement mobile money passe par un
/// prestataire sortant, qui n'est pas choisi. Ce qui vit ici est la trace de
/// la demande, de la decision, et de la reference du virement une fois qu'il
/// a eu lieu.
///
/// LE MONTANT EST FIGE A LA DEMANDE. Le livreur demande une somme, pas « tout
/// ce qui reste » : entre la demande et la decision, une course peut etre
/// livree, et le solde bouger. Figer le montant evite d'avoir a expliquer
/// pourquoi le versement ne correspond pas a ce qu'il avait sous les yeux.
///
/// UNE SEULE DEMANDE EN COURS PAR LIVREUR, verifiee par l'application ET par
/// un index unique : deux demandes simultanees videraient deux fois le meme
/// solde.
/// </summary>
public sealed class PayoutRequest : AggregateRoot
{
    private PayoutRequest()
    {
    }

    private PayoutRequest(Guid id, string driverId, long amountXof, DateTimeOffset requestedAt)
        : base(id)
    {
        DriverId = driverId;
        AmountXof = amountXof;
        Status = PayoutStatus.Requested;
        RequestedAt = requestedAt;
    }

    public string DriverId { get; private set; } = string.Empty;

    /// <summary>Montant demande, en francs CFA. Toujours strictement positif.</summary>
    public long AmountXof { get; private set; }

    public PayoutStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Instant de la decision — approbation ou refus.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Qui a decide. Un identifiant du back-office, jamais un nom.</summary>
    public string? DecidedBy { get; private set; }

    /// <summary>
    /// Motif du refus. OBLIGATOIRE : un refus sans motif laisse le livreur
    /// sans rien a corriger, et il redemandera le lendemain.
    /// </summary>
    public string? RejectionReason { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>
    /// Reference du virement chez l'operateur.
    ///
    /// OBLIGATOIRE ELLE AUSSI. « Verse » sans reference est une affirmation
    /// que rien ne soutient : le jour ou le livreur dit n'avoir rien recu, il
    /// faut pouvoir montrer quelque chose.
    /// </summary>
    public string? PaymentReference { get; private set; }

    /// <summary>La demande attend encore une decision ou un virement.</summary>
    public bool IsPending => Status is PayoutStatus.Requested or PayoutStatus.Approved;

    public static PayoutRequest Open(Guid id, string driverId, long amountXof, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (amountXof <= 0)
        {
            throw new DomainException(
                PaymentErrorCodes.InvalidAmount,
                "Une demande de versement porte sur un montant strictement positif.");
        }

        return new PayoutRequest(id, driverId.Trim(), amountXof, requestedAt);
    }

    public void Approve(string decidedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        EnsureIs(PayoutStatus.Requested, PayoutStatus.Approved);

        Status = PayoutStatus.Approved;
        DecidedAt = now;
        DecidedBy = decidedBy;
    }

    public void Reject(string reason, string decidedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                PaymentErrorCodes.MissingRejectionReason,
                "Un refus de versement doit etre motive : le livreur doit savoir quoi corriger.");
        }

        EnsureIs(PayoutStatus.Requested, PayoutStatus.Rejected);

        Status = PayoutStatus.Rejected;
        DecidedAt = now;
        DecidedBy = decidedBy;
        RejectionReason = reason.Trim();
    }

    /// <summary>
    /// Le virement a eu lieu. C'EST CETTE METHODE, ET ELLE SEULE, qui autorise
    /// l'appelant a ecrire le debit correspondant au grand livre : tant qu'elle
    /// n'a pas ete appelee, l'argent n'est pas parti.
    /// </summary>
    public void MarkPaid(string paymentReference, string decidedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new DomainException(
                PaymentErrorCodes.MissingPaymentReference,
                "Un versement se consigne avec la reference du virement.");
        }

        EnsureIs(PayoutStatus.Approved, PayoutStatus.Paid);

        Status = PayoutStatus.Paid;
        PaidAt = now;
        PaymentReference = paymentReference.Trim();

        // DecidedBy N'EST PAS ECRASE : celui qui approuve et celui qui verse
        // peuvent etre deux personnes, et c'est l'approbation qui engage.
        DecidedBy ??= decidedBy;
    }

    private void EnsureIs(PayoutStatus attendu, PayoutStatus vise)
    {
        if (Status != attendu)
        {
            throw new InvalidStateTransitionException(
                nameof(PayoutRequest),
                Status.ToString(),
                vise.ToString(),
                ActorKind.Admin);
        }
    }
}
