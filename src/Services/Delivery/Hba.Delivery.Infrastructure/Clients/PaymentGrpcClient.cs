using Grpc.Core;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Payment.V1;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Infrastructure.Clients;

internal sealed class PaymentGrpcClient(PaymentService.PaymentServiceClient client) : IPaymentClient
{
    public async Task<PaymentIntentResult> CreateIntentAsync(
        string idempotencyKey,
        Guid deliveryId,
        string payerId,
        string payerPhone,
        MoneyXof amount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amount);

        try
        {
            var intent = await client.CreatePaymentIntentAsync(
                new CreatePaymentIntentRequest
                {
                    IdempotencyKey = idempotencyKey,
                    ReferenceId = deliveryId.ToString(),

                    // CE SERVICE NE PAIE QUE DES LIVRAISONS, et le dire est devenu
                    // obligatoire : Payment regle aussi des courses de personnes
                    // depuis le 1er octobre 2026, et il refuse une intention sans
                    // nature plutot que de supposer le colis.
                    ServiceType = Contracts.Common.V1.ServiceType.Delivery,

                    PayerId = payerId,

                    // LE NUMERO NE FAIT QUE PREREMPLIR LA PAGE. Vide, Payment
                    // reprend celui du jeton ; et de toute facon le client peut
                    // en saisir un autre sur l'ecran du fournisseur.
                    PayerPhone = payerPhone,
                    Amount = new Contracts.Common.V1.Money
                    {
                        Amount = amount.Amount,
                        Currency = MoneyXof.CurrencyCode,
                    },
                    Method = PaymentMethod.MobileMoney,
                },
                cancellationToken: cancellationToken);

            return new PaymentIntentResult(intent.Id, intent.RedirectUrl);
        }
        catch (RpcException ex)
        {
            throw new DomainException(
                "PAYMENT_INTENT_FAILED",
                "Impossible de créer l'intention de paiement.",
                ex);
        }
    }
}
