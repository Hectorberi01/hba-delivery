using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Ride.V1;
using Hba.Dispatch.Application.Features.Dispatching.Commands;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DomainVehicle = Hba.Dispatch.Domain.Dispatching.VehicleType;

namespace Hba.Dispatch.Api.Messaging;

/// <summary>
/// Entree du moteur pour les COURSES DE PERSONNES.
/// </summary>
///
/// <remarks>
/// « RideDriverSearchStarted » EST LE SEUL DECLENCHEUR, et pas
/// « RideRequested » : entre la demande et la recherche il y a le paiement. Ride
/// n'emet cet evenement qu'une fois la course payee — exactement le role que
/// « DeliveryConfirmed » joue pour un colis.
///
/// L'EVENEMENT PORTE TOUT CE QU'IL FAUT POUR OUVRIR LA RECHERCHE : les deux
/// points, le vehicule demande, le client. C'est delibere — un consommateur
/// oblige de rappeler l'emetteur pour agir sur un message n'est plus decouple, et
/// Ride indisponible bloquerait alors tout le dispatch.
///
/// LE GAIN DU CHAUFFEUR VIENT DU DEVIS, pas d'un calcul local : Pricing le fixe
/// en meme temps que le prix, Ride le fige, et il voyage ici. Le recalculer
/// donnerait deux montants pour une meme course.
/// </remarks>
public sealed class RideEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<RideEventsConsumer> logger)
    : KafkaConsumerBase<RideEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.RideEvents;

    protected override Guid GetEventId(RideEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(RideEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        RideEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        var dispatcher = services.GetRequiredService<IDispatcher>();

        switch (message.PayloadCase)
        {
            case RideEvent.PayloadOneofCase.SearchStarted:
                await OuvrirAsync(dispatcher, message.SearchStarted, cancellationToken).ConfigureAwait(false);
                break;

            // UNE COURSE CLOSE ARRETE LE MOTEUR. Sans cela il continuerait ses
            // vagues sur une course annulee : des chauffeurs se deplaceraient pour
            // rien, et chaque offre publiee ferait echouer le consommateur de Ride
            // sur un etat terminal.
            case RideEvent.PayloadOneofCase.Cancelled:
                await ArreterAsync(
                    dispatcher,
                    message.Cancelled.RideId,
                    "RideCancelled",
                    cancellationToken).ConfigureAwait(false);
                break;

            // LA COURSE EST TERMINEE : il ne reste rien a chercher. Le cas est
            // theoriquement impossible — une course ne se termine pas sans
            // chauffeur — mais une recherche laissee ouverte par un enchainement
            // qu'on n'a pas prevu tournerait indefiniment.
            case RideEvent.PayloadOneofCase.Completed:
                await ArreterAsync(
                    dispatcher,
                    message.Completed.RideId,
                    "RideCompleted",
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                // Les autres faits de la course ne concernent pas le moteur :
                // l'affectation, l'approche, l'arrivee sont ses propres
                // consequences, pas ses entrees.
                break;
        }
    }

    private async Task OuvrirAsync(
        IDispatcher dispatcher,
        RideDriverSearchStarted debut,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(debut.RideId, out var rideId) || debut.Pickup is null)
        {
            logger.LogWarning(
                "RideDriverSearchStarted inexploitable pour {RideId} : identifiant ou point de prise en charge manquant.",
                debut.RideId);
            return;
        }

        await dispatcher.SendAsync(
            new StartDispatchCommand(
                ServiceType.Ride,
                rideId,
                debut.Pickup.Latitude,
                debut.Pickup.Longitude,

                // UNE COURSE N'A PAS DE REPERE ECRIT, et on n'en invente pas.
                //
                // Un colis part d'un commerce ou d'une adresse decrite à la main
                // — « en face de la pharmacie Gbedjromede » —, parce que
                // l'adressage formel est peu fiable au Benin. Un passager, lui,
                // pose un point sur une carte. Remplir ce champ d'un texte
                // fabrique ferait afficher au chauffeur un repere que personne n'a
                // ecrit ; vide, l'application montre le point, qui est la verite.
                PickupLandmark: null,

                debut.DistanceMeters,
                debut.DriverEarning?.Amount ?? 0,
                Vehicule(debut.VehicleType)),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ArreterAsync(
        IDispatcher dispatcher,
        string rideId,
        string raison,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(rideId, out var id))
        {
            return;
        }

        await dispatcher
            .SendAsync(new CancelDispatchCommand(ServiceType.Ride, id, raison), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Le vehicule demande par la course.
    /// </summary>
    ///
    /// <remarks>
    /// NON PRECISE RESTE NON PRECISE, ET NE DEVIENT PAS « MOTO ». Pour un colis ce
    /// defaut est juste — le devis est calcule sur une moto. Pour une course, un
    /// passager qui a demande une voiture ne doit pas se voir proposer une moto :
    /// mieux vaut ne filtrer sur rien que de filtrer sur le mauvais type, et
    /// l'absence signale un producteur a corriger.
    /// </remarks>
    private static DomainVehicle Vehicule(RideVehicleType type) => type switch
    {
        RideVehicleType.Motorcycle => DomainVehicle.Motorcycle,
        RideVehicleType.Car => DomainVehicle.Car,
        RideVehicleType.Tricycle => DomainVehicle.Tricycle,
        _ => DomainVehicle.Unspecified,
    };
}
