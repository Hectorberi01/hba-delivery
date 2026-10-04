using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Domain.Exceptions;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Domain.Quotes;

/// <summary>
/// Prix proposé pour un trajet, valable un temps borné et consommable une seule
/// fois. Un devis consommé est recopié dans la livraison et figé : c'est la
/// raison d'être de l'ADR 0004.
/// </summary>
public sealed class Quote : AggregateRoot
{
    private Quote()
    {
    }

    private Quote(
        Guid id,
        string tariffVersion,
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        GeoPoint pickup,
        GeoPoint dropoff,
        RouteMeasurement route,
        QuoteAmounts amounts,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        TariffVersion = tariffVersion;
        ZoneCode = zoneCode;
        ServiceType = serviceType;
        VehicleType = vehicleType;
        Pickup = pickup;
        Dropoff = dropoff;
        Route = route;
        Total = amounts.Total;
        BaseFare = amounts.BaseFare;
        VariableFare = amounts.VariableFare;
        SurgeFare = amounts.SurgeFare;
        DriverEarning = amounts.DriverEarning;
        CancellationFee = amounts.CancellationFee;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public string TariffVersion { get; private set; } = string.Empty;

    public string ZoneCode { get; private set; } = string.Empty;

    /// <summary>
    /// La nature chiffrée : colis ou course de personnes.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EST GARDÉE POUR ÊTRE VÉRIFIÉE À LA CONSOMMATION, et c'est tout son objet.
    /// Un devis de colis est moins cher qu'un devis de course sur la même distance :
    /// sans ce champ, rien n'empêcherait de payer une course au prix d'un paquet. C'est
    /// la même classe de faille que le trajet, déjà refermée par QUOTE_TRIP_MISMATCH.
    /// </remarks>
    public ServiceType ServiceType { get; private set; }

    public VehicleType VehicleType { get; private set; }

    public GeoPoint Pickup { get; private set; } = null!;

    public GeoPoint Dropoff { get; private set; } = null!;

    public RouteMeasurement Route { get; private set; } = null!;

    public MoneyXof Total { get; private set; } = null!;

    public MoneyXof BaseFare { get; private set; } = null!;

    public MoneyXof VariableFare { get; private set; } = null!;

    public MoneyXof SurgeFare { get; private set; } = null!;

    public MoneyXof DriverEarning { get; private set; } = null!;

    /// <summary>
    /// Retenue due si le client annule APRÈS l'affectation d'un livreur, figée au
    /// taux de la grille au moment du devis. Zéro vaut annulation gratuite.
    ///
    /// ELLE REVIENT EN ENTIER AU LIVREUR : ce n'est pas une part du total à
    /// partager, c'est le dédommagement d'un déplacement engagé. Voir
    /// <c>Tariff.CancellationFeeBasisPoints</c>.
    /// </summary>
    public MoneyXof CancellationFee { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Livraison qui a consommé ce devis. Nul tant qu'il ne l'est pas.</summary>
    public Guid? ConsumedByDeliveryId { get; private set; }

    public bool IsConsumed => ConsumedByDeliveryId is not null;

    public bool HasExpiredAt(DateTimeOffset instant) => instant >= ExpiresAt;

    public static Quote Create(
        Guid id,
        Tariff tariff,
        GeoPoint pickup,
        GeoPoint dropoff,
        RouteMeasurement route,
        DateTimeOffset createdAt,
        TimeSpan lifetime,
        QuoteInputs? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);
        ArgumentNullException.ThrowIfNull(route);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("INVALID_QUOTE_LIFETIME", "Un devis doit avoir une durée de validité positive.");
        }

        var amounts = QuoteCalculator.Compute(tariff, route, inputs);

        return new Quote(
            id,
            tariff.TariffVersion,
            tariff.ZoneCode,
            tariff.ServiceType,
            tariff.VehicleType,
            pickup,
            dropoff,
            route,
            amounts,
            createdAt,
            createdAt.Add(lifetime));
    }

    /// <summary>
    /// Ecart tolere entre le trajet devise et le trajet livre, a chaque bout.
    /// </summary>
    ///
    /// <remarks>
    /// POURQUOI UNE TOLERANCE, ET POURQUOI CELLE-LA. L'application envoie pour la
    /// livraison les coordonnees exactes qu'elle a devisees : en theorie l'ecart
    /// est nul. La tolerance n'est donc pas la pour absorber une derive de GPS,
    /// mais l'aller-retour JSON et l'arrondi des flottants — quelques metres au
    /// plus.
    ///
    /// ELLE RESTE DELIBEREMENT INUTILISABLE COMME LEVIER : 250 m a chaque bout ne
    /// peuvent deplacer la distance routee que de 500 m, soit quelques dizaines
    /// de francs sur la part variable. Aucun reglage ne l'elargit, et c'est
    /// voulu : un parametre de configuration finirait par etre desserre un jour
    /// de mise en production difficile, et c'est exactement ce trou qu'on vient
    /// de refermer.
    /// </remarks>
    public const double ToleranceDeTrajetMetres = 250;

    /// <summary>
    /// Consomme le devis au profit d'une livraison, pour LE TRAJET QU'IL A CHIFFRE.
    ///
    /// IDEMPOTENT PAR LIVRAISON : la création d'une livraison est rejouable, et
    /// un rejeu ne doit pas se heurter à « devis déjà consommé » alors que
    /// c'est la même livraison qui redemande. Une AUTRE livraison, elle, est
    /// refusée.
    /// </summary>
    ///
    /// <remarks>
    /// LES POINTS SONT VERIFIES DEPUIS LE 30 SEPTEMBRE 2026, et leur absence
    /// etait une fuite d'argent franche. Le devis portait deja
    /// <see cref="Pickup"/> et <see cref="Dropoff"/> ; personne ne les
    /// comparait, et Delivery creait la course avec les points de LA COMMANDE.
    ///
    /// Il suffisait donc de deviser Ganhi vers Ganhi — 300 m, 700 XOF —, puis de
    /// creer la livraison avec le meme devis et une remise a Calavi, 20 km plus
    /// loin. L'ADR 0004 faisait ensuite son travail avec application : le prix
    /// fige ne bougeait plus, le paiement etait encaisse a 700 XOF, et la part du
    /// livreur calculee sur 700 XOF. Le livreur roulait 20 km au prix de 300 m,
    /// et HBA portait l'ecart sur chaque course. Rien la-dedans ne demandait
    /// d'outil : le trafic de l'application suffisait.
    ///
    /// C'EST A PRICING DE TRANCHER, PAS A DELIVERY. Le devis est le seul a savoir
    /// ce qu'il a chiffre ; laisser le controle au service appelant reviendrait a
    /// lui demander de se surveiller lui-meme.
    /// </remarks>
    public void Consume(
        Guid deliveryId,
        ServiceType serviceType,
        GeoPoint pickup,
        GeoPoint dropoff,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);

        // LA NATURE SE VERIFIE, COMME LE TRAJET, ET POUR LA MEME RAISON.
        //
        // Un devis de colis est moins cher qu'un devis de course sur la meme distance
        // — sa grille a d'autres montants. Sans ce controle, un telephone pourrait
        // demander un devis de colis puis l'utiliser pour payer une course : le prix
        // serait plausible et faux, et rien dans les journaux ne le dirait. C'est
        // exactement la faille que QUOTE_TRIP_MISMATCH a refermee pour la distance.
        //
        // NON PRECISEE EST REFUSEE AUSSI : un appelant qui oublie le champ ne doit pas
        // consommer n'importe quel devis.
        if (serviceType != ServiceType)
        {
            throw new DomainException(
                PricingErrorCodes.QuoteServiceMismatch,
                $"Ce devis a ete etabli pour une prestation de type {ServiceType}, pas {serviceType}.");
        }

        if (deliveryId == Guid.Empty)
        {
            throw new DomainException("MISSING_DELIVERY_ID", "La consommation d'un devis nomme la livraison.");
        }

        if (ConsumedByDeliveryId == deliveryId)
        {
            return;
        }

        if (IsConsumed)
        {
            throw new DomainException("QUOTE_ALREADY_CONSUMED", "Ce devis a déjà servi à une autre livraison.");
        }

        if (HasExpiredAt(now))
        {
            throw new DomainException("QUOTE_EXPIRED", "Ce devis a expiré : il faut en demander un nouveau.");
        }

        EnsureSameTrip(pickup, dropoff);

        ConsumedByDeliveryId = deliveryId;
        ConsumedAt = now;
    }

    /// <summary>
    /// Le trajet presente doit etre celui qui a ete chiffre.
    /// </summary>
    ///
    /// <remarks>
    /// LE MESSAGE NE DIT PAS DE COMBIEN L'ECART DEPASSE, et ce n'est pas de
    /// l'avarice : un refus qui chiffre l'ecart apprend a l'appelant ou se trouve
    /// la limite, donc comment s'y tenir juste en dessous. Il dit quoi faire —
    /// redemander un devis —, ce qui est la seule chose utile a un client de
    /// bonne foi.
    /// </remarks>
    private void EnsureSameTrip(GeoPoint pickup, GeoPoint dropoff)
    {
        var ecartCollecte = Pickup.DistanceEnMetresVers(pickup);
        var ecartRemise = Dropoff.DistanceEnMetresVers(dropoff);

        if (ecartCollecte > ToleranceDeTrajetMetres || ecartRemise > ToleranceDeTrajetMetres)
        {
            throw new DomainException(
                "QUOTE_TRIP_MISMATCH",
                "Ce devis a été établi pour un autre trajet : il faut en demander un nouveau.");
        }
    }
}
