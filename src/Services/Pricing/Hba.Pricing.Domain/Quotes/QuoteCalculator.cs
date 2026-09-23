using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Calcul d'un prix à partir d'une grille et d'un trajet mesuré. Fonction pure :
/// mêmes entrées, même sortie, testable sans base ni réseau.
/// </summary>
public static class QuoteCalculator
{
    public static QuoteAmounts Compute(Tariff tariff, RouteMeasurement route)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(route);

        // Part variable : distance et durée. Le contrat hba.pricing.v1.Quote
        // n'offre que trois compartiments, donc la durée est comptée avec la
        // distance. Les séparer demanderait un champ de plus au contrat.
        var distancePart = tariff.PerKilometer.Amount * route.DistanceMeters / 1_000;
        var durationPart = tariff.PerMinute.Amount * route.DurationSeconds / 60;
        var variable = MoneyXof.FromNonNegative(distancePart + durationPart);

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

        return new QuoteAmounts(total, baseFare, variable, surge, driverEarning);
    }
}
