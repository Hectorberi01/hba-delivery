using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Calcul d'un prix à partir d'une grille et d'un trajet mesuré. Fonction pure :
/// mêmes entrées, même sortie, testable sans base ni réseau.
/// </summary>
public static class QuoteCalculator
{
    /// <summary>
    /// Le prix d'un trajet mesuré, pour une grille et une nature de prestation.
    /// </summary>
    ///
    /// <remarks>
    /// LA STRATÉGIE N'APPORTE QU'UN SUPPLÉMENT, et tout le reste est commun. Base,
    /// distance, durée, plancher, majoration et part du chauffeur valent pour un colis
    /// comme pour un passager : les dupliquer dans deux stratégies aurait créé deux
    /// copies du passage délicat — l'absorption du plancher par la part fixe, l'ordre
    /// de l'arrondi — qui auraient divergé au premier correctif.
    ///
    /// LA STRATÉGIE SE DÉDUIT DE LA GRILLE, elle n'est pas passée. Une grille porte sa
    /// nature ; laisser l'appelant choisir la stratégie permettrait de calculer une
    /// grille de course avec la formule du colis, ce qui produirait un prix plausible
    /// et faux.
    /// </remarks>
    public static QuoteAmounts Compute(Tariff tariff, RouteMeasurement route, QuoteInputs? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(route);

        var supplement = PricingStrategies
            .For(tariff.ServiceType)
            .Supplement(tariff, inputs ?? new QuoteInputs());

        // Part variable : distance et durée. Le contrat hba.pricing.v1.Quote
        // n'offre que trois compartiments, donc la durée est comptée avec la
        // distance. Les séparer demanderait un champ de plus au contrat.
        var distancePart = tariff.PerKilometer.Amount * route.DistanceMeters / 1_000;
        var durationPart = tariff.PerMinute.Amount * route.DurationSeconds / 60;
        // LE SUPPLÉMENT ENTRE DANS LA PART VARIABLE, DONC AVANT LE PLANCHER ET LA
        // MAJORATION. C'est voulu : un colis lourd sur un trajet très court doit sortir
        // du plancher, et sa majoration doit porter sur le prix réel du transport et
        // non sur un sous-total qui ignorerait son poids.
        var variable = MoneyXof.FromNonNegative(distancePart + durationPart + supplement.Amount);

        var baseFare = tariff.BaseFare;

        // Le plancher est absorbé par la part fixe, pour que les compartiments
        // continuent de faire le total. Si la part variable dépasse déjà le
        // plancher, rien ne change.
        var beforeSurge = baseFare.Add(variable);
        if (beforeSurge.CompareTo(tariff.MinimumFare) < 0)
        {
            baseFare = MoneyXof.FromNonNegative(tariff.MinimumFare.Amount - variable.Amount);
            beforeSurge = tariff.MinimumFare;
        }

        // Majoration : 10000 points de base signifie aucune majoration.
        var surge = MoneyXof.FromNonNegative(
            beforeSurge.Amount * (tariff.SurgeBasisPoints - 10_000) / 10_000);

        var total = baseFare.Add(variable).Add(surge).RoundUpToTen();

        // L'arrondi retombe lui aussi sur la part fixe, même raison.
        baseFare = MoneyXof.FromNonNegative(total.Amount - variable.Amount - surge.Amount);

        // Arrondi vers le bas : la part du livreur ne doit jamais dépasser le
        // total, et PricingSnapshot côté Delivery refuse le cas.
        var driverEarning = total.ApplyBasisPoints(tariff.DriverShareBasisPoints).RoundDownToTen();

        // LA RETENUE EST CALCULÉE ICI ET FIGÉE DANS LE DEVIS, PAS RELUE À
        // L'ANNULATION. Un devis est un instantané (ADR 0004) : relire la grille
        // au moment où le client annule ferait dépendre la retenue d'un taux
        // changé entre-temps, et le montant annoncé à l'écran ne serait plus
        // celui prélevé. C'est aussi ce qui la rend affichable AVANT la
        // confirmation, sans rien demander à Pricing.
        //
        // ARRONDI VERS LE BAS, COMME LA PART DU LIVREUR. Il penche du côté du
        // client, et il garantit que la retenue ne dépasse jamais le total —
        // ApplyBasisPoints le garantit déjà pour un taux ≤ 10000, l'arrondi ne
        // peut que l'abaisser. Sur un très petit total, la retenue retombe à
        // zéro : l'annulation est alors gratuite, ce qui est le bon défaut.
        var cancellationFee = total
            .ApplyBasisPoints(tariff.CancellationFeeBasisPoints)
            .RoundDownToTen();

        return new QuoteAmounts(total, baseFare, variable, surge, driverEarning, cancellationFee);
    }
}
