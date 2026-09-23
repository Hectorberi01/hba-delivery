using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Décomposition d'un prix. Les trois parts additionnées font exactement le
/// total : c'est ce qui permet à un agent du support d'expliquer une facture
/// sans recalculer quoi que ce soit.
/// </summary>
/// <param name="Total">Ce que paie le donneur d'ordre.</param>
/// <param name="BaseFare">Part fixe, ajustée du plancher et de l'arrondi.</param>
/// <param name="VariableFare">Part liée à la distance et à la durée.</param>
/// <param name="SurgeFare">Majoration, nulle tant qu'aucune règle n'est tranchée.</param>
/// <param name="DriverEarning">Part du livreur, toujours inférieure ou égale au total.</param>
public sealed record QuoteAmounts(
    MoneyXof Total,
    MoneyXof BaseFare,
    MoneyXof VariableFare,
    MoneyXof SurgeFare,
    MoneyXof DriverEarning);
