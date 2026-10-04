namespace Hba.Pricing.Domain.Tariffs;

/// <summary>
/// La nature de la prestation tarifée : un colis, ou une course de personnes.
/// </summary>
///
/// <remarks>
/// POURQUOI ELLE ENTRE DANS LA CLÉ DE LA GRILLE. Jusqu'au 1er octobre 2026, une
/// course de personnes était facturée sur la grille du colis — même tarif au
/// kilomètre, même plancher, même part du chauffeur. Ça fonctionnait, et les
/// chiffres étaient faux.
///
/// ELLE NE CHANGE PAS LA FORMULE, ELLE CHANGE LA GRILLE. C'est le constat qui a
/// écarté l'idée d'une stratégie de calcul par nature : base + distance + durée,
/// plancher, majoration, part du chauffeur valent pour les deux. Ce qui diffère,
/// ce sont les MONTANTS — et une seule chose de plus, le poids, que seul un colis
/// porte. Voir <see cref="IPricingStrategy"/>.
///
/// « NON PRÉCISÉE » EST REFUSÉE À LA CRÉATION, pas interprétée comme un colis :
/// une grille sans nature serait choisie pour une course aussi bien que pour un
/// paquet. Les grilles antérieures ont été reprises explicitement en Delivery par
/// migration.
///
/// LES NUMÉROS SUIVENT hba.common.v1.ServiceType et ne se réordonnent pas : ils
/// sont persistés et portés par le contrat.
/// </remarks>
public enum ServiceType
{
    Unspecified = 0,
    Delivery = 1,
    Ride = 2,
}
