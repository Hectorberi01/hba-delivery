using Hba.BuildingBlocks.Domain;
using Hba.Payment.Domain.Payments;

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

    /// <summary>
    /// Dedommagement d'une mission ENGAGEE MAIS NON ACCOMPLIE : le client a
    /// annule apres l'affectation, ou la remise s'est soldee par un echec.
    ///
    /// C'EST UN GENRE A PART, PAS UN DeliveryEarning AU RABAIS. Un gain
    /// remunere un service rendu ; celui-ci paie un deplacement qui n'a rien
    /// livre. Les confondre rendrait impossible de repondre a la seule
    /// question qui compte au bout de quelques semaines : combien coute ce
    /// qui n'aboutit pas.
    /// </summary>
    Compensation = 3,

    /// <summary>
    /// Pourboire laisse par le client, APRES la mission (point 42, tranche le
    /// 3 octobre 2026).
    ///
    /// UN GENRE A PART, ET C'EST TOUTE LA DECISION. Le ranger dans
    /// DeliveryEarning rendrait impossible de repondre a « combien les livreurs
    /// touchent-ils EN PLUS de la grille », qui est exactement la question qu'on
    /// se posera — et la premiere que posera un livreur.
    ///
    /// COMMISSION ZERO : LE LIVREUR TOUCHE TOUT. C'est la seule ecriture du
    /// systeme, avec le dedommagement, ou HBA ne preleve rien. En prelever sur
    /// un don se sait vite et se raconte mal.
    /// </summary>
    Tip = 4,
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
        ServiceType serviceType,
        Guid? referenceId,
        string referenceLabel,
        Guid? payoutId,
        DateTimeOffset occurredAt)
        : base(id)
    {
        DriverId = driverId;
        Kind = kind;
        Direction = direction;
        AmountXof = amountXof;
        ServiceType = serviceType;
        ReferenceId = referenceId;
        ReferenceLabel = referenceLabel;
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

    /// <summary>
    /// La nature de la mission remuneree. Non precisee pour un versement.
    ///
    /// SANS ELLE, LES GAINS DE COURSE N'APPARAISSAIENT NULLE PART : le grand livre
    /// ne connaissait que la livraison, donc un chauffeur enchainait les courses de
    /// personnes et son releve restait vide — il ne pouvait rien retirer.
    /// </summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>La mission a l'origine du credit. Nulle pour un versement.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>
    /// Reference lisible de la mission, recopiee ici.
    ///
    /// LA DENORMALISATION EST VOULUE : Payment n'a pas le droit d'aller lire la
    /// base de Delivery ni celle de Ride, et un releve qui n'afficherait que des
    /// UUID serait illisible pour le chauffeur comme pour la finance. La reference
    /// ne change jamais, la recopier ne cree donc aucune divergence.
    ///
    /// UNE COURSE DE PERSONNES N'A PAS DE REFERENCE LISIBLE aujourd'hui — Ride
    /// n'en attribue pas, contrairement a Delivery. Le champ recoit alors
    /// l'identifiant, ce qui est moins lisible mais vrai ; le jour ou Ride
    /// numerotera ses courses, il suffira de le remplir.
    /// </summary>
    public string ReferenceLabel { get; private set; } = string.Empty;

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

    /// <summary>
    /// Le credit d'une mission accomplie : un colis livre, ou une course terminee.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE S'APPELAIT ForDelivery, ET LE NOM ETAIT DEVENU FAUX. Le grand livre ne
    /// connaissait que la livraison : un chauffeur enchainait les courses de
    /// personnes et son releve restait vide, donc il ne pouvait rien retirer.
    ///
    /// LA NATURE EST EXIGEE. Sans elle, deux missions au meme identifiant
    /// partageraient la ligne qui empeche de crediter deux fois — et l'une des deux
    /// ne serait jamais payee.
    /// </remarks>
    public static DriverLedgerEntry ForMission(
        Guid id,
        string driverId,
        long amountXof,
        ServiceType serviceType,
        Guid referenceId,
        string? referenceLabel,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (amountXof < 0)
        {
            throw new DomainException(
                "NEGATIVE_LEDGER_AMOUNT",
                "Un mouvement de compte porte un montant positif ; le sens est separe.");
        }

        if (serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                "MISSING_REFERENCE",
                "Une remuneration dit de quelle nature est la mission : livraison ou course.");
        }

        return new DriverLedgerEntry(
            id,
            driverId.Trim(),
            LedgerEntryKind.DeliveryEarning,
            LedgerDirection.Credit,
            amountXof,
            serviceType,
            referenceId,
            referenceLabel?.Trim() ?? string.Empty,
            null,
            occurredAt);
    }

    /// <summary>
    /// Le pourboire laisse par le client apres la mission.
    /// </summary>
    ///
    /// <remarks>
    /// LE MONTANT EST VERSE EN ENTIER. Point 42 : HBA ne preleve rien sur un
    /// pourboire, et assume les frais de l'agregateur sur cette transaction.
    /// C'est le vrai cout de la decision, et il est a sa charge.
    ///
    /// LA MISSION EST NOMMEE, MAIS LA CLE N'EST PAS LA SIENNE. Deux lignes
    /// peuvent exister pour une meme course : la remuneration et le pourboire.
    /// C'est le GENRE qui les separe dans FindMissionEntryAsync — exactement la
    /// raison pour laquelle il y est entre le 2 octobre.
    ///
    /// UN MONTANT NUL EST REFUSE. Un pourboire de zero franc n'est pas un
    /// pourboire : c'est un client qui a renonce, et il ne doit rien laisser au
    /// releve.
    /// </remarks>
    public static DriverLedgerEntry ForTip(
        Guid id,
        string driverId,
        long amountXof,
        ServiceType serviceType,
        Guid referenceId,
        string? referenceLabel,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (amountXof <= 0)
        {
            throw new DomainException(
                "INVALID_TIP_AMOUNT",
                "Un pourboire porte sur un montant strictement positif.");
        }

        if (serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                "MISSING_REFERENCE",
                "Un pourboire dit de quelle nature est la mission : livraison ou course.");
        }

        return new DriverLedgerEntry(
            id,
            driverId.Trim(),
            LedgerEntryKind.Tip,
            LedgerDirection.Credit,
            amountXof,
            serviceType,
            referenceId,
            referenceLabel?.Trim() ?? string.Empty,
            null,
            occurredAt);
    }

    /// <summary>
    /// Le dedommagement d'une mission engagee et non accomplie.
    /// </summary>
    ///
    /// <remarks>
    /// DEUX SITUATIONS, UNE SEULE REGLE, ET C'EST VOULU (points 3 et 34, tranches
    /// le 2 octobre 2026). Le client annule apres qu'un livreur a accepte, ou le
    /// livreur declare un incident et la mission part en echec : dans les deux cas
    /// il s'est deplace, dans les deux cas il touche la meme chose.
    ///
    /// LE MONTANT VIENT DU DEVIS, PAS D'UN CALCUL D'ICI. La retenue d'annulation
    /// est figee dans le devis au taux de la grille du jour, exactement comme la
    /// part du livreur. Ce service la recopie ; il ne la recalcule pas, et n'a
    /// d'ailleurs aucune grille a lire.
    ///
    /// LA PART DU LIVREUR NE S'Y APPLIQUE PAS : la retenue lui revient EN ENTIER.
    /// HBA ne preleve rien sur une mission qui n'a pas eu lieu. C'est la seule
    /// ecriture du systeme dans ce cas.
    ///
    /// UN MONTANT NUL EST REFUSE, ET C'EST LE POINT DE LA REGLE. Une grille sans
    /// retenue rend l'annulation gratuite : il n'y a alors rien a dedommager, donc
    /// aucune ligne a ecrire. Une ligne a zero au releve d'un livreur se lirait
    /// comme une erreur de calcul — c'est a l'appelant de ne pas appeler.
    /// </remarks>
    public static DriverLedgerEntry ForCompensation(
        Guid id,
        string driverId,
        long amountXof,
        ServiceType serviceType,
        Guid referenceId,
        string? referenceLabel,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (amountXof <= 0)
        {
            throw new DomainException(
                "NEGATIVE_LEDGER_AMOUNT",
                "Un dedommagement porte un montant strictement positif : sans retenue, il n'y a pas de ligne.");
        }

        if (serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                "MISSING_REFERENCE",
                "Un dedommagement dit de quelle nature est la mission : livraison ou course.");
        }

        return new DriverLedgerEntry(
            id,
            driverId.Trim(),
            LedgerEntryKind.Compensation,
            LedgerDirection.Credit,
            amountXof,
            serviceType,
            referenceId,
            referenceLabel?.Trim() ?? string.Empty,
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
    /// AUCUNE MISSION N'EST RATTACHEE : un versement solde un cumul, pas une
    /// mission en particulier. Le champ de reference reste donc vide, sa nature
    /// non precisee, et c'est <see cref="PayoutId"/> qui porte l'origine.
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
            ServiceType.Unspecified,
            null,
            string.Empty,
            payoutId,
            occurredAt);
    }
}
