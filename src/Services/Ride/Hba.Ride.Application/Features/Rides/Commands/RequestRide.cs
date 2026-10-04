using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Ride.Application.Authorization;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Application.Ports;
using Hba.Ride.Domain.Exceptions;
using Hba.Ride.Domain.Results;
using Hba.Ride.Domain.Rides;

namespace Hba.Ride.Application.Features.Rides.Commands;

/// <summary>
/// Demande de course.
/// </summary>
///
/// <remarks>
/// AUCUN MONTANT N'EST ACCEPTÉ ICI, SEULEMENT UN IDENTIFIANT DE DEVIS. C'est la
/// seule protection qui tienne : un téléphone modifié peut envoyer ce qu'il veut
/// dans un corps de requête, il ne peut pas fabriquer un devis que Pricing a
/// émis. Le prix, la distance et la durée viennent donc de Pricing, jamais de
/// l'appelant.
///
/// LE CLIENT NE VIENT PAS NON PLUS DU CORPS. Il est lu dans le jeton que ce
/// service a validé : sinon n'importe qui commanderait au nom d'un autre.
/// </remarks>
public sealed record RequestRideCommand(
    string QuoteId,
    double PickupLatitude,
    double PickupLongitude,
    double DestinationLatitude,
    double DestinationLongitude,
    VehicleType VehicleType,
    string IdempotencyKey,

    // LE NUMERO QUI PAIE, FACULTATIF. Vide, c'est celui du jeton qui sert. Il
    // ne fait que preremplir la page du fournisseur, ou le passager peut en
    // saisir un autre.
    string PayerPhone) : ICommand<Result<RideRequestedView>>;

public sealed class RequestRideHandler(
    IRideRepository rides,
    IPricingClient pricing,
    IRidePaymentClient payment,
    ICallerContext caller,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RequestRideCommand, Result<RideRequestedView>>
{
    private const string IdempotencyScope = "ride.request";

    public async Task<Result<RideRequestedView>> HandleAsync(
        RequestRideCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.IsAuthenticated || string.IsNullOrWhiteSpace(caller.SubjectId))
        {
            return Result.Refus<RideRequestedView>(
                RideAccess.Forbidden, "Commander une course demande un compte.");
        }

        if (string.IsNullOrWhiteSpace(command.QuoteId))
        {
            return Result.Refus<RideRequestedView>(
                RideErrorCodes.ValidationFailed, "Une course se commande sur un devis.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Result.Refus<RideRequestedView>(
                RideErrorCodes.ValidationFailed, "Une cle d'idempotence est exigee.");
        }

        // LE REJEU REND LA COURSE DEJA CREEE. Un reseau coupe entre l'envoi et la
        // reponse ne doit pas produire deux courses — ni deux devis consommes.
        //
        // C'EST DESORMAIS LA SEULE PROTECTION CONTRE LE DOUBLE APPUI, puisqu'un
        // client peut avoir plusieurs courses ouvertes : rien d'autre ne
        // distingue « il a appuye deux fois » de « il commande une seconde
        // course ». La cle doit donc etre generee par ecran de commande, pas par
        // session.
        var connue = await idempotency
            .TryGetResultAsync(IdempotencyScope, Cle(command), cancellationToken)
            .ConfigureAwait(false);

        if (connue is not null && Guid.TryParse(connue, out var dejaCreee))
        {
            var existante = await rides.GetByIdAsync(dejaCreee, cancellationToken).ConfigureAwait(false);

            if (existante is not null)
            {
                // LE REJEU REND LA MEME ADRESSE DE PAIEMENT, et c'est pour cela que
                // la cle passee a Payment est celle de la course : Payment reconnait
                // son propre rejeu et rend l'intention deja ouverte, avec son
                // adresse. Sans cela, un passager qui reessaie se retrouverait avec
                // deux transactions ouvertes pour une seule course.
                var rouverte = await OuvrirLePaiementAsync(existante, command, cancellationToken)
                    .ConfigureAwait(false);

                return Result.Ok(new RideRequestedView(
                    RideView.From(existante), rouverte.PaymentIntentId, rouverte.RedirectUrl));
            }
        }

        var rideId = Guid.CreateVersion7();

        // LE DEVIS EST CONSOMME AVANT QUE LA COURSE N'EXISTE, et Pricing verifie
        // qu'il correspond A CE TRAJET. Creer d'abord puis consommer laisserait
        // une course sans prix si le devis etait expire.
        var devis = await pricing
            .ConsumeQuoteAsync(
                command.QuoteId,
                rideId,
                command.PickupLatitude,
                command.PickupLongitude,
                command.DestinationLatitude,
                command.DestinationLongitude,
                cancellationToken)
            .ConfigureAwait(false);

        var creation = RideAggregate.Request(
            rideId,
            caller.SubjectId,
            command.PickupLatitude,
            command.PickupLongitude,
            command.DestinationLatitude,
            command.DestinationLongitude,
            command.VehicleType,
            devis.DistanceMeters,
            devis.DurationSeconds,
            devis.Total,
            devis.DriverEarning,
            devis.CancellationFee,
            caller.ToActor(),
            clock.UtcNow);

        if (creation.Failed)
        {
            return Result.Refus<RideRequestedView>(creation.Error.Code, creation.Error.Message);
        }

        var course = creation.Value;

        rides.Add(course);

        // LE PAIEMENT S'OUVRE AVANT LE COMMIT, ET L'ORDRE EST CELUI DE DELIVERY.
        //
        // Si l'enregistrement echoue ensuite, une transaction reste ouverte chez le
        // fournisseur sans course en face : elle n'est jamais payee et se solde
        // d'elle-meme. L'inverse — enregistrer d'abord — laisserait une course sans
        // adresse de paiement, donc un passager devant un ecran vide.
        var paiement = await OuvrirLePaiementAsync(course, command, cancellationToken).ConfigureAwait(false);

        course.AttachPaymentIntent(paiement.PaymentIntentId);

        await idempotency
            .RememberAsync(IdempotencyScope, Cle(command), rideId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Ok(new RideRequestedView(
            RideView.From(course), paiement.PaymentIntentId, paiement.RedirectUrl));
    }

    /// <summary>
    /// La cle est portee par le client, pas seulement par la requete.
    ///
    /// DEUX CLIENTS PEUVENT ENVOYER LA MEME CLE — un identifiant genere par une
    /// application mal ecrite, ou simplement « 1 ». Sans le sujet dans la cle, le
    /// second recevrait la course du premier, avec son trajet et son prix.
    /// </summary>
    private string Cle(RequestRideCommand command)
        => $"{caller.SubjectId}:{command.IdempotencyKey}";

    /// <summary>
    /// Ouvre — ou retrouve — le paiement de cette course.
    /// </summary>
    ///
    /// <remarks>
    /// LA CLE EST L'IDENTIFIANT DE LA COURSE, ET C'EST CE QUI REND L'APPEL
    /// REJOUABLE. Payment est idempotent sur cette cle : le second appel ne cree
    /// rien et rend l'intention deja ouverte. C'est ce qui permet a un passager de
    /// reessayer sans qu'une seconde demande parte sur son telephone, et sans que
    /// nous tenions une seconde table de correspondance.
    ///
    /// LE TELEPHONE VIENT DU CORPS QUAND IL Y EN A UN, SINON DU JETON. Il ne
    /// fait que preremplir la page du fournisseur : le passager peut en saisir un
    /// autre, et c'est celui-la qui sera debite. Le champ existe parce que le
    /// compte mobile money n'est pas le compte HBA — on s'inscrit avec le
    /// telephone qu'on porte et on paie avec celui qui a de l'argent dessus.
    ///
    /// IL A ETE OBLIGATOIRE PENDANT UNE MATINEE, le 3 octobre 2026, quand la
    /// demande devait partir directement sur ce numero-la sans page ou le
    /// corriger. Le fournisseur refuse cette poussee sur notre compte.
    /// </remarks>
    private Task<PaymentIntentOpened> OuvrirLePaiementAsync(
        RideAggregate course,
        RequestRideCommand command,
        CancellationToken cancellationToken)
        => payment.OpenAsync(
            course.Id.ToString(),
            course.Id,
            course.CustomerId,
            command.PayerPhone,
            course.Estimate.Price.Amount,
            cancellationToken);
}
