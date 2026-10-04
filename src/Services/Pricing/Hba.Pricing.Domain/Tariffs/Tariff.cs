using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Tariffs;

/// <summary>
/// Grille tarifaire d'une zone, d'une NATURE de prestation, d'un type de véhicule
/// et d'une PLAGE HORAIRE, pour une période.
///
/// LA CLÉ A QUATRE DIMENSIONS DEPUIS LE 1er OCTOBRE 2026, et deux sont neuves.
/// Avant, une course de personnes était facturée sur la grille du colis — même
/// tarif au kilomètre, même plancher — et il n'y avait aucun tarif de nuit.
///
/// LA STRUCTURE VIENT DU CONTRAT : hba.pricing.v1.Quote impose base_fare,
/// distance_fare, surge_fare et driver_earning. LES MONTANTS, EUX, NE SONT PAS
/// DANS LE REFERENTIEL. Ils sont donnés en configuration et administrés
/// ensuite par ops et admin ; aucune valeur n'est codée en dur ici.
///
/// Une grille n'est jamais modifiée : on en crée une nouvelle version et on
/// clôt la précédente. C'est ce qui rend un devis relisible des mois plus tard,
/// et ce qui garantit qu'une modification de tarif ne change jamais une
/// livraison déjà confirmée.
/// </summary>
public sealed class Tariff : AggregateRoot
{
    private Tariff()
    {
    }

    private Tariff(
        Guid id,
        string tariffVersion,
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        TimeBand band,
        MoneyXof baseFare,
        MoneyXof perKilometer,
        MoneyXof perMinute,
        MoneyXof minimumFare,
        int surgeBasisPoints,
        int driverShareBasisPoints,
        DateTimeOffset validFrom,
        MoneyXof perKilogram,
        int includedWeightGrams,
        int cancellationFeeBasisPoints)
        : base(id)
    {
        TariffVersion = tariffVersion;
        ZoneCode = zoneCode;
        ServiceType = serviceType;
        VehicleType = vehicleType;
        Band = band;
        BaseFare = baseFare;
        PerKilometer = perKilometer;
        PerMinute = perMinute;
        MinimumFare = minimumFare;
        SurgeBasisPoints = surgeBasisPoints;
        DriverShareBasisPoints = driverShareBasisPoints;
        CancellationFeeBasisPoints = cancellationFeeBasisPoints;
        ValidFrom = validFrom;
        PerKilogram = perKilogram;
        IncludedWeightGrams = includedWeightGrams;
    }

    /// <summary>
    /// Version de la grille, reprise telle quelle dans le devis puis dans la
    /// livraison. ELLE NE PEUT PAS S'APPELER Version : AggregateRoot porte deja
    /// ce nom pour le jeton de concurrence xmin, et le masquer casserait le
    /// verrou optimiste.
    /// </summary>
    public string TariffVersion { get; private set; } = string.Empty;

    public string ZoneCode { get; private set; } = string.Empty;

    /// <summary>Colis ou course de personnes. Jamais « non précisée » en base.</summary>
    public ServiceType ServiceType { get; private set; }

    public VehicleType VehicleType { get; private set; }

    /// <summary>
    /// Plage horaire d'application, en heure locale.
    ///
    /// LA JOURNÉE ENTIÈRE EST UNE PLAGE COMME LES AUTRES : 0 → 1440. C'est la valeur
    /// de toutes les grilles antérieures, posée par migration.
    /// </summary>
    public TimeBand Band { get; private set; } = null!;

    /// <summary>Part fixe, due quelle que soit la distance.</summary>
    public MoneyXof BaseFare { get; private set; } = null!;

    public MoneyXof PerKilometer { get; private set; } = null!;

    /// <summary>
    /// Part liée au temps. À Cotonou, deux kilomètres aux heures de pointe ne
    /// coûtent pas le même carburant ni le même temps que deux kilomètres le
    /// dimanche ; c'est la durée qui le traduit.
    /// </summary>
    public MoneyXof PerMinute { get; private set; } = null!;

    /// <summary>Plancher : une course très courte reste rentable pour le livreur.</summary>
    public MoneyXof MinimumFare { get; private set; } = null!;

    /// <summary>
    /// Majoration en points de base appliquée au sous-total : 10000 vaut 1,00,
    /// c'est-à-dire aucune majoration. LES REGLES DE DECLENCHEMENT D'UNE
    /// MAJORATION NE SONT PAS TRANCHEES ; la valeur reste portée par la grille
    /// et vaut 10000 tant que rien n'est décidé.
    /// </summary>
    public int SurgeBasisPoints { get; private set; }

    /// <summary>Part du total revenant au livreur, en points de base.</summary>
    public int DriverShareBasisPoints { get; private set; }

    /// <summary>
    /// Retenue prélevée quand le client annule APRÈS qu'un livreur a été affecté,
    /// en points de base du prix du devis : 2000 vaut 20 %. Zéro veut dire
    /// annulation gratuite à tout moment, et c'est le défaut.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE VIT SUR LA GRILLE, ET NON EN CONFIGURATION. Trois raisons, tranchées
    /// le 2 octobre 2026 : elle se règle par zone, nature et véhicule depuis la
    /// console ; elle suit le versionnage des grilles (ADR 0004), donc un devis
    /// émis hier garde le taux de SA grille au lieu d'être recalculé avec celui
    /// d'aujourd'hui ; et elle est lisible avant que le client ne confirme, parce
    /// que la livraison porte le devis consommé.
    ///
    /// ELLE VA INTÉGRALEMENT AU LIVREUR. <see cref="DriverShareBasisPoints"/> ne
    /// s'y applique PAS : HBA ne prélève rien sur une course qui n'a pas eu lieu,
    /// et la retenue couvre exactement ce qu'elle prétend couvrir — le
    /// déplacement engagé. C'est la seule écriture du système où la part du
    /// livreur vaut 100 %.
    ///
    /// LA MÊME RETENUE DÉDOMMAGE UNE COURSE TERMINÉE EN ÉCHEC (point 34). Tant
    /// qu'elle reste une FRACTION du devis, abandonner paie strictement moins que
    /// livrer, et c'est ce qui tient la règle. Un taux porté près de 10000
    /// inverserait l'incitation : à relire avant de monter celui d'une zone.
    /// </remarks>
    public int CancellationFeeBasisPoints { get; private set; }

    /// <summary>
    /// Tarif au kilogramme au-delà de <see cref="IncludedWeightGrams"/>. Zéro pour
    /// une course de personnes, et l'agrégat le refuse autrement.
    /// </summary>
    ///
    /// <remarks>
    /// LE POIDS ÉTAIT COLLECTÉ ET JETÉ. Il traversait l'application cliente, la
    /// passerelle et le contrat gRPC, et la frontière de ce service ne le passait pas
    /// à la commande : vingt kilos coûtaient le prix de cinq cents grammes. Cette
    /// colonne est ce qui lui donne enfin un effet.
    /// </remarks>
    public MoneyXof PerKilogram { get; private set; } = null!;

    /// <summary>
    /// Poids inclus, en grammes. Au-delà, chaque kilo entamé est dû.
    ///
    /// CE N'EST PAS UNE FRANCHISE COMMERCIALE : c'est ce qu'une moto porte sans y
    /// penser. Au-delà, le transport change de nature.
    /// </summary>
    public int IncludedWeightGrams { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidUntil { get; private set; }

    public bool IsInForceAt(DateTimeOffset instant)
        => instant >= ValidFrom && (ValidUntil is null || instant < ValidUntil);

    /// <summary>
    /// La grille s'applique-t-elle à cette heure locale ?
    /// </summary>
    ///
    /// <remarks>
    /// DEUX QUESTIONS DISTINCTES, ET IL FAUT LES DEUX. <see cref="IsInForceAt"/> dit
    /// si la VERSION est en vigueur — une grille close en mars ne sert plus en avril.
    /// Celle-ci dit si l'HEURE tombe dans sa plage — la grille de nuit est en vigueur
    /// toute l'année et ne s'applique pas à midi. Les confondre ferait facturer la
    /// nuit en plein jour.
    /// </remarks>
    public bool AppliesAt(TimeOnly heureLocale) => Band.Contient(heureLocale);

    public static Tariff Create(
        Guid id,
        string tariffVersion,
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        TimeBand band,
        MoneyXof baseFare,
        MoneyXof perKilometer,
        MoneyXof perMinute,
        MoneyXof minimumFare,
        int surgeBasisPoints,
        int driverShareBasisPoints,
        DateTimeOffset validFrom,
        MoneyXof? perKilogram = null,
        int includedWeightGrams = 0,

        // LE DÉFAUT EST LA GRATUITÉ, ET C'EST LE SEUL DÉFAUT SÛR. Une grille
        // saisie sans taux ne retient rien : au pire le client annule sans frais,
        // ce qui n'enlève d'argent à personne. Le défaut inverse ferait prélever
        // une somme que personne n'a décidée.
        int cancellationFeeBasisPoints = 0)
    {
        ArgumentNullException.ThrowIfNull(baseFare);
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(perKilometer);
        ArgumentNullException.ThrowIfNull(perMinute);
        ArgumentNullException.ThrowIfNull(minimumFare);

        if (string.IsNullOrWhiteSpace(tariffVersion))
        {
            throw new DomainException("MISSING_TARIFF_VERSION", "Une grille doit porter une version.");
        }

        if (string.IsNullOrWhiteSpace(zoneCode))
        {
            throw new DomainException("MISSING_ZONE_CODE", "Une grille est rattachée à une zone.");
        }

        // UNE NATURE NON PRÉCISÉE EST REFUSÉE, PAS INTERPRÉTÉE COMME UN COLIS. Une
        // grille sans nature serait choisie pour une course aussi bien que pour un
        // paquet, et le prix d'une course de personnes dépendrait de l'ordre de
        // lecture en base.
        if (serviceType == ServiceType.Unspecified)
        {
            throw new DomainException(
                "INVALID_TARIFF",
                "Une grille dit de quelle nature est la prestation : colis ou course.");
        }

        var auKilo = perKilogram ?? MoneyXof.Zero;

        if (includedWeightGrams < 0)
        {
            throw new DomainException("INVALID_TARIFF", "Le poids inclus ne peut pas être négatif.");
        }

        // UNE COURSE DE PERSONNES N'A PAS DE TARIF AU KILO, et le refuser ici plutôt
        // que de l'ignorer en silence a une raison : une grille de course saisie avec
        // un tarif au kilo serait une grille dont une colonne ne sert à rien, et
        // personne ne saurait si c'est voulu ou si le calcul l'oublie. Un passager n'a
        // pas de poids déclaré ; s'il en avait un, ce serait une autre décision.
        if (serviceType == ServiceType.Ride && (auKilo.Amount > 0 || includedWeightGrams > 0))
        {
            throw new DomainException(
                "INVALID_TARIFF",
                "Une grille de course de personnes ne porte ni tarif au kilo ni poids inclus.");
        }

        if (minimumFare.Amount <= 0)
        {
            throw new DomainException("INVALID_TARIFF", "Le prix plancher doit être strictement positif.");
        }

        if (surgeBasisPoints < 10_000)
        {
            throw new DomainException(
                "INVALID_TARIFF",
                "Une majoration ne peut pas réduire le prix : le plancher est 10000, soit 1,00.");
        }

        if (cancellationFeeBasisPoints is < 0 or > 10_000)
        {
            throw new DomainException(
                "INVALID_TARIFF",
                "La retenue d'annulation va de 0 à 10000 points de base, soit de 0 % à 100 % du devis.");
        }

        if (driverShareBasisPoints is <= 0 or > 10_000)
        {
            throw new DomainException(
                "INVALID_TARIFF",
                "La part du livreur doit être strictement positive et ne peut pas dépasser le total.");
        }

        return new Tariff(
            id,
            tariffVersion.Trim(),
            zoneCode.Trim().ToLowerInvariant(),
            serviceType,
            vehicleType,
            band,
            baseFare,
            perKilometer,
            perMinute,
            minimumFare,
            surgeBasisPoints,
            driverShareBasisPoints,
            validFrom,
            auKilo,
            includedWeightGrams,
            cancellationFeeBasisPoints);
    }

    /// <summary>Clôt la grille. Les devis déjà émis gardent leur version.</summary>
    public void Close(DateTimeOffset at)
    {
        if (at < ValidFrom)
        {
            throw new DomainException("INVALID_TARIFF", "Une grille ne peut pas être close avant d'entrer en vigueur.");
        }

        ValidUntil = at;
    }
}
