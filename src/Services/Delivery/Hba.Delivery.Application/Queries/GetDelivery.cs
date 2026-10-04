using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Application.Authorization;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Queries;

/// <param name="AvecItineraire">
/// Demande la géométrie de l'itinéraire actif en plus de sa durée.
///
/// FACULTATIF PARCE QU'IL COÛTE : un tracé pèse plusieurs kilo-octets, et seul
/// un écran qui DESSINE la route en a besoin. Voir GetDeliveryRequest.
/// </param>
public sealed record GetDeliveryQuery(Guid DeliveryId, bool AvecItineraire = false)
    : IQuery<DeliveryView>;

public sealed class GetDeliveryHandler(
    IDeliveryRepository repository,
    IMesureurDItineraire mesureur,
    ICallerContext caller) : IQueryHandler<GetDeliveryQuery, DeliveryView>
{
    public async Task<DeliveryView> HandleAsync(GetDeliveryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var delivery = await repository.GetByIdAsync(query.DeliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", query.DeliveryId.ToString());

        DeliveryAccess.EnsureCanRead(delivery, caller);

        var vue = DeliveryViewMapper.ToView(delivery, caller);

        return await AvecLItineraireAsync(delivery, vue, query.AvecItineraire, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ajoute l'itinéraire actif, quand il a un sens et que l'appelant peut le
    /// voir.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE N'EST CALCULÉE QUE SUR LA LECTURE D'UNE SEULE LIVRAISON, jamais sur
    /// la liste. Deux appels réseau par ligne transformeraient « mes commandes »
    /// en une rafale de requêtes vers Location et Routing, pour une colonne que
    /// personne ne lit dans une liste.
    ///
    /// LA VISIBILITÉ EST CELLE DE LA VUE, PAS UNE RÈGLE DE PLUS. On se raccroche
    /// à CancellationFeeDueXof, déjà réservé au donneur d'ordre et au
    /// back-office : si la vue ne porte pas ce champ, l'appelant est un livreur
    /// ou un tiers, et il n'a pas à savoir dans combien de temps il arrive
    /// lui-même. Recopier le test des rôles ici aurait créé une deuxième
    /// implémentation de la même règle, et c'est celle qui diverge qui finit par
    /// montrer ce qu'elle ne devrait pas.
    /// </remarks>
    private async Task<DeliveryView> AvecLItineraireAsync(
        DeliveryAggregate delivery,
        DeliveryView vue,
        bool avecTrace,
        CancellationToken cancellationToken)
    {
        var driverId = delivery.Driver?.DriverId;

        if (string.IsNullOrWhiteSpace(driverId) || vue.CancellationFeeDueXof is null)
        {
            return vue;
        }

        // LA CIBLE SUIT L'ÉTAT. Avant l'enlèvement le livreur roule vers le point
        // de collecte ; une fois le colis à bord, vers le destinataire. Garder le
        // point de collecte après l'enlèvement ferait décroître une durée vers un
        // endroit que le livreur a déjà quitté.
        // LE TYPE EST ÉCRIT, PAS INFÉRÉ. Avec « var », le type naturel du switch
        // serait « Location » non nullable — le bras « null » s'y convertissant
        // — et le test qui suit passerait pour toujours faux : un avertissement,
        // donc une erreur de compilation dans ce dépôt.
        Location? cible = delivery.Status switch
        {
            DeliveryStatus.DriverAssigned or DeliveryStatus.DriverAtPickup => delivery.Pickup,
            DeliveryStatus.PickedUp => delivery.Dropoff,
            _ => null,
        };

        if (cible is null)
        {
            return vue;
        }

        var route = await mesureur.MesurerAsync(
            driverId,
            cible.Point.Latitude,
            cible.Point.Longitude,
            delivery.Driver!.VehicleType.ToString(),
            avecTrace,
            cancellationToken).ConfigureAwait(false);

        return route is null ? vue : vue with { Itineraire = route };
    }
}
