using Hba.BuildingBlocks.Domain;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Distance et durée d'un trajet, telles que les renvoie le moteur de routage,
/// ET LA FAÇON DONT ELLES ONT ÉTÉ OBTENUES.
/// </summary>
///
/// <remarks>
/// Ni la distance ni la durée ne sont estimées par Pricing : une distance à vol
/// d'oiseau sous-facturerait toutes les courses qui contournent la lagune.
///
/// LE TROISIÈME COMPOSANT EST LÀ PARCE QUE L'ESTIMATION ARRIVE QUAND MÊME.
/// Quand le fournisseur d'itinéraires ne répond pas, la mesure retombe sur la
/// ligne droite — environ un tiers trop courte en ville — et le devis est
/// FERME : une mesure dégradée devient une facture fausse que rien ne distingue
/// d'une autre. <see cref="IsDegraded"/> est cette distinction, et elle est
/// persistée avec le devis (point 36, option B, tranché le 2 octobre 2026).
///
/// IL MARQUE, IL NE CORRIGE PAS. Aucun coefficient n'est appliqué : majorer de
/// 1,3 corrigerait la moyenne et fausserait chaque cas, sans que personne puisse
/// le savoir après coup. Marquer rend les devis concernés retrouvables, et c'est
/// ce qui permettra plus tard de chiffrer le vrai écart à Cotonou.
/// </remarks>
public sealed class RouteMeasurement : ValueObject
{
    private RouteMeasurement()
    {
    }

    private RouteMeasurement(int distanceMeters, int durationSeconds, bool isDegraded)
    {
        DistanceMeters = distanceMeters;
        DurationSeconds = durationSeconds;
        IsDegraded = isDegraded;
    }

    public int DistanceMeters { get; private set; }

    public int DurationSeconds { get; private set; }

    /// <summary>
    /// Vrai quand la distance vient d'un repli à vol d'oiseau et non du réseau
    /// routier. Elle est alors SOUS-ESTIMÉE, et le prix avec elle.
    /// </summary>
    public bool IsDegraded { get; private set; }

    /// <summary>
    /// LE DÉFAUT EST « MESURE ROUTIÈRE », ET C'EST VOULU. Un appelant qui oublie
    /// le drapeau produit un devis réputé fiable : c'est le cas le plus fréquent
    /// et le seul dont l'oubli ne salit pas les statistiques des autres. Les deux
    /// seuls chemins dégradés du service le passent explicitement.
    /// </summary>
    public static RouteMeasurement Create(
        int distanceMeters,
        int durationSeconds,
        bool isDegraded = false)
    {
        if (distanceMeters < 0 || durationSeconds < 0)
        {
            throw new DomainException("INVALID_ROUTE", "Distance et durée doivent être positives.");
        }

        return new RouteMeasurement(distanceMeters, durationSeconds, isDegraded);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DistanceMeters;
        yield return DurationSeconds;
        yield return IsDegraded;
    }
}
