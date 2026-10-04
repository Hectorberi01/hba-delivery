using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Domain.Refunds;

/// <summary>Où en est un remboursement à faire.</summary>
public enum RefundDueStatus
{
    Unspecified = 0,

    /// <summary>À traiter par la finance.</summary>
    Pending = 1,

    /// <summary>L'argent a été rendu, et la finance l'a noté ici.</summary>
    Settled = 2,

    /// <summary>
    /// Écarté : rien n'est dû, après examen.
    /// </summary>
    ///
    /// <remarks>
    /// IL FAUT CET ÉTAT, et pas seulement « fait ». Une course annulée après qu'un
    /// chauffeur s'est déplacé peut ne rien devoir au passager ; la supprimer de la
    /// file ferait disparaître la décision avec elle, et personne ne saurait plus
    /// qu'elle a été examinée.
    /// </remarks>
    Dismissed = 3,
}

/// <summary>
/// Un remboursement à faire à la main, parce que le fournisseur n'en fait pas.
/// </summary>
///
/// <remarks>
/// POURQUOI CET AGRÉGAT EXISTE. Le prix est encaissé AVANT la recherche d'un
/// chauffeur (point 35), et FedaPay N'A PAS D'API DE REMBOURSEMENT — vérifié le
/// 30 septembre 2026 : tableau de bord marchand uniquement, et MTN Mobile Money
/// seulement. Quand aucun chauffeur ne répond, le passager a donc payé pour rien et
/// quelqu'un doit lui rendre son argent à la main.
///
/// LE CAS SERA BEAUCOUP PLUS FRÉQUENT POUR LES COURSES QUE POUR LES COLIS : un
/// passager à deux heures du matin dans un quartier calme. Sans cette file, un
/// remboursement oublié n'était détectable par personne — ni par le passager, qui
/// lisait « HBA revient vers vous », ni par la finance, qui aurait dû relire toutes
/// les transactions du fournisseur une par une.
///
/// CE N'EST PAS UN REMBOURSEMENT, C'EST UNE TÂCHE. L'argent part du tableau de bord
/// FedaPay, par un geste humain ; cet agrégat note qu'il doit partir, puis qu'il
/// est parti. Le fait <c>PaymentRefunded</c> reste constaté à la relecture de la
/// transaction, et il ne naît pas d'ici.
///
/// IL NE DÉCIDE AUCUNE POLITIQUE D'ANNULATION. Voir
/// <see cref="RecommendedRefund"/> : quand le montant dû dépend d'une règle non
/// tranchée (point 3), cet agrégat le dit au lieu de choisir.
/// </remarks>
public sealed class RefundDue : AggregateRoot
{
    private RefundDue()
    {
    }

    private RefundDue(
        Guid id,
        ServiceType serviceType,
        Guid referenceId,
        Guid paymentIntentId,
        string payerId,
        long amountCollected,
        long? recommendedRefund,
        string reason,
        DateTimeOffset createdAt)
        : base(id)
    {
        ServiceType = serviceType;
        ReferenceId = referenceId;
        PaymentIntentId = paymentIntentId;
        PayerId = payerId;
        AmountCollected = amountCollected;
        RecommendedRefund = recommendedRefund;
        Reason = reason;
        Status = RefundDueStatus.Pending;
        CreatedAt = createdAt;
    }

    public ServiceType ServiceType { get; private set; }

    public Guid ReferenceId { get; private set; }

    /// <summary>
    /// L'intention payée.
    ///
    /// C'EST ELLE QUI PORTE L'UNICITÉ : une mission encaissée une fois ne doit
    /// produire qu'une ligne de file, et un index unique en base le garantit même si
    /// deux consommateurs concluaient en même temps qu'elle manque.
    /// </summary>
    public Guid PaymentIntentId { get; private set; }

    /// <summary>Le compte à rembourser. Recopié : la file se lit sans jointure.</summary>
    public string PayerId { get; private set; } = string.Empty;

    /// <summary>Ce que le passager a payé, en francs CFA.</summary>
    public long AmountCollected { get; private set; }

    /// <summary>
    /// Ce qu'il faudrait rendre, si la règle le dit.
    /// </summary>
    ///
    /// <remarks>
    /// NUL VEUT DIRE « À DÉTERMINER », PAS « RIEN ». C'est le cas d'une mission
    /// annulée alors qu'un chauffeur s'était déjà déplacé : ce qui est dû dépend de
    /// la politique d'annulation, qui n'est PAS TRANCHÉE (point 3). Inventer un
    /// montant ici écrirait une règle que personne n'a décidée ; ne rien inscrire du
    /// tout ferait disparaître le cas. La file le montre donc à un humain, qui
    /// tranche au cas par cas — c'est exactement ce qu'une règle manquante exige.
    ///
    /// RENSEIGNÉ ET ÉGAL AU MONTANT ENCAISSÉ quand AUCUN service n'a été rendu :
    /// aucun chauffeur trouvé, ou annulation avant toute affectation. Là, aucune
    /// règle ne manque — rien n'a été fait, tout est dû.
    /// </remarks>
    public long? RecommendedRefund { get; private set; }

    /// <summary>Pourquoi un remboursement est dû, en termes métier.</summary>
    public string Reason { get; private set; } = string.Empty;

    public RefundDueStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Ce qui a réellement été rendu. Nul tant que rien ne l'est.</summary>
    public long? SettledAmount { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Qui a tranché. Jamais vide sur une ligne résolue : tout est audité.</summary>
    public string? ResolvedBy { get; private set; }

    /// <summary>
    /// Note de la finance : référence du virement, motif d'un écart, raison d'un
    /// classement sans suite.
    /// </summary>
    public string? Note { get; private set; }

    public bool EstOuvert => Status == RefundDueStatus.Pending;

    /// <summary>
    /// Inscrit un remboursement à faire.
    /// </summary>
    ///
    /// <remarks>
    /// <paramref name="recommendedRefund"/> EST NUL QUAND LA RÈGLE MANQUE, et c'est
    /// la seule façon honnête de traiter le point 3. Voir
    /// <see cref="RecommendedRefund"/>.
    /// </remarks>
    public static RefundDue Open(
        Guid id,
        ServiceType serviceType,
        Guid referenceId,
        Guid paymentIntentId,
        string payerId,
        long amountCollected,
        long? recommendedRefund,
        string reason,
        DateTimeOffset createdAt)
    {
        if (serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                "MISSING_REFERENCE",
                "Un remboursement dû dit de quelle nature est la mission.");
        }

        if (referenceId == Guid.Empty || paymentIntentId == Guid.Empty)
        {
            throw new DomainException(
                "MISSING_REFERENCE",
                "Un remboursement dû nomme la mission et l'intention de paiement.");
        }

        // LE MONTANT ENCAISSÉ EST STRICTEMENT POSITIF : il n'y a rien à rembourser
        // d'un paiement de zéro franc, et une ligne de file sans argent derrière
        // ferait perdre du temps à la finance.
        if (amountCollected <= 0)
        {
            throw new DomainException(
                "INVALID_AMOUNT",
                "Un remboursement dû porte sur un montant encaissé strictement positif.");
        }

        // ON NE PEUT PAS DEVOIR PLUS QU'ON A ENCAISSÉ. C'est le genre d'erreur qu'un
        // calcul de pénalité mal écrit produit sans bruit, et elle se découvrirait en
        // comptabilité.
        if (recommendedRefund is not null && (recommendedRefund < 0 || recommendedRefund > amountCollected))
        {
            throw new DomainException(
                "INVALID_AMOUNT",
                "Le remboursement recommandé est positif et ne dépasse pas le montant encaissé.");
        }

        return new RefundDue(
            id,
            serviceType,
            referenceId,
            paymentIntentId,
            payerId?.Trim() ?? string.Empty,
            amountCollected,
            recommendedRefund,
            string.IsNullOrWhiteSpace(reason) ? "UNSPECIFIED" : reason.Trim(),
            createdAt);
    }

    /// <summary>
    /// La finance a rendu l'argent.
    /// </summary>
    ///
    /// <remarks>
    /// LE MONTANT RENDU EST SAISI, PAS DÉDUIT. Il peut différer du recommandé — un
    /// remboursement partiel négocié, un arrondi du fournisseur — et c'est ce qui est
    /// réellement parti qui doit figurer au dossier, pas ce qu'on avait prévu.
    ///
    /// AUCUN APPEL AU FOURNISSEUR N'EST FAIT ICI, et il ne peut pas y en avoir :
    /// FedaPay n'expose pas de remboursement par API. Cette méthode enregistre un
    /// geste déjà accompli ailleurs — la confondre avec un ordre de paiement ferait
    /// croire que l'argent part d'ici.
    /// </remarks>
    public void Settle(long settledAmount, string resolvedBy, string? note, DateTimeOffset now)
    {
        if (!EstOuvert)
        {
            throw new DomainException(
                "REFUND_ALREADY_RESOLVED", "Ce remboursement a déjà été tranché.");
        }

        if (settledAmount <= 0 || settledAmount > AmountCollected)
        {
            throw new DomainException(
                "INVALID_AMOUNT",
                "Le montant rendu est strictement positif et ne dépasse pas le montant encaissé.");
        }

        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            throw new DomainException("MISSING_ACTOR", "Un remboursement tranché nomme qui l'a tranché.");
        }

        Status = RefundDueStatus.Settled;
        SettledAmount = settledAmount;
        ResolvedBy = resolvedBy.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolvedAt = now;
    }

    /// <summary>
    /// Rien n'est dû, après examen.
    /// </summary>
    ///
    /// <remarks>
    /// LE MOTIF EST OBLIGATOIRE ICI, alors qu'il est facultatif ailleurs dans ce
    /// dépôt. Classer sans suite un remboursement, c'est décider de garder l'argent
    /// d'un client : la décision doit porter un nom et une raison, sans quoi personne
    /// ne pourra la défendre six mois plus tard.
    /// </remarks>
    public void Dismiss(string resolvedBy, string note, DateTimeOffset now)
    {
        if (!EstOuvert)
        {
            throw new DomainException(
                "REFUND_ALREADY_RESOLVED", "Ce remboursement a déjà été tranché.");
        }

        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            throw new DomainException("MISSING_ACTOR", "Un remboursement tranché nomme qui l'a tranché.");
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            throw new DomainException(
                "MISSING_REASON",
                "Classer un remboursement sans suite demande une raison écrite.");
        }

        Status = RefundDueStatus.Dismissed;
        ResolvedBy = resolvedBy.Trim();
        Note = note.Trim();
        ResolvedAt = now;
    }
}
