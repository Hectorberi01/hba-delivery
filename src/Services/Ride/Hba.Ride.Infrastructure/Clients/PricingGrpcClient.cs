using Grpc.Core;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc;
using Hba.Contracts.Pricing.V1;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.ValueObjects;
using ServiceType = Hba.Contracts.Common.V1.ServiceType;

namespace Hba.Ride.Infrastructure.Clients;

/// <summary>
/// Adaptateur vers le service Pricing. La couche Application n'a jamais à
/// connaître gRPC.
/// </summary>
///
/// <remarks>
/// LES REFUS SORTENT EN EXCEPTIONS, PAS EN <c>Result</c>, ET CE N'EST PAS UNE
/// ENTORSE AU POINT 35. La convention <c>Result</c> couvre le flux métier de
/// Ride : ce que la course elle-même refuse. Un devis expiré ou établi pour un
/// autre trajet est un refus de PRICING, qui le dit déjà en statut gRPC avec son
/// propre code ; le retraduire en <c>Result</c> obligerait chaque commande à
/// reporter un code qu'elle ne possède pas, et la frontière traduit déjà ces
/// exceptions exactement comme elle le fait pour Delivery.
///
/// LE TRAJET EST PASSÉ À PRICING, et c'est ce qui empêche un devis de trois
/// cents mètres de payer une course de vingt kilomètres. La leçon a été apprise
/// aux dépens de Delivery.
/// </remarks>
internal sealed class PricingGrpcClient(PricingService.PricingServiceClient client) : IPricingClient
{
    public async Task<QuoteSnapshot> ConsumeQuoteAsync(
        string quoteId,
        Guid rideId,
        double pickupLatitude,
        double pickupLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken)
    {
        Quote devis;

        try
        {
            devis = await client.ConsumeQuoteAsync(
                new ConsumeQuoteRequest
                {
                    QuoteId = quoteId,

                    // LE CHAMP S'APPELLE « delivery_id » ET REÇOIT UN
                    // IDENTIFIANT DE COURSE. Le contrat de Pricing a été écrit
                    // quand seul Delivery consommait des devis ; le renommer en
                    // « consumer_id » toucherait Pricing et Delivery pour une
                    // question de vocabulaire. Pricing ne fait que consigner
                    // cette valeur, donc rien ne casse — mais la dette est
                    // réelle et mérite d'être levée un jour.
                    DeliveryId = rideId.ToString(),

                    // LA NATURE EST DITE, ET C'EST ELLE QUI FAIT REFUSER UN DEVIS
                    // DE COLIS. Sans ce champ, Pricing retomberait sur « colis » —
                    // son repli pour les appelants anciens — et une course se
                    // paierait au tarif d'un paquet : ni le même plancher, ni la
                    // même part du chauffeur. Ride est né après ce champ, il n'a
                    // aucune raison de le laisser vide.
                    ServiceType = ServiceType.Ride,
                    Pickup = Point(pickupLatitude, pickupLongitude),
                    Dropoff = Point(destinationLatitude, destinationLongitude),
                },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new NotFoundException("Devis", quoteId);
        }
        catch (RpcException ex) when (CodeMetier(ex) == "QUOTE_TRIP_MISMATCH")
        {
            // LE TRAJET NE CORRESPOND PAS, ET CE N'EST PAS « DEVIS INUTILISABLE ».
            // Les deux sortent en FAILED_PRECONDITION mais ne demandent pas la
            // même chose au passager : un devis expiré se redemande tel quel, un
            // trajet qui a changé exige de refaire la saisie.
            throw new DomainException(
                "QUOTE_TRIP_MISMATCH",
                "Le devis a été établi pour un autre trajet. Reprenez les adresses pour obtenir un nouveau prix.");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            throw new DomainException("QUOTE_NOT_USABLE", "Le devis est expiré ou a déjà été consommé.");
        }

        return new QuoteSnapshot(
            devis.Id,
            devis.TariffVersion,
            Montant(devis.Total),

            // LE CHAMP EXISTAIT DEJA DANS LE DEVIS, et personne ne le lisait de ce
            // cote : l'offre du chauffeur affichait donc zero franc. Pricing le
            // calcule depuis DriverShareBasisPoints de la grille en vigueur.
            Montant(devis.DriverEarning),

            // UN DEVIS SANS RETENUE N'EST PAS UNE ERREUR : la grille de la zone n'en
            // prevoit pas, et l'annulation y reste gratuite. Montant rend zero.
            Montant(devis.CancellationFee),

            devis.DistanceMeters,
            devis.DurationSeconds);
    }

    /// <summary>
    /// Le code métier voyage dans les trailers, posé par
    /// <c>ExceptionInterceptor</c>. C'est la seule façon de distinguer deux refus
    /// qui partagent le même statut gRPC.
    /// </summary>
    private static string? CodeMetier(RpcException exception)
        => exception.Trailers.GetValue(GrpcMetadataKeys.ErrorCode);

    private static Contracts.Common.V1.GeoPoint Point(double latitude, double longitude)
        => new() { Latitude = latitude, Longitude = longitude };

    /// <summary>
    /// LA DEVISE EST VÉRIFIÉE AVANT D'ÊTRE JETÉE. Le domaine de Ride ne porte
    /// aucune devise (point 35) : accepter sans regarder un montant libellé
    /// autrement qu'en XOF le transformerait silencieusement en francs.
    /// </summary>
    private static long Montant(Contracts.Common.V1.Money? money)
    {
        if (money is null)
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(money.Currency)
            && !string.Equals(money.Currency, MoneyXof.CurrencyCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "UNSUPPORTED_CURRENCY",
                $"Devise non prise en charge : {money.Currency}. Seul le XOF est accepté.");
        }

        return money.Amount;
    }
}
