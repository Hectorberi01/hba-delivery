using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Pricing.V1;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Application.Features.Quotes.Commands;
using Hba.Pricing.Application.Features.Quotes.Queries;
using Hba.Pricing.Application.Features.Tariffs.Commands;
using Hba.Pricing.Application.Features.Tariffs.Queries;
using Hba.Pricing.Application.Features.Zones.Commands;
using Hba.Pricing.Application.Features.Zones.Queries;
using Microsoft.AspNetCore.Authorization;
using CommonMoney = Hba.Contracts.Common.V1.Money;
using ProtoServiceType = Hba.Contracts.Common.V1.ServiceType;
using ProtoVehicleType = Hba.Contracts.Common.V1.VehicleType;
using DomainServiceType = Hba.Pricing.Domain.Tariffs.ServiceType;
using DomainTimeBand = Hba.Pricing.Domain.Tariffs.TimeBand;
using DomainVehicleType = Hba.Pricing.Domain.Tariffs.VehicleType;
using CommonGeoPoint = Hba.Contracts.Common.V1.GeoPoint;
using DomainGeoPoint = Hba.Pricing.Domain.ValueObjects.GeoPoint;
using ProtoZone = Hba.Contracts.Pricing.V1.Zone;

namespace Hba.Pricing.Api.Grpc;

/// <summary>
/// Entree synchrone du service Pricing.
///
/// PRICING EST LA SEULE AUTORITE SUR LE PRIX, comme le dit le contrat : ni le
/// client, ni le livreur, ni le commercant ne le fixent. Ce service ne prend
/// donc aucun montant en entree — seulement deux points, un vehicule et un
/// poids — et rend un devis qui sera consomme tel quel.
/// </summary>
[Authorize]
public sealed class PricingGrpcService(IDispatcher dispatcher) : PricingService.PricingServiceBase
{
    public override async Task<Quote> GetQuote(GetQuoteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Pickup is null || request.Dropoff is null)
        {
            throw new DomainException(
                "MISSING_POINTS",
                "Un devis demande un point d'enlevement et un point de remise.");
        }

        var view = await dispatcher.SendAsync(
            new GetQuoteCommand(
                request.Pickup.Latitude,
                request.Pickup.Longitude,
                request.Dropoff.Latitude,
                request.Dropoff.Longitude,
                ToDomain(request.ServiceType),
                ToDomain(request.VehicleType),

                // LE POIDS EST ENFIN TRANSMIS. Le champ existait au contrat depuis le
                // debut et cette methode le jetait : un colis de trente kilos etait
                // chiffre comme une enveloppe. Il n'est lu que pour une livraison, la
                // strategie de la nature s'en charge.
                request.PackageWeightGrams),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<Quote> GetQuoteById(GetQuoteByIdRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetQuoteByIdQuery(ParseId(request.QuoteId, "quote_id")),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    /// <summary>
    /// Consomme le devis pour une livraison. Appele par Delivery au moment de
    /// la creation, JAMAIS par une application cliente : c'est le service qui
    /// cree la livraison qui fige le prix, pas celui qui l'affiche.
    /// </summary>
    public override async Task<Quote> ConsumeQuote(ConsumeQuoteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Pickup is null || request.Dropoff is null)
        {
            // SANS LES POINTS, ON NE CONSOMME PAS. Les accepter absents
            // rouvrirait le trou du 29 septembre 2026 pour tout appelant qui
            // omettrait les champs — un ancien binaire, par exemple.
            throw new DomainException(
                "MISSING_POINTS",
                "La consommation d'un devis nomme le trajet de la livraison.");
        }

        var view = await dispatcher.SendAsync(
            new ConsumeQuoteCommand(
                ParseId(request.QuoteId, "quote_id"),
                ParseId(request.DeliveryId, "delivery_id"),
                ToDomain(request.ServiceType),
                request.Pickup.Latitude,
                request.Pickup.Longitude,
                request.Dropoff.Latitude,
                request.Dropoff.Longitude),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    /// <summary>
    /// Un identifiant mal forme est une erreur d'appelant, pas une panne : il
    /// devient INVALID_ARGUMENT et non une exception de parsing remontee en
    /// INTERNAL avec une reference de support.
    /// </summary>
    private static Guid ParseId(string value, string field)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException("INVALID_ID", $"Le champ {field} n'est pas un identifiant valide.");

    // ---- Administration des grilles -----------------------------------------
    //
    // CES TROIS APPELS NE VERIFIENT PAS L'AUTORISATION ICI, ET CE N'EST PAS UN
    // OUBLI. Le [Authorize] de la classe etablit qu'il y a un appelant ; QUI a
    // le droit d'ecrire une grille est decide dans PricingAccess, appele par
    // chaque handler. L'autorisation metier vit dans la couche Application, ou
    // elle est testable sans serveur gRPC (ADR 0007).

    public override async Task<Tariff> CreateTariff(CreateTariffRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.BaseFare is null || request.PerKilometer is null
            || request.PerMinute is null || request.MinimumFare is null)
        {
            throw new DomainException(
                "VALIDATION_FAILED",
                "Une grille porte quatre montants : base, kilometre, minute, plancher.");
        }

        var view = await dispatcher.SendAsync(
            new CreateTariffCommand(
                request.TariffVersion,
                request.ZoneCode,
                ExigeUneNature(request.ServiceType),
                ExigeUnVehicule(request.VehicleType),
                DebutDePlage(request.BandFromMinute, request.BandToMinute),
                FinDePlage(request.BandFromMinute, request.BandToMinute),
                request.BaseFare.Amount,
                request.PerKilometer.Amount,
                request.PerMinute.Amount,
                request.MinimumFare.Amount,
                request.SurgeBasisPoints,
                request.DriverShareBasisPoints,
                request.ValidFrom?.ToDateTimeOffset(),
                request.IdempotencyKey,
                request.PerKilogram?.Amount ?? 0,
                request.IncludedWeightGrams,
                request.CancellationFeeBasisPoints),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<Tariff> CloseTariff(CloseTariffRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new CloseTariffCommand(
                ParseId(request.TariffId, "tariff_id"),
                request.At?.ToDateTimeOffset()),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<ListTariffsResponse> ListTariffs(
        ListTariffsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListTariffsQuery(
                string.IsNullOrWhiteSpace(request.ZoneCode) ? null : request.ZoneCode,

                // MEME TRADUCTION QUE POUR LE VEHICULE, ET POUR LA MEME RAISON : en
                // LECTURE, « non precise » veut dire « toutes les natures ». C'est le
                // seul appel ou la valeur zero signifie cela — GetQuote en fait un
                // colis, CreateTariff la refuse.
                request.ServiceType == ProtoServiceType.Unspecified
                    ? null
                    : ToDomain(request.ServiceType),

                // LA VALEUR ZERO DU CONTRAT DEVIENT UN NUL, c'est-a-dire « tous
                // les vehicules ». C'est la seule frontiere ou UNSPECIFIED a un
                // sens : l'enum du domaine ne le porte pas.
                request.VehicleType == ProtoVehicleType.Unspecified
                    ? null
                    : ToDomain(request.VehicleType),
                request.IncludeClosed),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListTariffsResponse();
        reponse.Tariffs.AddRange(vues.Select(ToProto));
        return reponse;
    }

    /// <summary>
    /// La nature d'une grille, qui ne peut pas etre deduite.
    /// </summary>
    ///
    /// <remarks>
    /// MEME RAISONNEMENT QUE POUR LE VEHICULE, UN CRAN PLUS GRAVE. Un repli sur le
    /// colis a un sens quand un CLIENT demande un devis sans preciser : avant le
    /// 1er octobre 2026 toutes les grilles etaient des grilles de colis, et une
    /// application deja installee n'a aucun moyen de nommer la nature. ECRIRE une
    /// grille sans dire laquelle n'en a aucun : la grille de nuit des courses se
    /// retrouverait a tarifer des paquets, et le premier a le voir serait le
    /// chauffeur paye sur la part d'un livreur.
    /// </remarks>
    private static DomainServiceType ExigeUneNature(ProtoServiceType serviceType) => serviceType switch
    {
        ProtoServiceType.Delivery => DomainServiceType.Delivery,
        ProtoServiceType.Ride => DomainServiceType.Ride,
        _ => throw new DomainException(
            "VALIDATION_FAILED",
            "Une grille nomme la nature qu'elle tarife : colis ou course."),
    };

    /// <summary>
    /// Debut de la plage horaire d'une grille, en minutes depuis minuit local.
    /// </summary>
    ///
    /// <remarks>
    /// DEUX BORNES A ZERO VALENT LA JOURNEE ENTIERE, et c'est ici — a la frontiere —
    /// qu'on le decide, pas dans le domaine. En protobuf3 un entier absent et un
    /// entier a zero sont la meme chose : un appelant qui ne connait pas ces champs
    /// envoie 0 et 0, et <c>TimeBand.Create</c> refuse a juste titre deux bornes
    /// egales. Le domaine garde donc sa regle, et la frontiere traduit l'absence.
    ///
    /// DEUX BORNES EGALES ET NON NULLES PASSENT JUSQU'AU DOMAINE, qui les refuse.
    /// C'est voulu : 540 → 540 n'est pas une omission, c'est une plage vide saisie a
    /// la main, et elle doit etre signalee.
    /// </remarks>
    private static int DebutDePlage(int from, int to) => from == 0 && to == 0 ? 0 : from;

    private static int FinDePlage(int from, int to)
        => from == 0 && to == 0 ? DomainTimeBand.MinutesParJour : to;

    /// <summary>
    /// Le vehicule d'une grille, qui ne peut pas etre deduit.
    /// </summary>
    ///
    /// <remarks>
    /// CE N'EST PAS <c>ToDomain</c>, ET LA DIFFERENCE EST TOUT LE PROPOS. Le
    /// repli sur la moto a un sens quand un CLIENT ne precise rien : on lui
    /// donne la grille la moins chere, il n'y perd pas. ECRIRE une grille sans
    /// dire laquelle n'en a aucun : cela creerait un bareme moto sous le nom
    /// d'un autre vehicule, et personne ne le verrait avant la premiere
    /// facture — ni l'auteur de la saisie, ni le livreur paye en dessous.
    /// </remarks>
    private static DomainVehicleType ExigeUnVehicule(ProtoVehicleType vehicleType) => vehicleType switch
    {
        ProtoVehicleType.Motorcycle => DomainVehicleType.Motorcycle,
        ProtoVehicleType.Car => DomainVehicleType.Car,
        ProtoVehicleType.Van => DomainVehicleType.Van,
        ProtoVehicleType.Bicycle => DomainVehicleType.Bicycle,
        ProtoVehicleType.Tricycle => DomainVehicleType.Tricycle,
        _ => throw new DomainException(
            "VALIDATION_FAILED",
            "Une grille nomme le vehicule qu'elle tarife."),
    };

    private static Tariff ToProto(TariffView view) => new()
    {
        Id = view.Id.ToString(),
        TariffVersion = view.TariffVersion,
        ZoneCode = view.ZoneCode,
        VehicleType = view.VehicleType switch
        {
            DomainVehicleType.Car => ProtoVehicleType.Car,
            DomainVehicleType.Van => ProtoVehicleType.Van,
            DomainVehicleType.Bicycle => ProtoVehicleType.Bicycle,
            DomainVehicleType.Tricycle => ProtoVehicleType.Tricycle,
            _ => ProtoVehicleType.Motorcycle,
        },
        BaseFare = Money(view.BaseFare),
        PerKilometer = Money(view.PerKilometer),
        PerMinute = Money(view.PerMinute),
        MinimumFare = Money(view.MinimumFare),
        PerKilogram = Money(view.PerKilogram),
        IncludedWeightGrams = view.IncludedWeightGrams,
        ServiceType = ToProto(view.ServiceType),
        BandFromMinute = view.BandFromMinute,
        BandToMinute = view.BandToMinute,
        SurgeBasisPoints = view.SurgeBasisPoints,
        DriverShareBasisPoints = view.DriverShareBasisPoints,
        CancellationFeeBasisPoints = view.CancellationFeeBasisPoints,
        ValidFrom = Timestamp.FromDateTimeOffset(view.ValidFrom),
        ValidUntil = view.ValidUntil is null
            ? null
            : Timestamp.FromDateTimeOffset(view.ValidUntil.Value),
    };

    /// <summary>
    /// La nature non precisee devient le colis.
    /// </summary>
    ///
    /// <remarks>
    /// CE REPLI EST UNE DETTE ASSUMEE, PAS UN DEFAUT ELEGANT. Toutes les grilles
    /// anterieures au 1er octobre 2026 sont des grilles de colis, et toutes les
    /// applications deja installees demandent des devis sans nommer la nature :
    /// refuser la valeur zero aurait arrete les livraisons le jour de la mise en
    /// service. Ride, lui, est ne apres et remplit le champ.
    ///
    /// IL DISPARAITRA QUAND LES CLIENTS DEPLOYES ENVERRONT TOUS LA NATURE. D'ici la,
    /// un devis de course demande par un appelant muet est chiffre en colis, et le
    /// refus se produit a la consommation (QUOTE_SERVICE_MISMATCH) plutot qu'au
    /// paiement.
    /// </remarks>
    private static DomainServiceType ToDomain(ProtoServiceType serviceType) => serviceType switch
    {
        ProtoServiceType.Ride => DomainServiceType.Ride,
        _ => DomainServiceType.Delivery,
    };

    private static ProtoServiceType ToProto(DomainServiceType serviceType) => serviceType switch
    {
        DomainServiceType.Ride => ProtoServiceType.Ride,
        DomainServiceType.Delivery => ProtoServiceType.Delivery,
        _ => ProtoServiceType.Unspecified,
    };

    /// <summary>
    /// Le vehicule non precise devient la moto.
    ///
    /// CE N'EST PAS UN DEFAUT ARBITRAIRE : a Cotonou la course se fait a moto,
    /// le zemidjan est le mode par defaut du marche, et c'est la grille la moins
    /// chere — un client qui ne precise rien ne se voit donc jamais facturer le
    /// tarif d'une camionnette.
    /// </summary>
    public override async Task<ListZonesResponse> ListZones(
        ListZonesRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListZonesQuery(request.IncludeInactive),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListZonesResponse();
        reponse.Zones.AddRange(vues.Select(ToProto));

        return reponse;
    }

    public override async Task<ProtoZone> SaveZone(SaveZoneRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // LES COORDONNEES SONT VALIDEES PAR LE DOMAINE, UN SOMMET A LA FOIS.
        // GeoPoint.Create refuse une latitude hors bornes : un contour entier est
        // donc rejete pour un seul sommet aberrant, ce qui est la bonne granularite
        // — un polygone a moitie juste n'existe pas.
        var contour = request.Ring
            .Select(p => DomainGeoPoint.Create(p.Latitude, p.Longitude))
            .ToList();

        var vue = await dispatcher.SendAsync(
            new SaveZoneCommand(request.Code, request.Name, request.Priority, contour),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<ProtoZone> SetZoneActive(
        SetZoneActiveRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new SetZoneActiveCommand(request.Code, request.Active),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    private static ProtoZone ToProto(ZoneView vue)
    {
        var zone = new ProtoZone
        {
            Id = vue.Id.ToString(),
            Code = vue.Code,
            Name = vue.Name,
            Priority = vue.Priority,
            Active = vue.Active,
        };

        zone.Ring.AddRange(vue.Ring.Select(p => new CommonGeoPoint
        {
            Latitude = p.Latitude,
            Longitude = p.Longitude,
        }));

        return zone;
    }

    private static DomainVehicleType ToDomain(ProtoVehicleType vehicleType) => vehicleType switch
    {
        ProtoVehicleType.Car => DomainVehicleType.Car,
        ProtoVehicleType.Van => DomainVehicleType.Van,

        // CHACUN SON BRAS, SINON CHACUN SON PRIX — CELUI D'UN AUTRE. Le repli
        // sur la moto est un choix de defaut pour l'INDETERMINE (voir
        // ci-dessus) ; l'etendre au velo lui aurait facture la grille du
        // zemidjan, sans que rien ne le signale.
        ProtoVehicleType.Bicycle => DomainVehicleType.Bicycle,
        ProtoVehicleType.Tricycle => DomainVehicleType.Tricycle,
        _ => DomainVehicleType.Motorcycle,
    };

    private static Quote ToProto(QuoteView view) => new()
    {
        Id = view.Id.ToString(),
        TariffVersion = view.TariffVersion,
        ZoneId = view.ZoneCode,
        Total = Money(view.Total),
        BaseFare = Money(view.BaseFare),
        DistanceFare = Money(view.DistanceFare),
        SurgeFare = Money(view.SurgeFare),
        DriverEarning = Money(view.DriverEarning),
        CancellationFee = Money(view.CancellationFee),
        DistanceMeters = view.DistanceMeters,
        DurationSeconds = view.DurationSeconds,
        CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        ExpiresAt = Timestamp.FromDateTimeOffset(view.ExpiresAt),
        Consumed = view.Consumed,
        ServiceType = ToProto(view.ServiceType),
    };

    private static CommonMoney Money(long amount) => new()
    {
        Amount = amount,
        Currency = Hba.Pricing.Domain.ValueObjects.MoneyXof.CurrencyCode,
    };
}
