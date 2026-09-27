using Hba.BuildingBlocks.Domain;

namespace Hba.Payment.Domain.Earnings;

/// <summary>
/// Sens d'un mouvement au compte d'un livreur.
///
/// LE SIGNE N'EST PAS DANS LE MONTANT. Un montant negatif se glisse partout
/// — dans une somme, dans un affichage, dans une comparaison — et on ne
/// s'apercoit de l'erreur qu'au moment de payer quelqu'un. Le montant reste
/// donc toujours positif, et c'est ce champ qui dit s'il entre ou s'il sort.
/// </summary>
public enum LedgerDirection
{
    Unspecified = 0,

    /// <summary>Ce que HBA doit au livreur.</summary>
    Credit = 1,

    /// <summary>Ce que HBA lui a verse.</summary>
    Debit = 2,
}

/// <summary>
/// Nature d'un mouvement. Deux aujourd'hui ; la liste grandira avec les
/// regles, pas avant.
/// </summary>
public enum LedgerEntryKind
{
    Unspecified = 0,

    /// <summary>Remuneration d'une course livree.</summary>
    DeliveryEarning = 1,

    /// <summary>Versement effectue par la finance.</summary>
    Payout = 2,
}

/// <summary>
/// Une ligne du compte d'un livreur. IMMUABLE : un grand livre se corrige par
/// une ecriture de plus, jamais par une rature.
///
/// CE QUE CETTE LIGNE EST, ET CE QU'ELLE N'EST PAS. Elle enregistre ce que le
/// systeme SAIT devoir — la part du livreur figee par le devis, au moment ou
/// la course a ete livree. Elle n'est ni une promesse de date, ni un ordre de
/// virement, ni une piece comptable : rien ici ne fait bouger d'argent.
///
/// LE MONTANT EST CELUI DU DEVIS, PAS UN CALCUL D'ICI. « driver_earning » est
/// fige a la confirmation de la course et voyage avec l'evenement de remise.
/// Le recalculer au moment de la remise ferait dependre la paie d'une grille
/// tarifaire qui a pu changer entre-temps — et le livreur avait accepte sur
/// la foi du premier chiffre.
///
/// AUCUNE COMMISSION N'EST DEDUITE ICI, et c'est une decision, pas un oubli :
/// la part du livreur EST son net. La commission de HBA est l'ecart entre le
/// prix paye par le client et cette part ; elle est donc deja prise en amont,
/// dans le devis. Si cette regle change un jour, c'est le devis qu'il faudra
/// reprendre, pas ce fichier.
/// </summary>
public sealed class DriverLedgerEntry : AggregateRoot
{
    private DriverLedgerEntry()
    {
    }

    private DriverLedgerEntry(
        Guid id,
        string driverId,
        LedgerEntryKind kind,
        LedgerDirection direction,
        long amountXof,
        Guid? deliveryId,
        string deliveryReference,
        Guid? payoutId,
        DateTimeOffset occurredAt)
        : base(id)
    {
        DriverId = driverId;
        Kind = kind;
        Direction = direction;
        AmountXof = amountXof;
        DeliveryId = deliveryId;
        DeliveryReference = deliveryReference;
        PayoutId = payoutId;
        OccurredAt = occurredAt;
    }

    public string DriverId { get; private set; } = string.Empty;

    public LedgerEntryKind Kind { get; private set; }

    public LedgerDirection Direction { get; private set; }

    /// <summary>
    /// Montant en francs CFA, TOUJOURS POSITIF — le sens est porte par
    /// <see cref="Direction"/>.
    ///
    /// UN ENTIER NU PLUTOT QUE MoneyXof, ET C'EST DELIBERE. Le type monetaire
    /// est stocke par un convertisseur de valeur ; un « e.Amount.Amount »
    /// dans une requete LINQ ne se traduit alors pas en SQL, et la somme du
    /// compte echouerait a la premiere lecture. Le XOF est de toute facon la
    /// seule devise admise (ADR 0006), et le contrat porte deja « amount_xof ».
    /// </summary>
    public long AmountXof { get; private set; }

    /// <summary>La course a l'origine du credit. Nulle pour un versement.</summary>
    public Guid? DeliveryId { get; private set; }

    /// <summary>
    /// Reference lisible de la course, recopiee ici.
    ///
    /// LA DENORMALISATION EST VOULUE : Payment n'a pas le droit d'aller lire
    /// la base de Delivery, et un releve qui n'afficherait que des UUID serait
    /// illisible pour le livreur comme pour la finance. La reference ne change
    /// jamais, la recopier ne cree donc aucune divergence.
    /// </summary>
    public string DeliveryReference { get; private set; } = string.Empty;

    /// <summary>
    /// La demande de versement a l'origine du debit. Nulle pour un credit.
    ///
    /// CE LIEN N'EST PAS DECORATIF, IL EST LA GARANTIE QU'ON NE PAIE PAS DEUX
    /// FOIS. La machine a etats de la demande interdit deja un second passage
    /// par « verse » ; cet identifiant permet en plus a la BASE de l'interdire,
    /// par un index unique. Une ligne de debit sans origine serait aussi, au
    /// releve, un montant qui sort sans que rien ne dise pourquoi.
    /// </summary>
    public Guid? PayoutId { get; private set; }

    /// <summary>
    /// Instant du FAIT, pas de l'ecriture. Un rattrapage d'evenements ecrit
    /// aujourd'hui des lignes datees d'hier, et c'est la date d'hier qui doit
    /// figurer au releve.
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Le credit d'une course livree.</summary>
    public static DriverLedgerEntry ForDelivery(
        Guid id,
        string driverId,
        long amountXof,
        Guid deliveryId,
        string? deliveryReference,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (amountXof < 0)
        {
            throw new DomainException(
                "NEGATIVE_LEDGER_AMOUNT",
                "Un mouvement de compte porte un montant positif ; le sens est separe.");
        }

        return new DriverLedgerEntry(
            id,
            driverId.Trim(),
            LedgerEntryKind.DeliveryEarning,
            LedgerDirection.Credit,
            amountXof,
            deliveryId,
            deliveryReference?.Trim() ?? string.Empty,
            null,
            occurredAt);
    }

    /// <summary>
    /// Le debit d'un versement effectue.
    ///
    /// CETTE LIGNE NE S'ECRIT QUE QUAND LE VIREMENT A EU LIEU, jamais a
    /// l'approbation. « Approuve » veut dire que la finance est d'accord ;
    /// l'argent peut encore ne pas partir. Debiter a l'approbation ferait
    /// disparaitre du compte du livreur une somme qu'il n'a pas recue, et
    /// c'est exactement le genre d'ecart qu'un grand livre existe pour eviter.
    ///
    /// AUCUNE COURSE N'EST RATTACHEE : un versement solde un cumul, pas une
    /// course en particulier. Le champ de reference de course reste donc vide,
    /// et c'est <see cref="PayoutId"/> qui porte l'origine.
    /// </summary>
    public static DriverLedgerEntry ForPayout(
        Guid id,
        string driverId,
        long amountXof,
        Guid payoutId,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (amountXof <= 0)
        {
            throw new DomainException(
                "NEGATIVE_LEDGER_AMOUNT",
                "Un versement porte un montant strictement positif ; le sens est separe.");
        }

        return new DriverLedgerEntry(
            id,
            driverId.Trim(),
            LedgerEntryKind.Payout,
            LedgerDirection.Debit,
            amountXof,
            null,
            string.Empty,
            payoutId,
            occurredAt);
    }
}
