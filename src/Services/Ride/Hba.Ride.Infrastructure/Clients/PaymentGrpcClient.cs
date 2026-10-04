using Grpc.Core;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Payment.V1;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hba.Ride.Infrastructure.Clients;

/// <summary>
/// Adaptateur vers le service Payment.
/// </summary>
///
/// <remarks>
/// AUCUNE INTENTION RENDUE N'EST UNE PANNE, et c'est le point délicat de ce fichier.
/// Le devis a déjà été consommé quand on arrive ici : refuser la commande parce
/// que le fournisseur n'a pas répondu brûlerait le devis du passager sans lui
/// laisser ni course ni trace. On rend donc une intention vide, la course existe,
/// et elle finira en PaymentFailed — ce qui est diagnosticable, contrairement à une
/// exception qui ne laisse rien derrière elle.
///
/// UNE SEULE EXCEPTION À CETTE RÈGLE : un refus que Payment nomme. Un montant
/// invalide ou une nature manquante sont des fautes de notre côté, pas une panne du
/// fournisseur, et les taire laisserait le défaut vivre.
/// </remarks>
internal sealed class PaymentGrpcClient(
    PaymentService.PaymentServiceClient client,
    ILogger<PaymentGrpcClient> journal) : IRidePaymentClient
{
    public async Task<PaymentIntentOpened> OpenAsync(
        string idempotencyKey,
        Guid rideId,
        string customerId,
        string payerPhone,
        long amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var intention = await client.CreatePaymentIntentAsync(
                new CreatePaymentIntentRequest
                {
                    IdempotencyKey = idempotencyKey,
                    ReferenceId = rideId.ToString(),

                    // CE SERVICE NE PAIE QUE DES COURSES. Payment refuse une
                    // intention sans nature plutot que de supposer le colis.
                    ServiceType = Contracts.Common.V1.ServiceType.Ride,

                    PayerId = customerId,

                    // LE NUMERO NE FAIT QUE PREREMPLIR LA PAGE. Vide, Payment
                    // reprend celui du jeton ; et de toute facon le passager
                    // peut en saisir un autre sur l'ecran du fournisseur.
                    PayerPhone = payerPhone,
                    Amount = new Contracts.Common.V1.Money
                    {
                        Amount = amount,
                        Currency = MoneyXof.CurrencyCode,
                    },

                    // MOBILE MONEY, COMME POUR LE COLIS. La carte existe au
                    // contrat et la page hebergee la propose, mais rien ne
                    // permet encore de la choisir avant d'arriver sur la page.
                    Method = PaymentMethod.MobileMoney,
                },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new PaymentIntentOpened(intention.Id, intention.RedirectUrl);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument)
        {
            // NOTRE FAUTE, PAS CELLE DU RESEAU : montant invalide, nature manquante.
            // La taire laisserait le defaut vivre dans toutes les courses suivantes.
            throw new DomainException(
                "VALIDATION_FAILED",
                "Le paiement de la course a ete refuse : la demande est invalide.",
                exception);
        }
        catch (RpcException exception)
        {
            journal.LogError(
                exception,
                "Impossible d'ouvrir le paiement de la course {RideId} ({Statut}). "
                + "Le passager n'a aucune page a ouvrir : la course est enregistree "
                + "sans intention et finira en echec.",
                rideId,
                exception.StatusCode);

            return new PaymentIntentOpened(string.Empty, string.Empty);
        }
    }
}
