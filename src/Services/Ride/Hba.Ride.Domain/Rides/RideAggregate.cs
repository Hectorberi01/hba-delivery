using Hba.BuildingBlocks.Domain;
using Hba.Ride.Domain.Exceptions;
using Hba.Ride.Domain.Results;
using Hba.Ride.Domain.Rides.Events;
using Hba.Ride.Domain.ValueObjects;

namespace Hba.Ride.Domain.Rides;

/// <summary>
/// Une course de personnes, de la demande à la descente.
/// </summary>
///
/// <remarks>
/// LE PASSAGER EST UN <c>customer</c>, ET IL N'Y A PAS D'ACTEUR « passenger ».
/// Décidé le 1er octobre 2026, point 35 : le même compte envoie un colis le
/// matin et prend une course l'après-midi. D'où <c>CustomerId</c>, et non
/// <c>PassengerId</c> comme le proposait la demande initiale.
///
/// CET AGRÉGAT NE SAIT RIEN DU DEHORS. Il ne cherche pas de chauffeur, ne
/// calcule pas d'itinéraire, ne connaît aucune position temps réel, n'exécute
/// aucun paiement. Il tient une machine à états et refuse ce qui n'a pas de
/// sens — c'est tout, et c'est déjà ce qui empêche une course de démarrer sans
/// chauffeur sur place.
///
/// LES TRANSITIONS RENDENT UN <c>Result</c>, ELLES NE LÈVENT PAS. C'est la
/// convention propre à ce service ; la frontière la retraduit en refus
/// identiques à ceux du reste du dépôt. Voir <see cref="Result"/>.
/// </remarks>
public sealed class RideAggregate : AggregateRoot
{
    private RideAggregate()
    {
    }

    private RideAggregate(
        Guid id,
        string customerId,
        GeoPoint pickup,
        GeoPoint destination,
        VehicleType vehicleType,
        TripEstimate estimate,
        MoneyXof driverEarning,
        MoneyXof cancellationFee,
        DateTimeOffset requestedAt)
        : base(id)
    {
        CustomerId = customerId;
        DriverEarning = driverEarning;
        CancellationFee = cancellationFee;
        Pickup = pickup;
        Destination = destination;
        VehicleType = vehicleType;
        Estimate = estimate;
        Status = RideStatus.Requested;
        RequestedAt = requestedAt;
    }

    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Vide tant qu'aucun chauffeur n'a accepté.</summary>
    public string? DriverId { get; private set; }

    /// <summary>
    /// Le chauffeur affecté : nom, téléphone, véhicule. Nul avant l'affectation.
    /// </summary>
    ///
    /// <remarks>
    /// NUL MÊME APRÈS L'AFFECTATION SI DRIVER ÉTAIT INJOIGNABLE. L'identifiant, lui,
    /// est toujours posé : la course a bien un chauffeur, on ne sait simplement pas
    /// encore son nom. Refuser l'affectation pour un profil manquant laisserait un
    /// chauffeur réservé sur une course qui ne le reconnaît pas.
    /// </remarks>
    public AssignedDriver? Driver { get; private set; }

    public GeoPoint Pickup { get; private set; } = null!;

    public GeoPoint Destination { get; private set; } = null!;

    public VehicleType VehicleType { get; private set; }

    public RideStatus Status { get; private set; }

    /// <summary>Ce qui a été annoncé avant d'engager le passager.</summary>
    public TripEstimate Estimate { get; private set; } = null!;

    /// <summary>
    /// Le prix réellement dû.
    /// </summary>
    ///
    /// <remarks>
    /// IL VAUT L'ESTIMATION, ET C'EST UNE DÉCISION, PAS UN RACCOURCI. Le point
    /// 35 a tranché « prix ferme à la commande » : FedaPay encaisse mais ne
    /// pré-autorise pas, et n'a aucune API de remboursement — ni la capture au
    /// prix final ni la compensation d'un écart n'ont de chemin automatique. Le
    /// champ existe quand même, parce que le jour où un fournisseur le
    /// permettra, c'est ici que l'écart se logera, et non dans une table
    /// parallèle.
    /// </remarks>
    public MoneyXof? FinalPrice { get; private set; }

    /// <summary>
    /// Ce que touchera le chauffeur. Figé par Pricing, jamais calculé ici.
    /// </summary>
    ///
    /// <remarks>
    /// IL NE SORT JAMAIS VERS LE PASSAGER. Ce que gagne le chauffeur ne regarde
    /// pas le client : la vue de la course ne le porte pas, et la passerelle
    /// cliente ne pourrait donc pas le laisser fuir par distraction.
    ///
    /// IL EXISTE PARCE QUE L'OFFRE EN A BESOIN. Le chauffeur accepte sur ce
    /// montant ; tant que la course ne le portait pas, l'offre affichait zéro
    /// franc — et personne n'accepte une course à zéro. Le dispatch aurait tourné
    /// à vide sans que rien ne soit en panne.
    ///
    /// IL EST DANS L'ESTIMATION AU SENS LARGE, PAS DANS <see cref="TripEstimate"/>.
    /// Celle-ci est ce qui a été ANNONCÉ AU PASSAGER : y mêler la part du
    /// chauffeur ferait porter au même objet deux publics différents.
    /// </remarks>
    public MoneyXof DriverEarning { get; private set; } = MoneyXof.Zero;

    /// <summary>
    /// Retenue due au chauffeur si le passager annule APRÈS son affectation,
    /// figée par le devis. Zéro quand la grille n'en prévoit pas.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE REVIENT EN ENTIER AU CHAUFFEUR, et <see cref="DriverEarning"/> ne s'y
    /// applique pas : HBA ne prélève rien sur une course qui n'a pas eu lieu
    /// (point 3, tranché le 2 octobre 2026).
    ///
    /// ELLE N'EST DUE QUE SI QUELQU'UN S'EST DÉPLACÉ. C'est <see cref="RetenueDue"/>
    /// qui le décide, pas cette valeur — qui n'est que le montant figé.
    /// </remarks>
    public MoneyXof CancellationFee { get; private set; } = MoneyXof.Zero;

    /// <summary>
    /// Ce qui revient au chauffeur quand la course se clôt sans aboutir.
    /// </summary>
    ///
    /// <remarks>
    /// SANS CHAUFFEUR AFFECTÉ, RIEN N'EST DÛ, quel que soit le taux de la zone :
    /// personne ne s'est déplacé. L'identifiant fait foi et non le profil — il est
    /// toujours posé à l'affectation, alors que le profil peut manquer si Driver
    /// était injoignable, et un chauffeur réservé mérite son dédommagement même
    /// quand on ne connaît pas son nom.
    /// </remarks>
    /// <remarks>
    /// PUBLIQUE PARCE QUE L'ÉCRAN POSE LA MÊME QUESTION QUE L'ÉVÉNEMENT. Le
    /// passager doit voir ce qui sera retenu AVANT de confirmer son annulation ;
    /// recalculer la règle en Dart la mettrait à deux endroits.
    /// </remarks>
    public long RetenueDue() => string.IsNullOrEmpty(DriverId) ? 0 : CancellationFee.Amount;

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? DriverAssignedAt { get; private set; }

    public DateTimeOffset? DriverArrivedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// L'intention de paiement ouverte à la commande.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EST STOCKÉE POUR LA TRAÇABILITÉ, PAS POUR REPAYER. Le support et finance
    /// en ont besoin pour retrouver une transaction chez le fournisseur à partir
    /// d'une course. L'adresse de paiement, elle, n'est PAS conservée : elle expire,
    /// et une adresse périmée affichée dans un écran de suivi serait pire que pas
    /// d'adresse du tout.
    ///
    /// ELLE EST VIDE SI L'OUVERTURE A ÉCHOUÉ, et la course est alors inutilisable :
    /// personne ne peut la payer, donc elle finira en PaymentFailed. On ne refuse
    /// pas la commande pour autant — le devis est déjà consommé, et une course
    /// enregistrée sans paiement se diagnostique, alors qu'une erreur brute laisse
    /// un devis brûlé sans trace.
    /// </remarks>
    public string? PaymentIntentId { get; private set; }

    /// <summary>Plus aucune transition ne sort de là.</summary>
    public bool EstClose => Status
        is RideStatus.Completed
        or RideStatus.Cancelled
        or RideStatus.NoDriverAvailable
        or RideStatus.PaymentFailed;

    /// <summary>
    /// Les transitions autorisées, écrites une fois.
    /// </summary>
    ///
    /// <remarks>
    /// UNE TABLE PLUTÔT QU'UN <c>if</c> DANS CHAQUE MÉTHODE. Les règles d'une
    /// machine à états dispersées dans dix méthodes finissent par se contredire ;
    /// ici elles se lisent d'un coup d'œil, et un état ajouté se déclare à un
    /// seul endroit.
    ///
    /// DEUX ABSENCES MÉRITENT D'ÊTRE DITES. On ne passe PAS de
    /// <c>DriverAssigned</c> à <c>DriverArrived</c> : le chauffeur signale son
    /// départ, et cette étape est ce qui permet au passager de le voir approcher.
    /// Et <c>InProgress</c> ne mène PAS à <c>Cancelled</c> : une course commencée
    /// ne s'annule plus, elle se termine — le passager est déjà à bord, le
    /// service est rendu.
    /// </remarks>
    private static readonly IReadOnlyDictionary<RideStatus, IReadOnlySet<RideStatus>> Transitions =
        new Dictionary<RideStatus, IReadOnlySet<RideStatus>>
        {
            [RideStatus.Requested] = new HashSet<RideStatus>
            {
                RideStatus.SearchingDriver,
                RideStatus.Cancelled,

                // LE PAIEMENT PEUT ÉCHOUER AVANT QU'ON AIT CHERCHÉ QUI QUE CE SOIT.
                // C'est même le cas le plus fréquent de cet état : un solde Mobile
                // Money insuffisant, une page de paiement abandonnée.
                RideStatus.PaymentFailed,
            },
            [RideStatus.SearchingDriver] = new HashSet<RideStatus>
            {
                RideStatus.DriverAssigned,
                RideStatus.NoDriverAvailable,
                RideStatus.Cancelled,
            },
            [RideStatus.DriverAssigned] = new HashSet<RideStatus>
            {
                RideStatus.DriverArriving,
                RideStatus.Cancelled,
            },
            [RideStatus.DriverArriving] = new HashSet<RideStatus>
            {
                RideStatus.DriverArrived,
                RideStatus.Cancelled,
            },
            [RideStatus.DriverArrived] = new HashSet<RideStatus>
            {
                RideStatus.InProgress,
                RideStatus.Cancelled,
            },
            [RideStatus.InProgress] = new HashSet<RideStatus>
            {
                RideStatus.Completed,
            },
        };

    public static bool PeutAller(RideStatus de, RideStatus vers)
        => Transitions.TryGetValue(de, out var permises) && permises.Contains(vers);

    // ------------------------------------------------------------- Naissance --

    public static Result<RideAggregate> Request(
        Guid id,
        string customerId,
        double pickupLatitude,
        double pickupLongitude,
        double destinationLatitude,
        double destinationLongitude,
        VehicleType vehicleType,
        int estimatedDistanceMeters,
        int estimatedDurationSeconds,
        long estimatedPrice,
        long driverEarning,
        long cancellationFee,
        Actor actor,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Result.Refus<RideAggregate>(
                RideErrorCodes.ValidationFailed, "Une course a un client.");
        }

        if (!GeoPoint.EstValide(pickupLatitude, pickupLongitude)
            || !GeoPoint.EstValide(destinationLatitude, destinationLongitude))
        {
            return Result.Refus<RideAggregate>(
                RideErrorCodes.InvalidCoordinates, "Coordonnées hors du globe.");
        }

        var depart = GeoPoint.Create(pickupLatitude, pickupLongitude);
        var arrivee = GeoPoint.Create(destinationLatitude, destinationLongitude);

        // DEUX POINTS IDENTIQUES NE SONT PAS UNE COURSE. Sans ce refus, le
        // dispatch chercherait un chauffeur pour un trajet nul, et le passager
        // paierait le prix plancher pour rester où il est.
        if (depart == arrivee)
        {
            return Result.Refus<RideAggregate>(
                RideErrorCodes.SamePickupAndDestination,
                "Le départ et la destination sont au même endroit.");
        }

        if (!TripEstimate.EstValide(estimatedDistanceMeters, estimatedDurationSeconds, estimatedPrice))
        {
            return Result.Refus<RideAggregate>(
                RideErrorCodes.InvalidAmount,
                "Distance, durée et prix estimés sont positifs.");
        }

        // LA PART DU CHAUFFEUR NE PEUT PAS DÉPASSER LE PRIX PAYÉ. Ce serait une
        // course à perte pour la plateforme, et c'est le genre d'erreur qu'une
        // grille mal saisie produit sans bruit : mieux vaut refuser la course que
        // de la découvrir en comptabilité.
        // LA RETENUE NE PEUT PAS DEPASSER LE PRIX, meme controle que la part du
        // chauffeur. Pricing le garantit deja ; Ride est un AUTRE service et verifie
        // ce qu'il recoit plutot que de faire confiance.
        if (!MoneyXof.EstValide(cancellationFee) || cancellationFee > estimatedPrice)
        {
            return Result.Refus<RideAggregate>(
                RideErrorCodes.ValidationFailed,
                "La retenue d'annulation ne peut pas depasser le prix de la course.");
        }

        if (!MoneyXof.EstValide(driverEarning) || driverEarning > estimatedPrice)
        {
            return Result.Refus<RideAggregate>(
                RideErrorCodes.InvalidAmount,
                "La part du chauffeur est positive et ne dépasse pas le prix de la course.");
        }

        var course = new RideAggregate(
            id,
            customerId.Trim(),
            depart,
            arrivee,
            vehicleType,
            TripEstimate.Create(estimatedDistanceMeters, estimatedDurationSeconds, MoneyXof.From(estimatedPrice)),
            MoneyXof.From(driverEarning),
            MoneyXof.From(cancellationFee),
            requestedAt);

        course.Raise(new RideRequested(id, course.CustomerId, vehicleType, actor, requestedAt));

        return Result.Ok(course);
    }

    // ------------------------------------------------------------ Transitions --

    /// <summary>
    /// Rattache l'intention de paiement ouverte juste après la commande.
    /// </summary>
    ///
    /// <remarks>
    /// CE N'EST PAS UNE TRANSITION : l'état ne change pas, la course reste en
    /// attente de paiement. C'est pourquoi la méthode ne passe pas par la table
    /// des transitions et ne lève aucun fait — rien ne s'est produit du point de
    /// vue du métier, on a seulement noté où regarder.
    /// </remarks>
    public void AttachPaymentIntent(string paymentIntentId)
    {
        if (!string.IsNullOrWhiteSpace(paymentIntentId))
        {
            PaymentIntentId = paymentIntentId.Trim();
        }
    }

    /// <summary>
    /// Le paiement a échoué ou expiré.
    /// </summary>
    ///
    /// <remarks>
    /// LE MOTIF VIENT DU FOURNISSEUR ET N'EST PAS MONTRÉ TEL QUEL AU PASSAGER :
    /// « insufficient_balance » n'est pas une phrase. Il est enregistré pour le
    /// support, et c'est l'application qui écrit ce que lit le passager.
    /// </remarks>
    public Result MarkPaymentFailed(string? reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // LE REJEU EST UN SUCCÈS, PAS UN REFUS. Kafka livre au moins une fois, et
        // le fournisseur peut notifier deux fois le même échec.
        if (Status == RideStatus.PaymentFailed)
        {
            return Result.Ok();
        }

        var refus = Exiger(RideStatus.PaymentFailed);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.PaymentFailed;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Raise(new RidePaymentFailed(Id, CustomerId, CancellationReason, actor, now));

        return Result.Ok();
    }

    public Result StartSearching(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var refus = Exiger(RideStatus.SearchingDriver);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.SearchingDriver;
        Raise(new RideDriverSearchStarted(Id, actor, now));

        return Result.Ok();
    }

    /// <summary>
    /// Un chauffeur a accepté.
    /// </summary>
    ///
    /// <remarks>
    /// <paramref name="profil"/> PEUT ÊTRE NUL, et l'affectation passe quand même.
    /// Driver injoignable à cet instant ne doit pas faire échouer une acceptation
    /// déjà actée de son côté : le chauffeur serait réservé sur une course qui ne
    /// l'a pas enregistré, et c'est exactement la panne que Delivery a connue le
    /// 27 septembre. Le passager verra « un chauffeur arrive » au lieu de son nom.
    /// </remarks>
    public Result AssignDriver(string driverId, AssignedDriver? profil, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(driverId))
        {
            return Result.Refus(RideErrorCodes.ValidationFailed, "Un chauffeur a un identifiant.");
        }

        // ON REFUSE AVANT DE REGARDER L'ÉTAT, et l'ordre compte : deux
        // acceptations concurrentes doivent produire le MÊME refus, pas un refus
        // qui dépend de laquelle est arrivée la première.
        if (DriverId is not null)
        {
            return Result.Refus(
                RideErrorCodes.DriverAlreadyAssigned,
                "Cette course a déjà un chauffeur.");
        }

        var refus = Exiger(RideStatus.DriverAssigned);
        if (refus is not null)
        {
            return refus;
        }

        DriverId = driverId.Trim();
        Driver = profil;
        DriverAssignedAt = now;
        Status = RideStatus.DriverAssigned;

        Raise(new RideDriverAssigned(Id, DriverId, actor, now));

        return Result.Ok();
    }

    public Result DriverIsOnTheWay(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var refus = Exiger(RideStatus.DriverArriving);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.DriverArriving;
        Raise(new RideDriverArriving(Id, DriverId!, actor, now));

        return Result.Ok();
    }

    public Result DriverHasArrived(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var refus = Exiger(RideStatus.DriverArrived);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.DriverArrived;
        DriverArrivedAt = now;
        Raise(new RideDriverArrived(Id, DriverId!, actor, now));

        return Result.Ok();
    }

    /// <summary>
    /// Le passager est à bord.
    /// </summary>
    ///
    /// <remarks>
    /// ÉCHOUE SI LE CHAUFFEUR N'EST PAS ARRIVÉ, et c'est la règle que la demande
    /// cite en exemple. Elle n'est pas formelle : une course qui démarre avant
    /// l'arrivée fait courir le compteur pendant que le passager attend encore
    /// sur le trottoir.
    ///
    /// LE REFUS EST NOMMÉ, pas générique. « Transition interdite » laisserait
    /// l'application livreur afficher un message incompréhensible ;
    /// DRIVER_NOT_ARRIVED lui permet de dire « signalez d'abord votre arrivée ».
    /// </remarks>
    public Result Start(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status != RideStatus.DriverArrived)
        {
            return Result.Refus(
                RideErrorCodes.DriverNotArrived,
                "La course ne démarre qu'une fois le chauffeur sur place.");
        }

        Status = RideStatus.InProgress;
        StartedAt = now;
        Raise(new RideStarted(Id, DriverId!, actor, now));

        return Result.Ok();
    }

    /// <summary>
    /// Fin de course.
    /// </summary>
    ///
    /// <remarks>
    /// LE PRIX FINAL VAUT L'ESTIMATION, PAR DÉCISION. Voir
    /// <see cref="FinalPrice"/> : le prix est ferme à la commande. Le paramètre
    /// n'existe donc pas — l'ajouter aujourd'hui laisserait croire qu'un appelant
    /// peut fixer le montant dû, ce qu'aucune règle n'autorise.
    /// </remarks>
    public Result Complete(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var refus = Exiger(RideStatus.Completed);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.Completed;
        CompletedAt = now;
        FinalPrice = Estimate.Price;

        Raise(new RideCompleted(Id, DriverId!, FinalPrice, actor, now));

        return Result.Ok();
    }

    public Result Cancel(string? reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // TROIS REFUS DISTINCTS LÀ OÙ UN SEUL SUFFIRAIT TECHNIQUEMENT. Le
        // passager qui appuie sur « annuler » mérite de savoir POURQUOI c'est
        // refusé : parce que c'est déjà fini, ou parce qu'il est à bord.
        if (EstClose)
        {
            return Result.Refus(
                RideErrorCodes.RideAlreadyClosed, "Cette course est déjà terminée.");
        }

        if (Status == RideStatus.InProgress)
        {
            return Result.Refus(
                RideErrorCodes.RideAlreadyStarted,
                "Une course commencée ne s'annule pas : elle se termine.");
        }

        var refus = Exiger(RideStatus.Cancelled);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Raise(new RideCancelled(Id, CancellationReason, RetenueDue(), actor, now));

        return Result.Ok();
    }

    /// <summary>
    /// Aucun chauffeur n'a répondu.
    /// </summary>
    ///
    /// <remarks>
    /// C'EST UN ÉTAT TERMINAL, DONC IL A SA MÉTHODE. Dans Delivery, ce cas
    /// tombait dans la branche par défaut de l'écran d'accueil et le client
    /// regardait une recherche déjà abandonnée, sans issue autre que tuer
    /// l'application. On ne refait pas la même chose ici.
    /// </remarks>
    public Result NoDriverFound(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var refus = Exiger(RideStatus.NoDriverAvailable);
        if (refus is not null)
        {
            return refus;
        }

        Status = RideStatus.NoDriverAvailable;
        Raise(new RideNoDriverAvailable(Id, actor, now));

        return Result.Ok();
    }

    /// <summary>Le refus de transition, ou null si elle est permise.</summary>
    private Result? Exiger(RideStatus vers)
        => PeutAller(Status, vers)
            ? null
            : Result.Refus(
                RideErrorCodes.InvalidTransition,
                $"Transition interdite sur Ride : {Status} -> {vers}.");
}
