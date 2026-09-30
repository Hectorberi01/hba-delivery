using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Messaging.Outbox;
using System.Globalization;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Notification.V1;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.Deliveries.Events;
using DomainEvents = Hba.Delivery.Domain.Deliveries.Events;

namespace Hba.Delivery.Application.IntegrationEvents;

/// <summary>
/// Traduit les faits de domaine en messages Kafka et les dépose dans l'Outbox.
/// Tous les événements de domaine ne sortent pas : seuls ceux dont un autre
/// service a besoin.
/// </summary>
public interface IDeliveryIntegrationEventPublisher
{
    void Publish(IOutbox outbox, DeliveryAggregate delivery, IDomainEvent domainEvent);
}

/// L'Outbox est passée en paramètre plutôt qu'injectée : EfOutbox écrit dans le
/// DbContext, et le DbContext a besoin de ce publieur. Les injecter tous les
/// deux formerait un cycle que le conteneur refuse de résoudre — y compris au
/// moment où dotnet ef instancie le contexte.
public sealed class DeliveryIntegrationEventPublisher : IDeliveryIntegrationEventPublisher
{
    private const string Producer = "delivery";
    private const string AggregateType = "delivery";

    /// <summary>
    /// De quoi distinguer les identifiants des commandes derivees d'un meme
    /// fait de domaine.
    /// </summary>
    ///
    /// <remarks>
    /// UNE MARQUE PAR DESTINATION, ET ELLES DOIVENT RESTER DISTINCTES. Le
    /// dernier octet de l'identifiant d'evenement est modifie pour que deux
    /// messages nes du meme fait n'aient pas le meme EventId — sans quoi
    /// l'Inbox d'un service qui ecoute les deux topics prendrait le second pour
    /// un doublon et le jetterait. Deux marques identiques ramenent exactement
    /// ce defaut, en plus discret.
    /// </remarks>
    private const byte MarqueCourriel = 0x01;

    private const byte MarqueSms = 0x02;

    public void Publish(IOutbox outbox, DeliveryAggregate delivery, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(domainEvent);

        ArgumentNullException.ThrowIfNull(outbox);

        // LE RECU PART AVANT L'EVENEMENT, ET DANS LA MEME TRANSACTION.
        // L'ordre entre les deux enfilements n'a aucune importance — l'Outbox
        // les ecrit ensemble ou pas du tout — mais le faire ICI, et non dans le
        // handler qui cloture la course, en fait une consequence du FAIT
        // « la course est terminee » plutot qu'un effet de bord d'un appel.
        RecuDeCourse(outbox, delivery, domainEvent);
        CodeDeRemise(outbox, delivery, domainEvent);

        var message = Map(delivery, domainEvent);
        if (message is null)
        {
            return;
        }

        outbox.Enqueue(
            KafkaTopics.DeliveryEvents,
            delivery.Id.ToString(),
            message.Envelope.EventType,
            message);
    }

    /// <summary>
    /// Depose la demande d'envoi du code de remise au destinataire, par SMS.
    /// </summary>
    ///
    /// <remarks>
    /// CE MESSAGE N'AVAIT AUCUN PRODUCTEUR, ET C'EST LA PREUVE DE LIVRAISON QUI
    /// EN DEPENDAIT. Le modele « delivery_otp » existait dans le catalogue de
    /// Notification depuis la creation du service ; personne ne le demandait.
    /// Le destinataire ne recevait donc jamais son code, et l'ADR 0005 en fait
    /// la seule chose qui distingue « le colis a ete remis » de « quelqu'un a
    /// clique ».
    ///
    /// A L'ATTRIBUTION, ET PAS AVANT. Une course qui ne trouve pas de livreur
    /// n'envoie alors aucun SMS — et un SMS se paie au segment. Le destinataire
    /// apprend qu'un colis part vers lui au moment ou c'est devenu vrai, avec
    /// le temps de retrouver le message avant l'arrivee.
    ///
    /// LE NUMERO EST ICI, CONTRAIREMENT A L'ADRESSE DU RECU. Le destinataire
    /// n'a pas de compte HBA : son telephone est saisi a la commande et vit
    /// dans l'agregat. Il n'y a personne a qui aller le demander.
    ///
    /// UNE REAFFECTATION RENVOIE LE MESSAGE, avec le meme code. C'est assume :
    /// quand l'exploitation change de livreur, le destinataire est prevenu une
    /// seconde fois qu'un colis arrive, ce qui est exact.
    ///
    /// SMS SEULEMENT, ET LE CATALOGUE LE TIENT DEJA : le destinataire n'a
    /// jamais donne d'opt-in WhatsApp, que Meta exige avant tout gabarit.
    /// </remarks>
    private static void CodeDeRemise(
        IOutbox outbox,
        DeliveryAggregate delivery,
        IDomainEvent domainEvent)
    {
        if (domainEvent is not DomainEvents.DriverAssigned)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(delivery.Recipient.Phone))
        {
            return;
        }

        var commande = new NotificationCommand
        {
            Envelope = EnveloppeDeCommande(
                delivery,
                domainEvent,
                "hba.notification.v1.SendSms",
                MarqueSms),
            SendSms = new SendSms
            {
                ToPhone = delivery.Recipient.Phone,
                TemplateId = "delivery_otp",
            },
        };

        commande.SendSms.Variables.Add("reference", delivery.Reference);
        commande.SendSms.Variables.Add("code", delivery.Otp.Code);

        // CLE DE PARTITION : LA COURSE. Le destinataire n'a pas d'identifiant
        // chez nous, et c'est l'ordre des messages D'UNE MEME COURSE qui compte.
        outbox.Enqueue(
            KafkaTopics.NotificationCommands,
            delivery.Id.ToString(),
            commande.Envelope.EventType,
            commande);
    }

    /// <summary>
    /// Depose la demande d'envoi du recu de course, par courriel.
    /// </summary>
    ///
    /// <remarks>
    /// L'ADRESSE N'EST PAS ICI, ET C'EST DELIBERE. Delivery ne connait que
    /// l'identifiant du client ; son courriel vit dans Directory. Aller le
    /// chercher mettrait un appel reseau dans la transaction qui cloture une
    /// livraison — Directory injoignable, et le livreur ne peut plus clore sa
    /// mission. C'est Notification qui resout l'adresse, au moment d'envoyer.
    ///
    /// RIEN NE PART POUR UNE COURSE QUI N'A PAS DE CLIENT. Une course d'un
    /// partenaire B2B ou de HBA Express n'a pas de compte client a qui adresser
    /// un recu ; « IsCustomerOrdered » est exactement cette question.
    ///
    /// CE RECU N'EST PAS UNE FACTURE, et le modele le dit au client. Voir le
    /// point 29 : une facture supposerait une numerotation continue, l'IFU et
    /// le regime de TVA de HBA, dont rien n'existe dans ce depot.
    /// </remarks>
    private static void RecuDeCourse(
        IOutbox outbox,
        DeliveryAggregate delivery,
        IDomainEvent domainEvent)
    {
        if (domainEvent is not DomainEvents.DeliveryCompleted || !delivery.IsCustomerOrdered)
        {
            return;
        }

        var commande = new NotificationCommand
        {
            Envelope = EnveloppeDeCommande(
                delivery,
                domainEvent,
                "hba.notification.v1.SendEmail",
                MarqueCourriel),
            SendEmail = new SendEmail
            {
                SubjectId = delivery.CustomerId,
                TemplateId = "delivery_receipt",
            },
        };

        // LES VALEURS SONT MISES EN FORME ICI, PAS DANS LE MODELE. Le catalogue
        // de Notification substitue des chaines, il ne formate rien : lui
        // passer un nombre brut ferait ecrire « 1500 » la ou le client attend
        // « 1 500 F CFA », et une date ISO la ou il attend un jour.
        commande.SendEmail.Variables.Add("reference", delivery.Reference);
        commande.SendEmail.Variables.Add("date", Jour(delivery.CompletedAt ?? domainEvent.OccurredAt));
        commande.SendEmail.Variables.Add("depart", delivery.Pickup.Landmark);
        commande.SendEmail.Variables.Add("arrivee", delivery.Dropoff.Landmark);
        commande.SendEmail.Variables.Add("montant", Montant(delivery.Pricing.Total.Amount));

        // CLE DE PARTITION : LE CLIENT, ET NON LA COURSE. Deux recus pour le
        // meme client arrivent dans l'ordre ou ses courses se sont terminees.
        outbox.Enqueue(
            KafkaTopics.NotificationCommands,
            delivery.CustomerId!,
            commande.Envelope.EventType,
            commande);
    }

    /// <summary>
    /// L'enveloppe de la commande d'envoi.
    /// </summary>
    ///
    /// <remarks>
    /// L'IDENTIFIANT D'EVENEMENT EST DERIVE, PAS REPRIS TEL QUEL. Deux messages
    /// partent du meme fait de domaine — l'evenement « course terminee » et la
    /// demande d'envoi du recu — sur deux topics differents. Leur donner le
    /// meme EventId ferait que l'Inbox d'un service qui ecoute les deux
    /// prendrait le second pour un doublon du premier et le jetterait.
    ///
    /// Le derive est deterministe : le meme fait rejoue donne le meme
    /// identifiant, et l'idempotence tient.
    /// </remarks>
    private static EventEnvelope EnveloppeDeCommande(
        DeliveryAggregate delivery,
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
            AggregateId = delivery.Id.ToString(),
            OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            CorrelationId = System.Diagnostics.Activity.Current?.RootId ?? string.Empty,
            PartnerId = delivery.PartnerId,
            ActorType = MapActor(domainEvent.Actor.Kind),
            ActorId = domainEvent.Actor.Id,
            Producer = Producer,
        };
    }

    /// <summary>« 28/09/2026 a 14:35 », heure de Cotonou.</summary>
    ///
    /// <remarks>
    /// L'HEURE LOCALE, ET NON UTC. Un client qui lit « remis a 13:35 » alors
    /// qu'il a vu le livreur a 14:35 conclut que le recu est faux. Cotonou est
    /// a UTC+1 toute l'annee — pas de changement d'heure — ce qui rend le
    /// decalage fixe et ce calcul sur.
    /// </remarks>
    private static string Jour(DateTimeOffset quand)
        => quand.ToOffset(TimeSpan.FromHours(1))
            .ToString("dd/MM/yyyy 'a' HH:mm", CultureInfo.InvariantCulture);

    /// <summary>« 1 500 F CFA », avec une espace insecable avant l'unite.</summary>
    private static string Montant(long francs)
        => string.Create(CultureInfo.InvariantCulture, $"{francs:N0} F CFA")
            .Replace(",", "\u00a0", StringComparison.Ordinal);

    private static DeliveryEvent? Map(DeliveryAggregate delivery, IDomainEvent domainEvent) => domainEvent switch
    {
        DomainEvents.DeliveryCreated e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCreated", m => m.Created = new Contracts.Delivery.V1.DeliveryCreated
        {
            DeliveryId = delivery.Id.ToString(),
            Reference = delivery.Reference,
            Source = MapSource(delivery.Source),
            CustomerId = delivery.CustomerId ?? string.Empty,
            MerchantId = delivery.MerchantId ?? string.Empty,
            Pickup = MapLocation(delivery.Pickup),
            Dropoff = MapLocation(delivery.Dropoff),
            Total = MapMoney(e.Total.Amount),
        }),

        DomainEvents.DeliveryConfirmed e => Wrap(delivery, e, "hba.delivery.v1.DeliveryConfirmed", m => m.Confirmed = new Contracts.Delivery.V1.DeliveryConfirmed
        {
            DeliveryId = delivery.Id.ToString(),
            Pickup = MapLocation(delivery.Pickup),
            Dropoff = MapLocation(delivery.Dropoff),
            DriverEarning = MapMoney(delivery.Pricing.DriverEarning.Amount),
            DistanceMeters = delivery.Pricing.DistanceMeters,
            PaidAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        // LE MEME MESSAGE QUE « DeliveryConfirmed », ET C'EST VOULU.
        //
        // Dispatch n'ecoute qu'un declencheur — « DeliveryConfirmed » — et il
        // n'a besoin que du point d'enlevement, de la distance et du gain du
        // livreur. Lui inventer un second declencheur obligerait a modifier un
        // service qui fonctionne, pour lui apprendre une distinction qui ne le
        // concerne pas : d'ou vient l'argent n'a aucune consequence sur la
        // facon de chercher un livreur.
        //
        // LA DISTINCTION RESTE A L'INTERIEUR DE DELIVERY, dans le fait de
        // domaine, la ou elle sert : l'audit dit « le compte du commercant a
        // regle », et non « FedaPay a encaisse ».
        DomainEvents.DeliverySettledByAccount e => Wrap(delivery, e, "hba.delivery.v1.DeliveryConfirmed", m => m.Confirmed = new Contracts.Delivery.V1.DeliveryConfirmed
        {
            DeliveryId = delivery.Id.ToString(),
            Pickup = MapLocation(delivery.Pickup),
            Dropoff = MapLocation(delivery.Dropoff),
            DriverEarning = MapMoney(delivery.Pricing.DriverEarning.Amount),
            DistanceMeters = delivery.Pricing.DistanceMeters,
            PaidAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DriverAssigned e => Wrap(delivery, e, "hba.delivery.v1.DriverAssigned", m => m.DriverAssigned = new Contracts.Delivery.V1.DriverAssigned
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            OfferId = e.OfferId,
            AssignedAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DriverArrivedAtPickup e => Wrap(delivery, e, "hba.delivery.v1.DriverArrivedAtPickup", m => m.DriverArrivedAtPickup = new Contracts.Delivery.V1.DriverArrivedAtPickup
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DeliveryPickedUp e => Wrap(delivery, e, "hba.delivery.v1.DeliveryPickedUp", m => m.PickedUp = new Contracts.Delivery.V1.DeliveryPickedUp
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        DomainEvents.DeliveryCompleted e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCompleted", m => m.Delivered = new Contracts.Delivery.V1.DeliveryCompleted
        {
            DeliveryId = delivery.Id.ToString(),
            DriverId = e.DriverId,
            DriverEarning = MapMoney(e.DriverEarning.Amount),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
            Reference = delivery.Reference,
        }),

        // « DriverUnassigned » EMPRUNTE « DeliveryCancelled », ET C'EST LE SEUL
        // MESSAGE QUI FAIT CE QU'IL FAUT.
        //
        // Ce fait ne sortait nulle part : il n'etait ni mappe ni consomme. Il
        // sert maintenant a un cas precis — un livreur qui a accepte une course
        // deja close — et le seul service concerne est Driver, qui met fin a la
        // mission sur « Cancelled » porteur d'un driver_id. Lui inventer un
        // message a lui demanderait un proto, un topic relu et une branche de
        // consommateur, pour dire exactement la meme chose a la meme personne.
        //
        // LE STATUT PUBLIE EST CELUI DE LA COURSE, pas « Cancelled » en dur :
        // elle peut aussi etre close en NO_DRIVER_FOUND ou en FAILED, et
        // « previous_status » est justement la pour le dire.
        DomainEvents.DriverUnassigned e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCancelled", m => m.Cancelled = new Contracts.Delivery.V1.DeliveryCancelled
        {
            DeliveryId = delivery.Id.ToString(),
            CancelledBy = MapActor(e.Actor.Kind),
            ActorId = e.Actor.Id,
            Reason = e.Reason,
            PreviousStatus = MapStatus(delivery.Status),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
            DriverId = e.PreviousDriverId,
        }),

        DomainEvents.DeliveryCancelled e => Wrap(delivery, e, "hba.delivery.v1.DeliveryCancelled", m => m.Cancelled = new Contracts.Delivery.V1.DeliveryCancelled
        {
            DeliveryId = delivery.Id.ToString(),
            CancelledBy = MapActor(e.Actor.Kind),
            ActorId = e.Actor.Id,
            Reason = e.Reason,
            PreviousStatus = MapStatus(e.PreviousStatus),
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),

            // L'AGREGAT PORTE ENCORE SON LIVREUR AU MOMENT DE L'ANNULATION :
            // Cancel ne l'efface pas. C'est ce qui permet a Driver de le
            // liberer — sans quoi il resterait en mission sur une course qui
            // n'existe plus.
            DriverId = delivery.Driver?.DriverId ?? string.Empty,
        }),

        DomainEvents.DeliveryFailed e => Wrap(delivery, e, "hba.delivery.v1.DeliveryFailed", m => m.Failed = new Contracts.Delivery.V1.DeliveryFailed
        {
            DeliveryId = delivery.Id.ToString(),
            Reason = e.Reason,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
            DriverId = delivery.Driver?.DriverId ?? string.Empty,
        }),

        DomainEvents.NoDriverFound e => Wrap(delivery, e, "hba.delivery.v1.NoDriverFound", m => m.NoDriverFound = new Contracts.Delivery.V1.NoDriverFound
        {
            DeliveryId = delivery.Id.ToString(),
            WavesAttempted = e.WavesAttempted,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }),

        // Les autres faits (recherche ouverte, OTP refusé, désaffectation, preuve
        // attachée) restent internes : aucun autre service n'en dépend
        // aujourd'hui. Pour la preuve c'est même voulu — une photo de remise ne
        // se voit que par l'admin depuis le 30 septembre 2026, et annoncer sur
        // le bus qu'elle existe irait contre cette décision.
        _ => null,
    };

    private static DeliveryEvent Wrap(
        DeliveryAggregate delivery,
        IDomainEvent domainEvent,
        string eventType,
        Action<DeliveryEvent> fill)
    {
        var message = new DeliveryEvent
        {
            Envelope = new EventEnvelope
            {
                EventId = domainEvent.EventId.ToString(),
                EventType = eventType,
                SchemaVersion = 1,
                AggregateType = AggregateType,
                AggregateId = delivery.Id.ToString(),
                OccurredAt = Timestamp.FromDateTimeOffset(domainEvent.OccurredAt),
                TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
                CorrelationId = System.Diagnostics.Activity.Current?.RootId ?? string.Empty,
                PartnerId = delivery.PartnerId,
                ActorType = MapActor(domainEvent.Actor.Kind),
                ActorId = domainEvent.Actor.Id,
                Producer = Producer,
            },
        };

        fill(message);
        return message;
    }

    private static Money MapMoney(long amount) => new() { Amount = amount, Currency = "XOF" };

    private static Contracts.Common.V1.Location MapLocation(Domain.ValueObjects.Location location) => new()
    {
        Point = new GeoPoint { Latitude = location.Point.Latitude, Longitude = location.Point.Longitude },
        Landmark = location.Landmark,
        Phone = location.Phone,
        ContactName = location.ContactName,
        Notes = location.Notes ?? string.Empty,
    };

    private static Source MapSource(DeliverySource source) => source switch
    {
        DeliverySource.ClientApp => Source.ClientApp,
        DeliverySource.HbaExpress => Source.HbaExpress,
        DeliverySource.HbaFood => Source.HbaFood,
        DeliverySource.PartnerApi => Source.PartnerApi,
        DeliverySource.MerchantPortal => Source.ClientApp,
        _ => Source.Unspecified,
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

    private static Contracts.Delivery.V1.DeliveryStatus MapStatus(Domain.Deliveries.DeliveryStatus status) => status switch
    {
        Domain.Deliveries.DeliveryStatus.PendingPayment => Contracts.Delivery.V1.DeliveryStatus.PendingPayment,
        Domain.Deliveries.DeliveryStatus.PaymentFailed => Contracts.Delivery.V1.DeliveryStatus.PaymentFailed,
        Domain.Deliveries.DeliveryStatus.Paid => Contracts.Delivery.V1.DeliveryStatus.Paid,
        Domain.Deliveries.DeliveryStatus.SearchingDriver => Contracts.Delivery.V1.DeliveryStatus.SearchingDriver,
        Domain.Deliveries.DeliveryStatus.NoDriverFound => Contracts.Delivery.V1.DeliveryStatus.NoDriverFound,
        Domain.Deliveries.DeliveryStatus.DriverAssigned => Contracts.Delivery.V1.DeliveryStatus.DriverAssigned,
        Domain.Deliveries.DeliveryStatus.DriverAtPickup => Contracts.Delivery.V1.DeliveryStatus.DriverAtPickup,
        Domain.Deliveries.DeliveryStatus.PickedUp => Contracts.Delivery.V1.DeliveryStatus.PickedUp,
        Domain.Deliveries.DeliveryStatus.Delivered => Contracts.Delivery.V1.DeliveryStatus.Delivered,
        Domain.Deliveries.DeliveryStatus.Cancelled => Contracts.Delivery.V1.DeliveryStatus.Cancelled,
        Domain.Deliveries.DeliveryStatus.Failed => Contracts.Delivery.V1.DeliveryStatus.Failed,
        _ => Contracts.Delivery.V1.DeliveryStatus.Unspecified,
    };
}
