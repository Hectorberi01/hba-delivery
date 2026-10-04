using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Ce que l'appelant déclare en plus du trajet.
/// </summary>
///
/// <remarks>
/// LE POIDS ÉTAIT DÉJÀ COLLECTÉ ET JETÉ EN SILENCE. « package_weight_grams »
/// partait de l'application client, traversait la passerelle, arrivait dans
/// <c>GetQuoteRequest</c>… et la frontière gRPC ne le passait pas à la commande. Un
/// client qui déclarait vingt kilos payait donc exactement le prix d'un colis de
/// cinq cents grammes, et le commentaire du service prétendait le prendre en
/// compte. C'est corrigé depuis le 1er octobre 2026 : <c>PricingGrpcService</c> le
/// transmet, et il arrive ici.
///
/// IL NE SUFFIT PAS DE LE TRANSMETTRE POUR QU'IL COÛTE QUELQUE CHOSE. Une grille
/// dont <c>per_kilogram</c> vaut zéro — c'est le cas de toutes celles reprises par
/// migration — ne facture toujours pas le poids. La décision de le facturer est une
/// décision de barème, prise grille par grille dans le back-office, pas une
/// propriété du code.
///
/// RIEN D'AUTRE NE PEUT ENTRER ICI SANS ROUVRIR LE POINT 35. Tout ce qu'on
/// aimerait facturer sur une course — l'attente du chauffeur, la durée réelle, un
/// détour — n'est connu QU'APRÈS la course, alors que le prix est encaissé AVANT le
/// dispatch et qu'il est le prix final. Une entrée de calcul n'est tenable que si
/// elle est connue au moment du devis.
/// </remarks>
public sealed record QuoteInputs(int PackageWeightGrams = 0);

/// <summary>
/// Ce qui s'ajoute au prix selon la nature de la prestation.
/// </summary>
///
/// <remarks>
/// POURQUOI UN SUPPLÉMENT ET NON UNE FORMULE ENTIÈRE PAR NATURE. Base, distance,
/// durée, plancher, majoration et part du chauffeur sont IDENTIQUES pour un colis
/// et pour un passager : dupliquer tout cela dans deux stratégies aurait créé deux
/// copies d'un calcul délicat — l'absorption du plancher par la part fixe, l'ordre
/// de l'arrondi — qui auraient divergé au premier correctif. Ce qui diffère
/// réellement se réduit à un montant qui s'ajoute.
///
/// LE SUPPLÉMENT ENTRE DANS LA PART VARIABLE, donc AVANT le plancher et la
/// majoration. C'est voulu : un colis lourd sur un trajet très court doit sortir du
/// plancher, et sa majoration doit porter sur le prix réel du transport, pas sur un
/// sous-total qui ignorerait son poids.
/// </remarks>
public interface IPricingStrategy
{
    ServiceType ServiceType { get; }

    MoneyXof Supplement(Tariff tariff, QuoteInputs inputs);
}

/// <summary>
/// Un colis : le poids se paie au-delà de ce que la grille inclut.
/// </summary>
///
/// <remarks>
/// LE POIDS INCLUS N'EST PAS UNE FRANCHISE COMMERCIALE, c'est ce qu'une moto porte
/// sans y penser. Au-delà, le transport change de nature — un deuxième passage, un
/// autre véhicule — et la grille le facture au kilo.
///
/// LE KILO ENTAMÉ EST DÛ, ET C'EST UN CHOIX. Facturer au gramme donnerait des
/// montants que personne ne peut vérifier de tête ; l'arrondi à l'entier supérieur
/// se relit, et il va dans le sens de celui qui porte la charge.
/// </remarks>
public sealed class DeliveryPricingStrategy : IPricingStrategy
{
    public ServiceType ServiceType => ServiceType.Delivery;

    public MoneyXof Supplement(Tariff tariff, QuoteInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(inputs);

        if (tariff.PerKilogram.Amount <= 0 || inputs.PackageWeightGrams <= tariff.IncludedWeightGrams)
        {
            return MoneyXof.Zero;
        }

        var excedent = inputs.PackageWeightGrams - tariff.IncludedWeightGrams;

        // DIVISION ENTIÈRE PLUS UN RESTE : le kilo entamé est dû. « (excedent + 999)
        // / 1000 » ferait la même chose en une ligne et déborderait sur un poids
        // absurde ; celle-ci ne déborde pas.
        var kilos = (excedent / 1_000) + (excedent % 1_000 > 0 ? 1 : 0);

        return MoneyXof.FromNonNegative(tariff.PerKilogram.Amount * kilos);
    }
}

/// <summary>
/// Une course de personnes : aucun supplément.
/// </summary>
///
/// <remarks>
/// ELLE NE REND PAS ZÉRO PAR PARESSE, ET CE FICHIER EST LE BON ENDROIT POUR LE
/// DIRE. Un passager n'a pas de poids déclaré, et le nombre de passagers n'existe
/// dans aucun contrat du dépôt — l'ajouter serait une décision, pas une
/// implémentation. L'attente du chauffeur, elle, contredit le point 35 : elle n'est
/// connue qu'après l'encaissement, et FedaPay ne sait ni pré-autoriser ni capturer
/// (ADR 0017).
///
/// CE QUI DISTINGUE DÉJÀ UNE COURSE D'UN COLIS, c'est sa GRILLE : ses propres
/// montants au kilomètre, à la minute, son propre plancher et sa propre part du
/// chauffeur, dans sa propre plage horaire. C'est là que se joue la différence, et
/// elle est réelle.
/// </remarks>
public sealed class RidePricingStrategy : IPricingStrategy
{
    public ServiceType ServiceType => ServiceType.Ride;

    public MoneyXof Supplement(Tariff tariff, QuoteInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(tariff);

        // UN POIDS DÉCLARÉ SUR UNE COURSE EST UNE ERREUR D'APPELANT, PAS UN BAGAGE.
        // On ne lève pas — un devis refusé pour un champ parasite serait une mauvaise
        // réponse — mais on ne facture rien non plus. La grille d'une course interdit
        // de son côté un tarif au kilo : voir Tariff.Create.
        return MoneyXof.Zero;
    }
}

/// <summary>
/// La stratégie d'une nature. Écrite une fois, pour qu'un ajout de nature ne se
/// cherche pas dans trois fichiers.
/// </summary>
public static class PricingStrategies
{
    private static readonly IReadOnlyDictionary<ServiceType, IPricingStrategy> Toutes =
        new Dictionary<ServiceType, IPricingStrategy>
        {
            [ServiceType.Delivery] = new DeliveryPricingStrategy(),
            [ServiceType.Ride] = new RidePricingStrategy(),
        };

    /// <summary>
    /// UNE NATURE INCONNUE LÈVE, ELLE NE RETOMBE PAS SUR LE COLIS. Un devis calculé
    /// avec la mauvaise stratégie produirait un prix plausible et faux, ce qui est
    /// bien pire qu'un refus : personne ne le verrait.
    /// </summary>
    public static IPricingStrategy For(ServiceType serviceType)
        => Toutes.TryGetValue(serviceType, out var strategie)
            ? strategie
            : throw new DomainException(
                "UNKNOWN_SERVICE_TYPE",
                $"Aucune stratégie de prix pour la nature {serviceType}.");
}
