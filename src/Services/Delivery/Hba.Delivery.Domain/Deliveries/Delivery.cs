using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries.Events;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// Agrégat central du service. Toute transition passe par une méthode d'ici, et
/// toute méthode commence par vérifier la table des transitions : il n'existe
/// aucun autre chemin pour changer l'état d'une livraison.
/// </summary>
public sealed class Delivery : AggregateRoot
{
    // Constructeur de matérialisation (EF Core).
    private Delivery()
    {
    }

    private Delivery(
        Guid id,
        string reference,
        DeliverySource source,
        string partnerId,
        string? externalOrderId,
        string? customerId,
        string? merchantId,
        string? pickupPointId,
        Location pickup,
        Location dropoff,
        Recipient recipient,
        PricingSnapshot pricing,
        string? packageDescription,
        int packageWeightGrams,
        DateTimeOffset createdAt)
        : base(id)
    {
        Reference = reference;
        Source = source;
        PartnerId = partnerId;
        ExternalOrderId = externalOrderId;
        CustomerId = customerId;
        MerchantId = merchantId;
        PickupPointId = pickupPointId;
        Pickup = pickup;
        Dropoff = dropoff;
        Recipient = recipient;
        Pricing = pricing;
        PackageDescription = packageDescription;
        PackageWeightGrams = packageWeightGrams;
        CreatedAt = createdAt;
        Status = DeliveryStatus.PendingPayment;
        Otp = DeliveryOtp.Generate();
    }

    public string Reference { get; private set; } = string.Empty;

    public DeliveryStatus Status { get; private set; }

    public DeliverySource Source { get; private set; }

    /// <summary>
    /// Cloisonnement multi-partenaires : toute donnée porte son PartnerId, et un
    /// partenaire ne voit jamais les livraisons d'un autre.
    /// </summary>
    public string PartnerId { get; private set; } = string.Empty;

    /// <summary>Identifiant de commande côté appelant. Unique par partenaire.</summary>
    public string? ExternalOrderId { get; private set; }

    /// <summary>Donneur d'ordre particulier. Exclusif avec <see cref="MerchantId"/>.</summary>
    public string? CustomerId { get; private set; }

    /// <summary>Donneur d'ordre entreprise.</summary>
    public string? MerchantId { get; private set; }

    public string? PickupPointId { get; private set; }

    public Location Pickup { get; private set; } = null!;

    public Location Dropoff { get; private set; } = null!;

    /// <summary>Destinataire figé à la demande. Ce n'est pas un compte.</summary>
    public Recipient Recipient { get; private set; } = null!;

    /// <summary>Prix figé. Aucune méthode ne le remplace : c'est volontaire.</summary>
    public PricingSnapshot Pricing { get; private set; } = null!;

    public AssignedDriver? Driver { get; private set; }

    public string? CurrentOfferId { get; private set; }

    public DeliveryOtp Otp { get; private set; } = null!;

    public string? PackageDescription { get; private set; }

    public int PackageWeightGrams { get; private set; }

    public string? PaymentIntentId { get; private set; }

    /// <summary>
    /// La photo de collecte, si le livreur en a joint une.
    /// </summary>
    ///
    /// <remarks>
    /// UN IDENTIFIANT DE MEDIA, PLUS UNE CLE DE STOCKAGE, et c'est le point 27
    /// qui l'impose : « la clé de stockage ne sort pas du service » Media. Un
    /// service qui garderait la clé finirait par parler au stockage en direct, et
    /// l'inventaire — celui qui permet la purge à trente jours et la suppression
    /// de compte — serait faux dès la première écriture qui l'aurait contourné.
    ///
    /// NUL TANT QUE RIEN N'EST DEPOSE, et cela reste le cas normal : la photo est
    /// proposée, jamais exigée.
    /// </remarks>
    public Guid? PickupProofMediaId { get; private set; }

    /// <summary>La photo de remise, même régime que celle de collecte.</summary>
    public Guid? DeliveryProofMediaId { get; private set; }

    public string? ClosureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? ArrivedAtPickupAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Instant où le remboursement du client a été CONSTATÉ. Nul tant qu'il n'y
    /// en a pas.
    /// </summary>
    ///
    /// <remarks>
    /// CONSTATÉ, ET NON DÉCLENCHÉ. FedaPay n'a pas d'API de remboursement
    /// (vérifié le 30 septembre 2026 : tableau de bord uniquement, MTN Mobile
    /// Money seulement). C'est `finance` qui rend l'argent à la main ; le système
    /// l'apprend par la relecture de la transaction chez le fournisseur.
    ///
    /// UN SEUL CHAMP, ET PAS DE MONTANT. Le montant rendu, quand il est total,
    /// est celui de la course ; quand il est partiel, le fournisseur ne le dit
    /// pas, et l'écrire au hasard serait pire que de ne rien écrire. Le drapeau
    /// ci-dessous porte la distinction.
    /// </remarks>
    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>Vrai quand le remboursement constaté est partiel.</summary>
    public bool RefundPartial { get; private set; }

    public bool IsClosed => DeliveryTransitions.Terminal.Contains(Status);

    /// <summary>
    /// Le payeur est le client lorsqu'il y en a un. Sinon, le règlement relève du
    /// commerçant ou du partenaire — deux cas encore à trancher, cf. docs/adr.
    /// </summary>
    public bool IsCustomerOrdered => !string.IsNullOrWhiteSpace(CustomerId);

    /// <summary>
    /// Crée une livraison. Le prix vient d'un devis Pricing déjà consommé : la
    /// livraison ne calcule jamais son prix elle-même.
    /// </summary>
    /// <summary>
    /// Le règlement d'une course B2B, par débit du compte du donneur d'ordre.
    /// </summary>
    ///
    /// <remarks>
    /// PRESENT VEUT DIRE « DEJA PAYEE ». Le débit a eu lieu chez Billing avant
    /// la création, de façon synchrone, pour qu'un donneur d'ordre au plafond
    /// l'apprenne avant que la course n'existe. L'identifiant porté ici est
    /// celui du MOUVEMENT comptable — c'est la pièce à présenter le jour où
    /// quelqu'un demande qui a payé cette course.
    /// </remarks>
    public sealed record AccountSettlement(string MovementId);

    public static Delivery Create(
        Guid id,
        string reference,
        DeliverySource source,
        string partnerId,
        string? externalOrderId,
        string? customerId,
        string? merchantId,
        string? pickupPointId,
        Location pickup,
        Location dropoff,
        Recipient recipient,
        PricingSnapshot pricing,
        string? packageDescription,
        int packageWeightGrams,
        Actor actor,
        DateTimeOffset createdAt,
        AccountSettlement? settlement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerId);
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(actor);

        var hasCustomer = !string.IsNullOrWhiteSpace(customerId);
        var hasMerchant = !string.IsNullOrWhiteSpace(merchantId);
        var hasExternalPartner = !string.Equals(partnerId, "hba-internal", StringComparison.Ordinal);

        // Le donneur d'ordre doit être identifiable : un client, un commerçant,
        // ou un partenaire B2B. Un client PEUT désigner un commerçant comme point
        // de collecte sans que celui-ci devienne donneur d'ordre : c'est
        // CustomerId qui, lorsqu'il est présent, désigne le payeur.
        if (!hasCustomer && !hasMerchant && !hasExternalPartner)
        {
            throw new DomainException(
                "MISSING_ORDERER",
                "Une livraison doit avoir un donneur d'ordre : client, commerçant ou partenaire.");
        }

        if (packageWeightGrams < 0)
        {
            throw new DomainException("INVALID_WEIGHT", "Le poids du colis ne peut pas être négatif.");
        }

        var delivery = new Delivery(
            id,
            reference,
            source,
            partnerId,
            externalOrderId,
            customerId,
            merchantId,
            pickupPointId,
            pickup,
            dropoff,
            recipient,
            pricing,
            packageDescription,
            packageWeightGrams,
            createdAt);

        delivery.Raise(new DeliveryCreated(
            delivery.Id,
            reference,
            source,
            customerId,
            merchantId,
            pricing.Total,
            actor,
            createdAt));

        // UNE COURSE DEJA REGLEE NE PASSE PAS PAR « EN ATTENTE DE PAIEMENT ».
        //
        // Le compte du donneur d'ordre a été débité AVANT cet appel : la course
        // n'a jamais attendu de paiement, et la faire naître PENDING_PAYMENT
        // pour la faire avancer dans la milliseconde écrirait dans l'audit un
        // état qui n'a jamais existé.
        //
        // L'EVENEMENT DE REGLEMENT EST DISTINCT DE « DeliveryConfirmed », ET
        // C'EST TOUT LE POINT. Les deux ouvrent la recherche d'un livreur, mais
        // ils ne disent pas la même chose : l'un veut dire « FedaPay a
        // encaissé », l'autre « le compte du commerçant a réglé ». Les
        // confondre mettrait un encaissement imaginaire dans la seule trace
        // qu'on relira le jour d'un litige.
        if (settlement is not null)
        {
            delivery.Status = DeliveryStatus.Paid;
            delivery.PaidAt = createdAt;

            delivery.Raise(new DeliverySettledByAccount(
                delivery.Id,
                settlement.MovementId,
                pricing.Total,
                actor,
                createdAt));
        }

        return delivery;
    }

    /// <summary>Rattache l'intention de paiement créée par le service Payment.</summary>
    public void AttachPaymentIntent(string paymentIntentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentIntentId);

        if (Status != DeliveryStatus.PendingPayment)
        {
            throw new InvalidStateTransitionException(
                nameof(Delivery),
                Status.ToString(),
                Status.ToString(),
                ActorKind.Scheduler);
        }

        PaymentIntentId = paymentIntentId;
    }

    /// <summary>
    /// Confirmation du paiement. Réservée au fournisseur de paiement : aucun
    /// autre acteur ne peut faire avancer cet état, et aucune recherche de
    /// livreur n'a lieu avant.
    /// </summary>
    public void ConfirmPayment(string paymentIntentId, Actor actor, DateTimeOffset paidAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == DeliveryStatus.Paid)
        {
            return; // Webhook rejoué : idempotent.
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.Paid, actor);

        Status = DeliveryStatus.Paid;
        PaymentIntentId = paymentIntentId;
        PaidAt = paidAt;

        Raise(new DeliveryConfirmed(Id, paymentIntentId, Pricing.Total, actor, paidAt));
    }

    public void FailPayment(string reason, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == DeliveryStatus.PaymentFailed)
        {
            return;
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.PaymentFailed, actor);

        Status = DeliveryStatus.PaymentFailed;
        ClosureReason = reason;
        CompletedAt = occurredAt;

        Raise(new DeliveryPaymentFailed(Id, reason, actor, occurredAt));
    }

    /// <summary>Le moteur de dispatch ouvre la recherche.</summary>
    public void StartDriverSearch(Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == DeliveryStatus.SearchingDriver)
        {
            return;
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.SearchingDriver, actor);

        Status = DeliveryStatus.SearchingDriver;
        Raise(new DriverSearchStarted(Id, actor, occurredAt));
    }

    /// <summary>
    /// Affecte le livreur qui a gagné l'offre. L'unicité du gagnant est garantie
    /// en amont par le verrou Dispatch ; ici, la concurrence optimiste sur
    /// <see cref="AggregateRoot.Version"/> rejette une seconde affectation.
    /// </summary>
    public void AssignDriver(AssignedDriver driver, string offerId, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(offerId);

        if (Status == DeliveryStatus.DriverAssigned && Driver?.DriverId == driver.DriverId)
        {
            return; // Événement rejoué.
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.DriverAssigned, actor);

        Status = DeliveryStatus.DriverAssigned;
        Driver = driver;
        CurrentOfferId = offerId;
        AssignedAt = occurredAt;

        Raise(new DriverAssigned(Id, driver.DriverId, offerId, actor, occurredAt));
    }

    /// <summary>Réaffectation forcée par ops : la livraison retourne en recherche.</summary>
    public void Unassign(string reason, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.SearchingDriver, actor);

        var previousDriverId = Driver?.DriverId ?? string.Empty;

        Status = DeliveryStatus.SearchingDriver;
        Driver = null;
        CurrentOfferId = null;
        AssignedAt = null;
        ArrivedAtPickupAt = null;

        Raise(new DriverUnassigned(Id, previousDriverId, reason, actor, occurredAt));
    }

    public void MarkNoDriverFound(int wavesAttempted, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == DeliveryStatus.NoDriverFound)
        {
            return;
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.NoDriverFound, actor);

        Status = DeliveryStatus.NoDriverFound;
        ClosureReason = "Aucun livreur disponible.";
        CompletedAt = occurredAt;

        Raise(new NoDriverFound(Id, wavesAttempted, actor, occurredAt));
    }

    /// <summary>Le livreur signale son arrivée au point de collecte.</summary>
    public void MarkArrivedAtPickup(Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureIsAssignedDriver(actor);

        if (Status == DeliveryStatus.DriverAtPickup)
        {
            return; // Action rejouée depuis la file hors connexion.
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.DriverAtPickup, actor);

        Status = DeliveryStatus.DriverAtPickup;
        ArrivedAtPickupAt = occurredAt;

        Raise(new DriverArrivedAtPickup(Id, Driver!.DriverId, actor, occurredAt));
    }

    /// <summary>
    /// Le livreur a le colis.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE NE PREND PLUS DE PREUVE, depuis le 30 septembre 2026. Elle en
    /// acceptait une dans le même appel, et rien ne vérifiait que la chaîne
    /// reçue désignait un objet existant : une preuve invérifiable n'en est pas
    /// une. La photo arrive maintenant par son propre chemin
    /// (<see cref="AttacherLaPreuve"/>), APRÈS l'étape — ce qui est aussi la
    /// seule façon de tenir la décision « l'étape ne l'attend jamais ».
    /// </remarks>
    public void MarkPickedUp(Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureIsAssignedDriver(actor);

        if (Status == DeliveryStatus.PickedUp)
        {
            return;
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.PickedUp, actor);

        Status = DeliveryStatus.PickedUp;
        PickedUpAt = occurredAt;

        Raise(new DeliveryPickedUp(Id, Driver!.DriverId, actor, occurredAt));
    }

    /// <summary>
    /// Remise au destinataire. Le code vient du destinataire, pas du système du
    /// livreur : sans OTP valide, la livraison ne peut pas passer en Delivered.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE NE LEVE PLUS SUR UN CODE FAUX, ET C'EST LA CORRECTION DU
    /// 30 SEPTEMBRE 2026. Elle incrémentait le compteur de tentatives puis levait
    /// « INVALID_OTP ». Or le gestionnaire n'appelle SaveChanges qu'après un
    /// « apply » réussi, et le Dispatcher n'ouvre aucune transaction :
    /// l'exception remontait, RIEN N'ETAIT ENREGISTRE, et le compteur repartait
    /// de zéro à la requête suivante.
    ///
    /// CONSEQUENCE : <see cref="ValueObjects.DeliveryOtp.IsLocked"/> n'était
    /// jamais vrai en production et OTP_LOCKED était du code mort. Un code de
    /// six chiffres, forçable en quelques milliers d'appels par le livreur
    /// affecté — qui n'avait donc plus besoin du destinataire pour clore la
    /// course, c'est-à-dire pour déclencher la facturation du donneur d'ordre et
    /// le crédit de sa propre part.
    ///
    /// LE TEST QUI COUVRAIT CE VERROU PASSAIT AU VERT, et c'est le plus
    /// instructif : il enchaînait cinq appels sur la MEME instance en mémoire,
    /// où rien n'est rechargé entre deux. Il vérifiait une propriété que le
    /// système n'avait pas. Un test d'intégration le prouve désormais à travers
    /// une vraie base — <c>Le_compteur_de_tentatives_survit_a_un_code_faux</c>.
    ///
    /// L'issue est donc RENDUE, pas levée : l'appelant enregistre, puis traduit.
    /// Ce qui reste levé — transition interdite, OTP_LOCKED — n'a produit aucune
    /// écriture à conserver.
    /// </remarks>
    public DeliveryConfirmation ConfirmDelivery(
        string otpCode,
        Actor actor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureIsAssignedDriver(actor);

        if (Status == DeliveryStatus.Delivered)
        {
            return DeliveryConfirmation.Confirmed;
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.Delivered, actor);

        var verification = Otp.Verify(otpCode);
        Otp = verification.Otp;

        if (!verification.Succeeded)
        {
            Raise(new DeliveryOtpAttemptFailed(Id, Driver!.DriverId, Otp.FailedAttempts, actor, occurredAt));
            return DeliveryConfirmation.OtpRefused;
        }

        Status = DeliveryStatus.Delivered;
        CompletedAt = occurredAt;

        Raise(new DeliveryCompleted(Id, Driver!.DriverId, Pricing.DriverEarning, actor, occurredAt));

        return DeliveryConfirmation.Confirmed;
    }

    /// <summary>Annulation selon la politique d'annulation.</summary>
    /// <summary>
    /// Un livreur a accepté une course qui venait de se clore : on le rend à la
    /// file. LA COURSE NE CHANGE PAS D'ÉTAT — elle est close et le reste.
    /// </summary>
    ///
    /// <remarks>
    /// LA FENÊTRE EST D'UNE SECONDE, ET ELLE IMMOBILISAIT UN LIVREUR POUR
    /// TOUJOURS.
    ///
    /// Entre l'annulation d'une course et sa prise en compte par Dispatch, il
    /// s'écoule le temps d'un tour d'Outbox. Un livreur qui accepte dans cet
    /// intervalle passe EN MISSION chez Driver — c'est l'acceptation qui le dit,
    /// et elle a réussi. Delivery, lui, refusait l'affectation sur un état
    /// terminal : l'événement échouait trois fois puis était abandonné, et plus
    /// rien ne venait jamais libérer ce livreur. Il ne pouvait ni se mettre hors
    /// ligne — la méthode refuse depuis « en mission » — ni recevoir d'offre.
    /// Réparation en base uniquement.
    ///
    /// LE FAIT LEVÉ EST « DriverUnassigned », QUI EXISTAIT DÉJÀ et ne servait à
    /// personne. Le publieur le traduit sur le fil en « DeliveryCancelled »
    /// portant l'identifiant du livreur — exactement le message que Driver sait
    /// déjà consommer pour mettre fin à une mission.
    ///
    /// ON NE TOUCHE PAS À L'ÉTAT, ET C'EST TOUT L'INTÉRÊT : une course close ne
    /// se rouvre pas pour corriger une course de vitesse. On constate, on
    /// libère, on n'écrit rien d'autre.
    /// </remarks>
    public void ReleaseLateAcceptance(string driverId, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);

        if (!IsClosed)
        {
            throw new DomainException(
                "DELIVERY_NOT_CLOSED",
                "Cette course n'est pas close : le livreur s'affecte normalement.");
        }

        Raise(new DriverUnassigned(
            Id,
            driverId,
            "Course déjà close au moment de l'acceptation.",
            actor,
            occurredAt));
    }

    public void Cancel(string reason, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.Cancelled, actor);

        var previous = Status;
        Status = DeliveryStatus.Cancelled;
        ClosureReason = reason;
        CompletedAt = occurredAt;

        Raise(new DeliveryCancelled(Id, previous, reason, actor, occurredAt));
    }

    /// <summary>
    /// Le livreur constate que la course ne peut pas aboutir.
    /// </summary>
    ///
    /// <remarks>
    /// TRANCHÉ LE 29 SEPTEMBRE 2026, ET C'EST UN ÉLARGISSEMENT DE PÉRIMÈTRE
    /// ASSUMÉ : le référentiel acteurs marque l'incident « hors MVP ». Ce qu'il
    /// ne disait pas, c'est ce que devenait le livreur sans lui. Depuis
    /// PICKED_UP, la seule transition qui lui était ouverte était « remise » :
    /// destinataire absent, adresse fausse ou code bloqué après cinq essais, il
    /// gardait le colis et restait EN MISSION — donc sans pouvoir se mettre hors
    /// ligne ni recevoir la moindre offre — jusqu'à ce qu'un ops clôture à sa
    /// place. Un cas par jour immobilisait un livreur pour la journée.
    ///
    /// LE MOTIF EST UN TEXTE LIBRE, ET CE N'EST PAS DE LA PARESSE. Aucune liste
    /// d'incidents n'existe dans le référentiel ; en inventer une ici la
    /// graverait dans le contrat et dans la base avant que quiconque l'ait
    /// décidée. L'application propose des formulations courantes, le domaine
    /// enregistre ce qui remonte, et la liste se fixera quand on aura vu les
    /// vrais cas. Voir points-a-trancher.
    ///
    /// L'ÉTAT VISÉ EST « FAILED », JAMAIS « CANCELLED ». Une course annulée n'a
    /// pas eu lieu ; celle-ci a eu lieu et a échoué, le colis est quelque part,
    /// et le remboursement comme le sort du colis n'obéissent pas aux mêmes
    /// règles. Confondre les deux rendrait la statistique inutilisable.
    ///
    /// LE LIVREUR EST LIBÉRÉ PAR L'ÉVÉNEMENT, comme pour toute fin de course :
    /// « DeliveryFailed » porte son identifiant et Driver met fin à la mission.
    /// </remarks>
    public void DeclareIncident(string reason, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (actor.Kind != ActorKind.Driver)
        {
            throw new ForbiddenException("Seul le livreur affecté déclare un incident sur sa course.");
        }

        if (Driver is null || Driver.DriverId != actor.Id)
        {
            throw new ForbiddenException("Cette course n'est pas la vôtre.");
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.Failed, actor);

        var previous = Status;
        Status = DeliveryStatus.Failed;
        ClosureReason = reason;
        CompletedAt = occurredAt;

        Raise(new DeliveryFailed(Id, previous, reason, actor, occurredAt));
    }

    /// <summary>
    /// Le client a été remboursé. ON LE CONSTATE, L'ÉTAT NE CHANGE PAS.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUNE TRANSITION, ET C'EST TOUT L'INTÉRÊT. Une course remboursée reste
    /// annulée, échouée ou sans livreur : c'est ce qu'elle est. Le remboursement
    /// est un fait de paiement, pas un état de livraison — lui donner un statut
    /// obligerait chaque écran et chaque statistique à connaître une valeur de
    /// plus pour dire la même chose.
    ///
    /// LE CLIENT EN A BESOIN, ET C'EST POUR LUI QUE CE CHAMP EXISTE. L'écran lui
    /// disait « HBA revient vers vous au sujet du montant prélevé » sans jamais
    /// pouvoir lui dire que c'était fait.
    ///
    /// REJOUABLE : le webhook du fournisseur est rejoué, et un second passage ne
    /// doit ni échouer ni réécrire la date.
    /// </remarks>
    public void MarkRefunded(bool partial, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (RefundedAt is not null)
        {
            return;
        }

        RefundedAt = occurredAt;
        RefundPartial = partial;

        Raise(new DeliveryRefunded(Id, partial, actor, occurredAt));
    }

    /// <summary>
    /// Clôture administrative. Un administrateur ne peut clore qu'en Failed ou
    /// Cancelled : il ne peut jamais livrer à la place du livreur.
    /// </summary>
    public void AdminClose(DeliveryStatus targetStatus, string reason, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (actor.Kind != ActorKind.Admin)
        {
            throw new ForbiddenException("Seul un acteur du back-office peut clore une livraison de force.");
        }

        if (targetStatus is not (DeliveryStatus.Failed or DeliveryStatus.Cancelled))
        {
            throw new ForbiddenException(
                "Une clôture administrative ne peut viser que Failed ou Cancelled. "
                + "Marquer une livraison comme remise appartient au seul livreur, avec l'OTP.");
        }

        DeliveryTransitions.EnsureAllowed(Status, targetStatus, actor);

        var previous = Status;
        Status = targetStatus;
        ClosureReason = reason;
        CompletedAt = occurredAt;

        if (targetStatus == DeliveryStatus.Cancelled)
        {
            Raise(new DeliveryCancelled(Id, previous, reason, actor, occurredAt));
        }
        else
        {
            Raise(new DeliveryFailed(Id, previous, reason, actor, occurredAt));
        }
    }

    /// <summary>
    /// Le livreur ne voit l'adresse exacte de destination qu'après acceptation.
    /// Tant qu'il n'est pas affecté, il n'a droit qu'au repère de collecte.
    /// </summary>
    public bool CanRevealDropoffTo(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Kind != ActorKind.Driver)
        {
            return true;
        }

        // Affecté : il a accepté, donc il a besoin de l'adresse. Sinon, non.
        return Driver is not null && Driver.DriverId == actor.Id;
    }

    /// <summary>
    /// Fenêtre pendant laquelle une photo peut encore être rattachée à l'étape
    /// qui vient de se produire.
    /// </summary>
    ///
    /// <remarks>
    /// TRENTE MINUTES, TRANCHÉ LE 30 SEPTEMBRE 2026 (point 7). Sans borne, un
    /// livreur joindrait une photo à une course finie il y a trois semaines, et
    /// cette photo passerait pour la preuve d'un geste qu'elle n'a pas vu.
    ///
    /// CE QUE CES TRENTE MINUTES PAIENT, ET C'EST DEUX CHOSES DISTINCTES :
    /// <list type="bullet">
    /// <item>L'ENVOI LUI-MÊME, six minutes dans le pire cas réel : le délai
    /// d'envoi est de trois minutes, et un jeton expiré ajoute une seconde
    /// tentative après rafraîchissement (voir <c>ApiClient.upload</c>).</item>
    /// <item>LE RESTE ABSORBE UNE HORLOGE FAUSSE, et c'est la vraie raison du
    /// chiffre. Voir ci-dessous.</item>
    /// </list>
    ///
    /// ATTENTION — CETTE SOUSTRACTION MÊLE DEUX HORLOGES, ET C'EST ASSUMÉ.
    /// <c>PickedUpAt</c> et <c>CompletedAt</c> portent l'horodatage du TÉLÉPHONE
    /// : <c>NormalizeTimestamp</c> l'accepte tel quel jusqu'à vingt-quatre heures
    /// dans le passé, parce que l'application met ses étapes en file quand le
    /// réseau tombe. <c>occurredAt</c>, lui, vient du serveur. Un téléphone qui
    /// retarde de vingt minutes — courant sur un appareil d'entrée de gamme sans
    /// réglage automatique — consomme donc vingt des trente minutes AVANT que le
    /// livreur ait touché son appareil photo.
    ///
    /// CE QUI RESTE POSSIBLE, ET QUI A ÉTÉ PESÉ : un téléphone qui retarde de
    /// plus d'une demi-heure refusera toujours la photo, avec un message que le
    /// livreur ne pourra pas relier à sa cause. La réparation propre — un
    /// instant posé par le SERVEUR à chaque étape — coûte deux colonnes et une
    /// migration, pour une pièce qui reste facultative ; elle a été écartée à ce
    /// prix-là, pas par oubli. Si le refus se voit en exploitation, c'est ce
    /// chantier-là qu'il faut ouvrir, pas ce nombre qu'il faut augmenter.
    ///
    /// L'AUTRE BOUT DU MARCHÉ : une photo prise vingt-cinq minutes après la
    /// remise est acceptée comme si elle montrait l'instant. C'est le prix de la
    /// tolérance ci-dessus, et il est supportable parce que la photo n'est PAS
    /// ce qui fait foi — l'ADR 0005 donne ce rôle au code de remise.
    /// </remarks>
    public static readonly TimeSpan FenetreDeDepotDeLaPreuve = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Rattache une photo à la collecte ou à la remise.
    /// </summary>
    ///
    /// <remarks>
    /// APPELEE PAR LE SERVICE, APRES QUE MEDIA A ACCEPTE LE FICHIER. C'est le
    /// sens de « Delivery porte les octets » (point 7, question 4, tranchée le
    /// 30 septembre 2026) : le livreur envoie sa photo à Delivery, qui vérifie
    /// ICI qu'il est bien le livreur affecté, puis la pousse chez Media avec un
    /// jeton de service. Media ne sait pas qui est affecté à quelle course, et
    /// n'a pas à le savoir.
    ///
    /// L'IDENTIFIANT RECU DESIGNE UN FICHIER QUI EXISTE, et c'est toute la
    /// différence avec ce qui était refusé jusqu'ici sous « PROOF_NOT_SUPPORTED »
    /// : la clé arrivait du téléphone et ne nommait rien. Celle-ci est rendue par
    /// Media, qui vient de l'écrire.
    ///
    /// TROIS REFUS, ET CHACUN DIT QUELQUE CHOSE :
    /// <list type="bullet">
    /// <item>l'étape n'a pas eu lieu — une preuve de ce qui n'est pas arrivé
    /// n'est pas une preuve ;</item>
    /// <item>une preuve est déjà là — elle ne se remplace pas, sans quoi le
    /// livreur choisirait laquelle raconte l'histoire ;</item>
    /// <item>l'étape est trop ancienne — voir
    /// <see cref="FenetreDeDepotDeLaPreuve"/>.</item>
    /// </list>
    ///
    /// LE MEME IDENTIFIANT DEUX FOIS NE LEVE PAS : le téléphone qui n'a pas reçu
    /// la réponse renvoie, et il ne doit pas lire un échec là où tout s'est bien
    /// passé. Un identifiant DIFFERENT, lui, est bien un remplacement.
    /// </remarks>
    public void AttacherLaPreuve(
        EtapeDeLaPreuve etape,
        Guid mediaId,
        Actor actor,
        DateTimeOffset occurredAt)
    {
        if (mediaId == Guid.Empty)
        {
            throw new DomainException(
                "PROOF_MEDIA_REQUIRED",
                "Aucun média n'est désigné : il n'y a rien à rattacher.");
        }

        var deja = EnsurePeutRecevoirLaPreuve(etape, actor, occurredAt);

        if (deja is not null)
        {
            // LE MEME IDENTIFIANT DEUX FOIS EST UN REJEU, pas un remplacement :
            // le telephone qui n'a pas recu la reponse renvoie, et il ne doit
            // pas lire un echec la ou tout s'est bien passe.
            if (deja == mediaId)
            {
                return;
            }

            throw new DomainException(
                "PROOF_ALREADY_ATTACHED",
                "Une preuve est déjà jointe à cette étape, et elle ne se remplace pas.");
        }

        if (etape == EtapeDeLaPreuve.Collecte)
        {
            PickupProofMediaId = mediaId;
        }
        else
        {
            DeliveryProofMediaId = mediaId;
        }

        Raise(new PreuveAttachee(Id, Driver!.DriverId, etape, mediaId, actor, occurredAt));
    }

    /// <summary>
    /// Toutes les raisons de refuser une preuve, SANS rien modifier.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE EXISTE POUR ETRE APPELEE AVANT LE DEPOT, et c'est une question
    /// d'octets autant que de justesse : sans elle, le service enverrait la
    /// photo à Media, attendrait son identifiant, puis découvrirait que l'étape
    /// n'a pas eu lieu — et laisserait derrière lui un fichier que plus rien ne
    /// réclame. Le livreur, lui, aurait payé ses données pour un refus.
    ///
    /// <c>AttacherLaPreuve</c> LA RAPPELLE QUAND MEME. Entre le contrôle et le
    /// dépôt, la course a pu bouger ; c'est l'agrégat qui tranche en dernier,
    /// jamais le contrôle avancé.
    /// </remarks>
    /// <returns>
    /// La preuve DEJA rattachée à cette étape, ou <c>null</c> si la place est
    /// libre. Ce n'est pas une erreur ici : seul l'appelant sait si
    /// l'identifiant qu'il apporte est le même — auquel cas c'est un rejeu — ou
    /// un autre, auquel cas c'est un remplacement, et un remplacement est refusé.
    /// </returns>
    public Guid? EnsurePeutRecevoirLaPreuve(
        EtapeDeLaPreuve etape,
        Actor actor,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureIsAssignedDriver(actor);

        var (deja, faiteA) = etape switch
        {
            EtapeDeLaPreuve.Collecte => (PickupProofMediaId, PickedUpAt),

            // LE STATUT, ET NON « CompletedAt » : cet horodatage est aussi posé
            // par une annulation et par un echec. Une course annulee a donc une
            // date de cloture, et sans cette condition elle accepterait une
            // « preuve de remise » pour une remise qui n'a jamais eu lieu.
            EtapeDeLaPreuve.Remise => (
                DeliveryProofMediaId,
                Status == DeliveryStatus.Delivered ? CompletedAt : null),

            _ => throw new DomainException(
                "PROOF_STEP_UNKNOWN",
                "Étape de preuve inconnue."),
        };

        if (faiteA is null)
        {
            throw new DomainException(
                "PROOF_STEP_NOT_DONE",
                "L'étape n'a pas encore eu lieu : il n'y a rien à prouver.");
        }

        if (occurredAt - faiteA.Value > FenetreDeDepotDeLaPreuve)
        {
            throw new DomainException(
                "PROOF_TOO_LATE",
                "L'étape est trop ancienne : une photo jointe maintenant ne "
                + "montrerait plus le moment qu'elle prétend prouver.");
        }

        return deja;
    }

    private void EnsureIsAssignedDriver(Actor actor)
    {
        if (actor.Kind != ActorKind.Driver)
        {
            throw new ForbiddenException("Cette action appartient au livreur affecté.");
        }

        if (Driver is null || Driver.DriverId != actor.Id)
        {
            throw new ForbiddenException("Ce livreur n'est pas affecté à cette livraison.");
        }
    }
}
