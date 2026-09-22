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

    public string? PickupProofObjectKey { get; private set; }

    public string? DeliveryProofObjectKey { get; private set; }

    public string? ClosureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? ArrivedAtPickupAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

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
        DateTimeOffset createdAt)
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

    public void MarkPickedUp(string? proofObjectKey, Actor actor, DateTimeOffset occurredAt)
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
        PickupProofObjectKey = proofObjectKey;

        Raise(new DeliveryPickedUp(Id, Driver!.DriverId, proofObjectKey, actor, occurredAt));
    }

    /// <summary>
    /// Remise au destinataire. Le code vient du destinataire, pas du système du
    /// livreur : sans OTP valide, la livraison ne peut pas passer en Delivered.
    /// </summary>
    public void ConfirmDelivery(string otpCode, string? proofObjectKey, Actor actor, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureIsAssignedDriver(actor);

        if (Status == DeliveryStatus.Delivered)
        {
            return;
        }

        DeliveryTransitions.EnsureAllowed(Status, DeliveryStatus.Delivered, actor);

        var verification = Otp.Verify(otpCode);
        Otp = verification.Otp;

        if (!verification.Succeeded)
        {
            Raise(new DeliveryOtpAttemptFailed(Id, Driver!.DriverId, Otp.FailedAttempts, actor, occurredAt));
            throw new DomainException("INVALID_OTP", "Code de remise incorrect.");
        }

        Status = DeliveryStatus.Delivered;
        CompletedAt = occurredAt;
        DeliveryProofObjectKey = proofObjectKey;

        Raise(new DeliveryCompleted(Id, Driver!.DriverId, Pricing.DriverEarning, proofObjectKey, actor, occurredAt));
    }

    /// <summary>Annulation selon la politique d'annulation.</summary>
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
