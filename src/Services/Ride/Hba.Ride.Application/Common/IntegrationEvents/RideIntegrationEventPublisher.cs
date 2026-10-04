using System.Diagnostics;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Notification.V1;
using Hba.Contracts.Ride.V1;
using DomainEvents = Hba.Ride.Domain.Rides.Events;
using DomainMoney = Hba.Ride.Domain.ValueObjects.MoneyXof;
using DomainVehicle = Hba.Ride.Domain.Rides.VehicleType;
using RideEntity = Hba.Ride.Domain.Rides.RideAggregate;

namespace Hba.Ride.Application.Common.IntegrationEvents;

/// <summary>
/// Traduit les faits du domaine Ride en messages Kafka et les dépose dans
/// l'Outbox.
/// </summary>
///
/// <remarks>
/// TOUS LES FAITS NE SORTENT PAS, ET C'EST LA RÈGLE DU DÉPÔT. Sept des neuf
/// événements de la course ont un consommateur identifié ; <c>RideRequested</c>
/// et <c>RideStarted</c> n'en ont pas, et un message que personne ne lit est une
/// promesse de compatibilité à tenir pour rien. Le contrat les laisse entrer
/// plus tard en champs 19 et 20.
/// </remarks>
public interface IRideIntegrationEventPublisher
{
    void Publish(IOutbox outbox, RideEntity ride, IDomainEvent domainEvent);
}

/// L'Outbox est passée en paramètre plutôt qu'injectée : EfOutbox écrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de résoudre.
public sealed class RideIntegrationEventPublisher : IRideIntegrationEventPublisher
{
    private const string Producer = "ride";
    private const string AggregateType = "ride";

    public void Publish(IOutbox outbox, RideEntity ride, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(ride);
        ArgumentNullException.ThrowIfNull(domainEvent);

        // LES NOTIFICATIONS PARTENT D'ABORD, ET SUR UN AUTRE TOPIC. Elles ne sont
        // pas conditionnees par le fait qu'un evenement sorte vers hba.ride.events :
        // « paiement echoue » reste interne au service, et le passager doit pourtant
        // l'apprendre. C'est la forme qu'a deja Delivery.
        Notifier(outbox, ride, domainEvent);

        var message = Map(ride, domainEvent);
        if (message is null)
        {
            return;
        }

        outbox.Enqueue(
            KafkaTopics.RideEvents,
            ride.Id.ToString(),
            message.Envelope.EventType,
            message);
    }

    private static RideEvent? Map(RideEntity ride, IDomainEvent domainEvent) => domainEvent switch
    {
        // LE TYPE DE VÉHICULE ET LES DEUX POINTS VOYAGENT AVEC LA RECHERCHE.
        // Dispatch en a besoin pour composer ses vagues, et un consommateur qui
        // doit rappeler l'émetteur pour agir n'est plus découplé.
        DomainEvents.RideDriverSearchStarted e => Wrap(ride, e, "hba.ride.v1.RideDriverSearchStarted", m => m.SearchStarted = new RideDriverSearchStarted
        {
            RideId = e.RideId.ToString(),
            CustomerId = ride.CustomerId,
            VehicleType = MapVehicle(ride.VehicleType),
            Pickup = Point(ride.Pickup.Latitude, ride.Pickup.Longitude),
            Destination = Point(ride.Destination.Latitude, ride.Destination.Longitude),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
            DistanceMeters = ride.Estimate.DistanceMeters,

            // SANS CE MONTANT, L'OFFRE DU CHAUFFEUR AFFICHE ZERO FRANC. Personne
            // n'accepte une course a zero : le dispatch aurait tourne a vide sans
            // qu'aucun service ne soit en panne, et c'est le genre de panne qu'on
            // ne trouve qu'en lisant le code.
            DriverEarning = Montant(ride.DriverEarning.Amount),
        }),

        DomainEvents.RideDriverAssigned e => Wrap(ride, e, "hba.ride.v1.RideDriverAssigned", m => m.DriverAssigned = new RideDriverAssigned
        {
            RideId = e.RideId.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.RideDriverArriving e => Wrap(ride, e, "hba.ride.v1.RideDriverArriving", m => m.DriverArriving = new RideDriverArriving
        {
            RideId = e.RideId.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.RideDriverArrived e => Wrap(ride, e, "hba.ride.v1.RideDriverArrived", m => m.DriverArrived = new RideDriverArrived
        {
            RideId = e.RideId.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        // LE MONTANT EST PORTÉ PARCE QUE BILLING EN VIT : la commission se
        // calcule dessus, et l'obliger à rappeler Ride pour chaque course
        // terminée en ferait un consommateur couplé à notre disponibilité.
        DomainEvents.RideCompleted e => Wrap(ride, e, "hba.ride.v1.RideCompleted", m => m.Completed = new RideCompleted
        {
            RideId = e.RideId.ToString(),
            CustomerId = ride.CustomerId,
            DriverId = e.DriverId,
            FinalPrice = Montant(e.FinalPrice.Amount),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),

            // LA PART DU CHAUFFEUR, LUE SUR L'AGREGAT. L'evenement du domaine ne la
            // porte pas — il porte le prix final — et c'est elle qu'il faut pour
            // crediter le grand livre. Les confondre donnerait au chauffeur la
            // totalite de la course.
            DriverEarning = Montant(ride.DriverEarning.Amount),
        }),

        // LE CHAUFFEUR EST PORTÉ, VIDE S'IL N'Y EN AVAIT PAS. C'est lui qu'il
        // faut remettre en ligne : sans ce champ, Driver devrait interroger Ride
        // pour savoir s'il y avait quelqu'un à libérer. L'événement du domaine ne
        // le porte pas — il est lu sur l'agrégat, qui vient d'être annulé et le
        // connaît encore.
        DomainEvents.RideCancelled e => Wrap(ride, e, "hba.ride.v1.RideCancelled", m => m.Cancelled = new RideCancelled
        {
            RideId = e.RideId.ToString(),
            CustomerId = ride.CustomerId,
            DriverId = ride.DriverId ?? string.Empty,
            Reason = e.Reason ?? string.Empty,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),

            // LA RETENUE VIENT DE L'EVENEMENT, PAS DE L'AGREGAT, et la nuance
            // compte : c'est le domaine qui a decide qu'elle est nulle sans
            // chauffeur affecte. La relire dans ride.CancellationFee republierait
            // le montant de la grille meme quand personne ne s'est deplace, et
            // Payment crediterait un chauffeur qui n'existe pas.
            CancellationFee = Montant(e.CancellationFeeXof),
        }),

        DomainEvents.RideNoDriverAvailable e => Wrap(ride, e, "hba.ride.v1.RideNoDriverAvailable", m => m.NoDriverAvailable = new RideNoDriverAvailable
        {
            RideId = e.RideId.ToString(),
            CustomerId = ride.CustomerId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        _ => null,
    };

    /// <summary>
    /// Depose les demandes d'envoi au passager.
    /// </summary>
    ///
    /// <remarks>
    /// LE NUMERO N'EST PAS ICI, ET C'EST DELIBERE. Ride ne connait que le compte du
    /// passager ; son telephone vit dans Directory. Aller le chercher mettrait un
    /// appel reseau dans la transaction qui affecte un chauffeur — Directory
    /// injoignable, et plus aucune course ne pourrait demarrer. C'est Notification qui
    /// resout le numero au moment d'envoyer, par « subject_id ».
    ///
    /// QUATRE FAITS SUR NEUF DECLENCHENT UN MESSAGE. L'affectation et l'arrivee parce
    /// que le passager attend dehors ; l'echec de paiement et l'absence de chauffeur
    /// parce que sans message il regarderait un sablier. Le depart et la fin, lui, il
    /// les vit — un SMS « votre course a commence » ne dit rien a quelqu'un qui est
    /// assis dans le vehicule. La fin produit un recu, par courriel.
    /// </remarks>
    private static void Notifier(IOutbox outbox, RideEntity ride, IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case DomainEvents.RideDriverAssigned when ride.Driver is { } affecte:
                Sms(outbox, ride, domainEvent, 0x11, "ride_driver_assigned", new()
                {
                    ["driver"] = Borner(affecte.DisplayName, 40),
                    ["vehicule"] = Borner(affecte.Vehicule(), 36),
                    ["plaque"] = Borner(affecte.VehiclePlate, 16),
                    ["phone"] = Borner(affecte.Phone, 18),
                });
                break;

            case DomainEvents.RideDriverArrived when ride.Driver is { } surPlace:
                Sms(outbox, ride, domainEvent, 0x12, "ride_driver_arrived", new()
                {
                    ["driver"] = Borner(surPlace.DisplayName, 60),
                    ["vehicule"] = Borner(surPlace.Vehicule(), 60),
                });
                break;

            case DomainEvents.RidePaymentFailed:
                Sms(outbox, ride, domainEvent, 0x13, "ride_payment_failed", new());
                break;

            case DomainEvents.RideNoDriverAvailable:
                Sms(outbox, ride, domainEvent, 0x14, "ride_no_driver", new());
                break;

            case DomainEvents.RideCompleted termine:
                Recu(outbox, ride, termine);
                break;

            default:
                break;
        }
    }

    private static void Sms(
        IOutbox outbox,
        RideEntity ride,
        IDomainEvent domainEvent,
        byte marque,
        string modele,
        Dictionary<string, string> variables)
    {
        var commande = new NotificationCommand
        {
            Envelope = EnveloppeDeCommande(ride, domainEvent, "hba.notification.v1.SendSms", marque),
            SendSms = new SendSms
            {
                // LE COMPTE, PAS LE NUMERO : Notification le resout chez Directory.
                SubjectId = ride.CustomerId,
                TemplateId = modele,
            },
        };

        commande.SendSms.Variables.Add(variables);

        // CLE DE PARTITION : LA COURSE. L'ordre qui compte est celui des messages
        // d'UNE MEME course — « un chauffeur arrive » avant « il est sur place ».
        outbox.Enqueue(
            KafkaTopics.NotificationCommands,
            ride.Id.ToString(),
            commande.Envelope.EventType,
            commande);
    }

    private static void Recu(IOutbox outbox, RideEntity ride, DomainEvents.RideCompleted termine)
    {
        var commande = new NotificationCommand
        {
            Envelope = EnveloppeDeCommande(ride, termine, "hba.notification.v1.SendEmail", 0x15),
            SendEmail = new SendEmail
            {
                SubjectId = ride.CustomerId,
                TemplateId = "ride_receipt",
            },
        };

        commande.SendEmail.Variables.Add("date", Jour(termine.OccurredAt));
        commande.SendEmail.Variables.Add("driver", ride.Driver?.DisplayName ?? "-");
        commande.SendEmail.Variables.Add("vehicule", ride.Driver?.Vehicule() ?? "-");
        commande.SendEmail.Variables.Add("montant", MontantLisible(termine.FinalPrice.Amount));

        // CLE DE PARTITION : LE PASSAGER, ET NON LA COURSE. Deux recus du meme
        // passager arrivent dans l'ordre ou ses courses se sont terminees.
        outbox.Enqueue(
            KafkaTopics.NotificationCommands,
            ride.CustomerId,
            commande.Envelope.EventType,
            commande);
    }

    /// <summary>
    /// Borne une variable de SMS.
    /// </summary>
    ///
    /// <remarks>
    /// PARCE QU'UN SMS RENDU AU-DELA DE 160 CARACTERES FAIT ECHOUER L'ENVOI, pas
    /// seulement coûter deux segments : MessageTemplate.Render leve
    /// TEMPLATE_TOO_LONG, le handler releve, et l'Inbox rejoue le message
    /// indefiniment. Or le nom d'un chauffeur et la marque d'un vehicule sont du
    /// texte libre — Driver les borne a 120 et 64 caracteres, ce qui suffit a faire
    /// exploser un SMS a lui seul.
    ///
    /// ON TRONQUE PLUTOT QUE DE NE RIEN ENVOYER : « Jean-Baptiste A. vient vous
    /// chercher » reste utile, un message jamais parti ne l'est pas.
    ///
    /// LA MARQUE DE TRONCATURE EST UN POINT, PAS DES POINTS DE SUSPENSION. Le
    /// caractere « … » n'est pas dans l'alphabet GSM-7 : un seul suffit a faire
    /// basculer tout le message en UCS-2, qui ne tient plus que 70 caracteres par
    /// segment. C'est exactement ce que le catalogue interdit.
    /// </remarks>
    private static string Borner(string? valeur, int maximum)
    {
        var propre = (valeur ?? string.Empty).Trim();

        return propre.Length <= maximum ? propre : propre[..(maximum - 1)] + ".";
    }

    /// <summary>« 01/10/2026 a 14:35 », heure de Cotonou.</summary>
    ///
    /// <remarks>
    /// L'HEURE LOCALE, ET NON UTC. Un passager qui lit « terminee a 13:35 » alors
    /// qu'il est descendu a 14:35 conclut que le recu est faux. Cotonou est a UTC+1
    /// toute l'annee — aucun changement d'heure — ce qui rend ce calcul sur.
    /// </remarks>
    private static string Jour(DateTimeOffset quand)
        => quand.ToOffset(TimeSpan.FromHours(1)).ToString("dd/MM/yyyy' a 'HH:mm", CultureInfo.InvariantCulture);

    private static string MontantLisible(long xof)
        => xof.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ", StringComparison.Ordinal) + " FCFA";

    /// <summary>
    /// L'enveloppe d'une demande de notification.
    /// </summary>
    ///
    /// <remarks>
    /// L'IDENTIFIANT D'EVENEMENT EST DERIVE, PAS REPRIS TEL QUEL. Deux messages
    /// partent du meme fait de domaine — l'evenement de course et la demande d'envoi
    /// — sur deux topics differents. Leur donner le meme EventId ferait que l'Inbox
    /// d'un service qui ecoute les deux prendrait le second pour un doublon du premier
    /// et le jetterait. Le derive est deterministe : le meme fait rejoue donne le meme
    /// identifiant, et l'idempotence tient.
    /// </remarks>
    private static EventEnvelope EnveloppeDeCommande(
        RideEntity ride,
        IDomainEvent domainEvent,
        string eventType,
        byte marque)
    {
        Span<byte> octets = stackalloc byte[16];
        domainEvent.EventId.TryWriteBytes(octets);
        octets[15] ^= marque;

        return new EventEnvelope
        {
            EventId = new Guid(octets).ToString(),
            EventType = eventType,
            SchemaVersion = 1,
            AggregateType = AggregateType,
            AggregateId = ride.Id.ToString(),
            OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
            TraceId = Activity.Current?.TraceId.ToString() ?? string.Empty,
            CorrelationId = Activity.Current?.RootId ?? string.Empty,
            ActorType = MapActor(domainEvent.Actor.Kind),
            ActorId = domainEvent.Actor.Id,
            Producer = Producer,
        };
    }

    private static RideEvent Wrap(
        RideEntity ride,
        IDomainEvent domainEvent,
        string eventType,
        Action<RideEvent> fill)
    {
        var message = new RideEvent
        {
            Envelope = new EventEnvelope
            {
                EventId = domainEvent.EventId.ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = AggregateType,
                AggregateId = ride.Id.ToString(),
                OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
                TraceId = Activity.Current?.TraceId.ToString() ?? string.Empty,
                CorrelationId = Activity.Current?.RootId ?? string.Empty,
                ActorType = MapActor(domainEvent.Actor.Kind),
                ActorId = domainEvent.Actor.Id,
                Producer = Producer,
            },
        };

        fill(message);
        return message;
    }

    private static GeoPoint Point(double latitude, double longitude)
        => new() { Latitude = latitude, Longitude = longitude };

    /// <summary>
    /// LE CODE DEVISE EST ÉCRIT ICI ALORS QUE LE DOMAINE NE LE PORTE PAS. Le
    /// contrat commun a un champ <c>currency</c> pour le jour où une autre
    /// devise UEMOA apparaîtra ; le laisser vide sur le fil obligerait chaque
    /// consommateur à deviner.
    /// </summary>
    private static Money Montant(long amount)
        => new() { Amount = amount, Currency = DomainMoney.CurrencyCode };

    private static RideVehicleType MapVehicle(DomainVehicle type) => type switch
    {
        DomainVehicle.Motorcycle => RideVehicleType.Motorcycle,
        DomainVehicle.Car => RideVehicleType.Car,
        DomainVehicle.Tricycle => RideVehicleType.Tricycle,
        _ => RideVehicleType.Unspecified,
    };

    private static ActorType MapActor(ActorKind kind) => kind switch
    {
        ActorKind.Customer => ActorType.Customer,
        ActorKind.Driver => ActorType.Driver,
        ActorKind.Merchant => ActorType.Merchant,
        ActorKind.Partner => ActorType.Partner,
        ActorKind.Admin => ActorType.Admin,
        ActorKind.Dispatch => ActorType.Dispatch,
        ActorKind.PaymentProvider => ActorType.PaymentProvider,
        ActorKind.Scheduler => ActorType.Scheduler,
        ActorKind.Service => ActorType.Service,
        _ => ActorType.Unspecified,
    };
}
