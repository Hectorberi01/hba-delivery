using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Tariffs;

/// <summary>
/// Grille tarifaire d'une zone et d'un type de véhicule, pour une période.
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
        VehicleType vehicleType,
        MoneyXof baseFare,
        MoneyXof perKilometer,
        MoneyXof perMinute,
        MoneyXof minimumFare,
        int surgeBasisPoints,
        int driverShareBasisPoints,
        DateTimeOffset validFrom)
        : base(id)
    {
        TariffVersion = tariffVersion;
        ZoneCode = zoneCode;
        VehicleType = vehicleType;
        BaseFare = baseFare;
        PerKilometer = perKilometer;
        PerMinute = perMinute;
        MinimumFare = minimumFare;
        SurgeBasisPoints = surgeBasisPoints;
        DriverShareBasisPoints = driverShareBasisPoints;
        ValidFrom = validFrom;
    }

    /// <summary>
    /// Version de la grille, reprise telle quelle dans le devis puis dans la
    /// livraison. ELLE NE PEUT PAS S'APPELER Version : AggregateRoot porte deja
    /// ce nom pour le jeton de concurrence xmin, et le masquer casserait le
    /// verrou optimiste.
    /// </summary>
    public string TariffVersion { get; private set; } = string.Empty;

    public string ZoneCode { get; private set; } = string.Empty;

    public VehicleType VehicleType { get; private set; }

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

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidUntil { get; private set; }

    public bool IsInForceAt(DateTimeOffset instant)
        => instant >= ValidFrom && (ValidUntil is null || instant < ValidUntil);

    public static Tariff Create(
        Guid id,
        string tariffVersion,
        string zoneCode,
        VehicleType vehicleType,
        MoneyXof baseFare,
        MoneyXof perKilometer,
        MoneyXof perMinute,
        MoneyXof minimumFare,
        int surgeBasisPoints,
        int driverShareBasisPoints,
        DateTimeOffset validFrom)
    {
        ArgumentNullException.ThrowIfNull(baseFare);
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
            vehicleType,
            baseFare,
            perKilometer,
            perMinute,
            minimumFare,
            surgeBasisPoints,
            driverShareBasisPoints,
            validFrom);
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
