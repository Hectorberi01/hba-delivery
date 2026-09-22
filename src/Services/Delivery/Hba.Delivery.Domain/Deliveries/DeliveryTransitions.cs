using Hba.BuildingBlocks.Domain;

namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// Table des transitions autorisées, avec l'acteur qui a le droit de les
/// provoquer. C'est la référence unique : l'agrégat ne change jamais d'état sans
/// passer par <see cref="EnsureAllowed"/>.
/// </summary>
public static class DeliveryTransitions
{
    public readonly record struct Transition(DeliveryStatus From, DeliveryStatus To, ActorKind Actor);

    private static readonly HashSet<Transition> Allowed =
    [
        // Paiement — seul FedaPay, par webhook vérifié, fait avancer cet état.
        new(DeliveryStatus.PendingPayment, DeliveryStatus.Paid, ActorKind.PaymentProvider),
        new(DeliveryStatus.PendingPayment, DeliveryStatus.PaymentFailed, ActorKind.PaymentProvider),
        new(DeliveryStatus.PendingPayment, DeliveryStatus.PaymentFailed, ActorKind.Scheduler),

        // Annulation avant paiement.
        new(DeliveryStatus.PendingPayment, DeliveryStatus.Cancelled, ActorKind.Customer),
        new(DeliveryStatus.PendingPayment, DeliveryStatus.Cancelled, ActorKind.Merchant),
        new(DeliveryStatus.PendingPayment, DeliveryStatus.Cancelled, ActorKind.Partner),
        new(DeliveryStatus.PendingPayment, DeliveryStatus.Cancelled, ActorKind.Admin),
        new(DeliveryStatus.PendingPayment, DeliveryStatus.Cancelled, ActorKind.Scheduler),

        // Dispatch — le moteur seul ouvre et ferme la recherche.
        new(DeliveryStatus.Paid, DeliveryStatus.SearchingDriver, ActorKind.Dispatch),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.DriverAssigned, ActorKind.Dispatch),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.NoDriverFound, ActorKind.Dispatch),

        // Réaffectation forcée par ops : on retourne en recherche.
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.SearchingDriver, ActorKind.Admin),
        new(DeliveryStatus.DriverAtPickup, DeliveryStatus.SearchingDriver, ActorKind.Admin),

        // Exécution — le livreur seul.
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.DriverAtPickup, ActorKind.Driver),
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.PickedUp, ActorKind.Driver),
        new(DeliveryStatus.DriverAtPickup, DeliveryStatus.PickedUp, ActorKind.Driver),
        new(DeliveryStatus.PickedUp, DeliveryStatus.Delivered, ActorKind.Driver),

        // Annulation après paiement, avant collecte.
        new(DeliveryStatus.Paid, DeliveryStatus.Cancelled, ActorKind.Customer),
        new(DeliveryStatus.Paid, DeliveryStatus.Cancelled, ActorKind.Merchant),
        new(DeliveryStatus.Paid, DeliveryStatus.Cancelled, ActorKind.Partner),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.Cancelled, ActorKind.Customer),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.Cancelled, ActorKind.Merchant),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.Cancelled, ActorKind.Partner),
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.Cancelled, ActorKind.Customer),
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.Cancelled, ActorKind.Merchant),
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.Cancelled, ActorKind.Partner),

        // Clôture administrative : FAILED ou CANCELLED, jamais DELIVERED.
        new(DeliveryStatus.Paid, DeliveryStatus.Cancelled, ActorKind.Admin),
        new(DeliveryStatus.Paid, DeliveryStatus.Failed, ActorKind.Admin),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.Cancelled, ActorKind.Admin),
        new(DeliveryStatus.SearchingDriver, DeliveryStatus.Failed, ActorKind.Admin),
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.Cancelled, ActorKind.Admin),
        new(DeliveryStatus.DriverAssigned, DeliveryStatus.Failed, ActorKind.Admin),
        new(DeliveryStatus.DriverAtPickup, DeliveryStatus.Cancelled, ActorKind.Admin),
        new(DeliveryStatus.DriverAtPickup, DeliveryStatus.Failed, ActorKind.Admin),
        new(DeliveryStatus.PickedUp, DeliveryStatus.Cancelled, ActorKind.Admin),
        new(DeliveryStatus.PickedUp, DeliveryStatus.Failed, ActorKind.Admin),
    ];

    /// <summary>États depuis lesquels plus aucune transition n'est possible.</summary>
    public static readonly IReadOnlySet<DeliveryStatus> Terminal = new HashSet<DeliveryStatus>
    {
        DeliveryStatus.PaymentFailed,
        DeliveryStatus.NoDriverFound,
        DeliveryStatus.Delivered,
        DeliveryStatus.Cancelled,
        DeliveryStatus.Failed,
    };

    public static bool IsAllowed(DeliveryStatus from, DeliveryStatus to, ActorKind actor)
        => Allowed.Contains(new Transition(from, to, actor));

    /// <summary>Transitions possibles depuis un état, tous acteurs confondus.</summary>
    public static IEnumerable<Transition> From(DeliveryStatus status)
        => Allowed.Where(t => t.From == status);

    public static void EnsureAllowed(DeliveryStatus from, DeliveryStatus to, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Terminal.Contains(from))
        {
            throw new InvalidStateTransitionException(nameof(Delivery), from.ToString(), to.ToString(), actor.Kind);
        }

        if (!IsAllowed(from, to, actor.Kind))
        {
            throw new InvalidStateTransitionException(nameof(Delivery), from.ToString(), to.ToString(), actor.Kind);
        }
    }
}
