using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Commands.CreateDelivery;

public sealed class CreateDeliveryHandler(
    IDeliveryRepository repository,
    IPricingClient pricing,
    IPaymentClient payments,
    IReferenceGenerator references,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<CreateDeliveryCommand, CreateDeliveryResult>
{
    private const string IdempotencyScope = "delivery:create";

    public async Task<CreateDeliveryResult> HandleAsync(
        CreateDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var orderer = ResolveOrderer(command);

        // Rejeu : la même clé renvoie la même livraison, sans rien recréer.
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingId = await idempotency
                .TryGetResultAsync(IdempotencyScope, BuildIdempotencyKey(orderer, command.IdempotencyKey), cancellationToken)
                .ConfigureAwait(false);

            if (existingId is not null && Guid.TryParse(existingId, out var knownId))
            {
                var known = await repository.GetByIdAsync(knownId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException("Livraison", existingId);

                return new CreateDeliveryResult(
                    DeliveryViewMapper.ToView(known, caller),
                    known.PaymentIntentId,
                    null);
            }
        }

        // Un partenaire ne crée jamais deux fois la même commande externe.
        if (!string.IsNullOrWhiteSpace(command.ExternalOrderId))
        {
            var duplicate = await repository
                .GetByExternalOrderIdAsync(orderer.PartnerId, command.ExternalOrderId, cancellationToken)
                .ConfigureAwait(false);

            if (duplicate is not null)
            {
                return new CreateDeliveryResult(
                    DeliveryViewMapper.ToView(duplicate, caller),
                    duplicate.PaymentIntentId,
                    null);
            }
        }

        var deliveryId = Guid.CreateVersion7();

        // Le devis est consommé : le prix ne pourra plus changer pour cette course.
        var snapshot = await pricing
            .ConsumeQuoteAsync(command.QuoteId, deliveryId, cancellationToken)
            .ConfigureAwait(false);

        var delivery = DeliveryAggregate.Create(
            deliveryId,
            references.NextDeliveryReference(),
            command.Source,
            orderer.PartnerId,
            command.ExternalOrderId,
            orderer.CustomerId,
            orderer.MerchantId,
            command.PickupPointId,
            ToLocation(command.Pickup),
            ToLocation(command.Dropoff),
            Recipient.Create(command.RecipientName, command.RecipientPhone),
            snapshot,
            command.PackageDescription,
            command.PackageWeightGrams,
            caller.ToActor(),
            clock.UtcNow);

        PaymentIntentResult? intent = null;

        if (orderer.CustomerId is not null)
        {
            intent = await payments
                .CreateIntentAsync(
                    command.IdempotencyKey ?? deliveryId.ToString(),
                    deliveryId,
                    orderer.CustomerId,
                    command.Pickup.Phone,
                    snapshot.Total,
                    cancellationToken)
                .ConfigureAwait(false);

            delivery.AttachPaymentIntent(intent.PaymentIntentId);
        }

        repository.Add(delivery);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            await idempotency
                .RememberAsync(
                    IdempotencyScope,
                    BuildIdempotencyKey(orderer, command.IdempotencyKey),
                    deliveryId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Une seule transaction : la livraison, la clé d'idempotence et les
        // messages d'Outbox sont validés ensemble.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CreateDeliveryResult(
            DeliveryViewMapper.ToView(delivery, caller),
            intent?.PaymentIntentId,
            intent?.RedirectUrl);
    }

    /// <summary>
    /// Détermine le donneur d'ordre à partir du jeton, jamais à partir du corps
    /// de la requête : un appelant ne choisit pas pour le compte de qui il crée.
    /// </summary>
    private Orderer ResolveOrderer(CreateDeliveryCommand command)
    {
        if (caller.IsInRole(HbaRoles.Partner))
        {
            var partnerId = caller.PartnerId
                ?? throw new ForbiddenException("Le jeton partenaire ne porte pas de partner_id.");

            if (string.IsNullOrWhiteSpace(command.ExternalOrderId))
            {
                throw new DomainException(
                    "MISSING_EXTERNAL_ORDER_ID",
                    "Un partenaire doit fournir externalOrderId.");
            }

            if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                throw new DomainException(
                    "MISSING_IDEMPOTENCY_KEY",
                    "Idempotency-Key est obligatoire pour les créations partenaire.");
            }

            // Le partenaire agit pour un commerçant qu'il désigne, ou pour
            // lui-même. Le paiement est celui du partenaire, hors de ce service.
            return new Orderer(partnerId, CustomerId: null, MerchantId: command.MerchantId);
        }

        if (caller.IsInRole(HbaRoles.MerchantOwner))
        {
            var merchantId = caller.MerchantId
                ?? throw new ForbiddenException("Le jeton commerçant ne porte pas de merchant_id.");

            // À TRANCHER — mode de règlement quand le commerçant est lui-même le
            // donneur d'ordre : paiement FedaPay à chaque course, ou facturation
            // périodique. Tant que ce n'est pas tranché, la création est refusée
            // plutôt qu'implémentée d'une des deux façons.
            throw new DomainException(
                "MERCHANT_SETTLEMENT_UNDECIDED",
                "Le mode de règlement du commerçant donneur d'ordre n'est pas tranché : "
                + "paiement par course ou facturation périodique. Aucune des deux options n'est implémentée.");
        }

        if (caller.IsInRole(HbaRoles.MerchantStaff))
        {
            throw new ForbiddenException("Un employé de commerçant ne crée pas de livraison.");
        }

        if (caller.IsInRole(HbaRoles.Customer))
        {
            return new Orderer(PartnerIds.Internal, caller.SubjectId, MerchantId: command.MerchantId);
        }

        throw new ForbiddenException("Ce rôle ne peut pas créer de livraison.");
    }

    private static string BuildIdempotencyKey(Orderer orderer, string key) => $"{orderer.PartnerId}:{key}";

    private static Location ToLocation(LocationInput input)
        => Location.Create(
            GeoPoint.Create(input.Latitude, input.Longitude),
            input.Landmark,
            input.Phone,
            input.ContactName,
            input.Notes);

    private sealed record Orderer(string PartnerId, string? CustomerId, string? MerchantId);
}
