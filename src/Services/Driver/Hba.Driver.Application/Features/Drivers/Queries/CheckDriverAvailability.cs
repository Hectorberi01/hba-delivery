using Hba.BuildingBlocks.Application.Messaging;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// « CE livreur peut-il recevoir une offre ? »
/// </summary>
///
/// <remarks>
/// CE N'EST PAS <see cref="FilterAvailableDriversHandler"/> AVEC UN SEUL NOM, meme si
/// les deux posent la meme question au depot. L'ecart est dans la REPONSE : le filtre
/// rend ceux qui passent et se taît sur les autres, ce qui suffit a une vague ;
/// l'affectation manuelle, elle, a besoin de savoir POURQUOI celui qu'on a designe ne
/// passe pas. Un bouton grise sans motif envoie l'exploitation chercher au hasard.
///
/// LE VERDICT VIENT DE LA MEME LECTURE QUE LE DISPATCH. <c>FindAvailableAsync</c> est
/// ce qui decide, pour une vague, qui peut recevoir une offre ; on l'appelle ici sur un
/// seul identifiant plutot que de reecrire ses criteres. Deux definitions de
/// « joignable » finiraient par diverger, et l'ecart se verrait le jour ou la console
/// proposerait une course que le moteur refuse.
///
/// IL NE REGARDE PLUS LA POSITION, ET NE RECOIT PLUS DE POINT DE REFERENCE. Jusqu'au
/// 1er octobre 2026, ce handler lisait aussi Redis et pouvait repondre
/// DRIVER_POSITION_STALE. Depuis que les positions ont quitte ce service, ce motif
/// appartient a celui qui croise : Dispatch interroge Location en plus de Driver, et
/// conclut lui-meme que le telephone est eteint.
///
/// L'ORDRE DE LECTURE QUE CELA PRESERVE. L'ancien handler lisait l'etat AVANT la
/// position, parce qu'un livreur suspendu qui laisse tourner son telephone a une
/// position parfaitement fraiche : annoncer « position perimee » aurait ete exact sur
/// la forme et faux sur le fond. Cette precedence doit etre tenue par l'appelant
/// maintenant, et elle l'est — le motif de Driver gagne sur celui de Location.
///
/// AUCUN CONTROLE DE ROLE ICI, contrairement a
/// <see cref="GetDriverSummariesHandler"/>. La difference n'est pas un oubli : cette
/// lecture n'est pas exposee a la passerelle. Elle n'est appelee que par Dispatch, avec
/// un jeton de service, et l'autorisation de l'operateur humain a ete verifiee la-bas,
/// dans le handler qui pose l'offre. La carte, elle, part bien d'un navigateur, et doit
/// donc se defendre seule.
/// </remarks>
public sealed record CheckDriverAvailabilityQuery(Guid DriverId) : IQuery<DriverAvailabilityView>;

public sealed class CheckDriverAvailabilityHandler(IDriverRepository drivers)
    : IQueryHandler<CheckDriverAvailabilityQuery, DriverAvailabilityView>
{
    public const string NotFound = "DRIVER_NOT_FOUND";

    public const string NotVerified = "DRIVER_NOT_VERIFIED";

    public const string NotAvailable = "DRIVER_NOT_AVAILABLE";

    public async Task<DriverAvailabilityView> HandleAsync(
        CheckDriverAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var driver = await drivers.GetByIdAsync(query.DriverId, cancellationToken).ConfigureAwait(false);

        if (driver is null)
        {
            return new DriverAvailabilityView(
                query.DriverId,
                string.Empty,
                false,
                NotFound,
                VerificationStatus.Unspecified,
                OperationalStatus.Unspecified);
        }

        var eligibles = await drivers
            .FindAvailableAsync([driver.Id], null, cancellationToken)
            .ConfigureAwait(false);

        var motif = eligibles.Count == 0 ? Refus(driver) : null;

        return new DriverAvailabilityView(
            driver.Id,
            driver.DisplayName,
            motif is null,
            motif,
            driver.VerificationStatus,
            driver.OperationalStatus);
    }

    /// <summary>
    /// Traduit un refus en motif. LE DOSSIER PASSE AVANT L'ETAT : un livreur non
    /// verifie est hors ligne par construction, et dire « il n'est pas en ligne »
    /// enverrait l'exploitation lui telephoner au lieu d'aller regarder son dossier.
    /// </summary>
    private static string Refus(DriverAggregate driver)
        => driver.VerificationStatus != VerificationStatus.Verified ? NotVerified : NotAvailable;
}
