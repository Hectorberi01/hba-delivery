using Hba.BuildingBlocks.Application.Messaging;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;
using Hba.Driver.Domain.ValueObjects;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// « Puis-je proposer une course a CE livreur, depuis CE point ? »
///
/// CE N'EST PAS FindAvailableNearby AVEC UN FILTRE. La recherche de proximite
/// repond « les N plus proches d'ici » : aucun rayon, aucune limite ne
/// garantit qu'un livreur nomme figure dans sa reponse, et augmenter les deux
/// jusqu'a ce qu'il apparaisse reviendrait a balayer toute la ville pour
/// interroger une seule ligne. L'affectation manuelle pose une autre question,
/// elle merite sa lecture.
///
/// LE VERDICT VIENT DE LA MEME LECTURE QUE LE DISPATCH. FindAvailableAsync
/// est ce qui decide, pour une vague, qui peut recevoir une offre ; on
/// l'appelle ici sur un seul identifiant plutot que de reecrire ses criteres.
/// Deux definitions de « joignable » finiraient par diverger, et l'ecart se
/// verrait le jour ou la console proposerait une course que le moteur refuse.
///
/// AUCUN CONTROLE DE ROLE ICI, comme pour FindAvailableNearby et contrairement
/// a ListDriverPositions. La difference n'est pas un oubli : cette lecture
/// n'est pas exposee a la passerelle. Elle n'est appelee que par Dispatch,
/// avec un jeton de service, et l'autorisation de l'operateur humain a ete
/// verifiee la-bas, dans le handler qui pose l'offre. La carte, elle, part
/// bien d'un navigateur, et doit donc se defendre seule.
/// </summary>
public sealed record CheckDriverAvailabilityQuery(
    Guid DriverId,
    double Latitude,
    double Longitude) : IQuery<DriverAvailabilityView>;

public sealed class CheckDriverAvailabilityHandler(
    IDriverRepository drivers,
    IDriverLocationStore locations)
    : IQueryHandler<CheckDriverAvailabilityQuery, DriverAvailabilityView>
{
    public const string NotFound = "DRIVER_NOT_FOUND";

    public const string NotVerified = "DRIVER_NOT_VERIFIED";

    public const string NotAvailable = "DRIVER_NOT_AVAILABLE";

    public const string PositionStale = "DRIVER_POSITION_STALE";

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
                OperationalStatus.Unspecified,
                null);
        }

        var eligibles = await drivers
            .FindAvailableAsync([driver.Id], null, cancellationToken)
            .ConfigureAwait(false);

        // L'ETAT SE LIT AVANT LA POSITION, ET L'ORDRE PORTE UN SENS. Un
        // livreur suspendu qui laisse tourner son telephone a une position
        // parfaitement fraiche ; annoncer « position perimee » serait exact
        // sur la forme et faux sur le fond.
        var motif = eligibles.Count == 0 ? Refus(driver) : null;

        var position = await locations
            .MeasureAsync(driver.Id, GeoPoint.Create(query.Latitude, query.Longitude), cancellationToken)
            .ConfigureAwait(false);

        if (motif is null && position is null)
        {
            motif = PositionStale;
        }

        return new DriverAvailabilityView(
            driver.Id,
            driver.DisplayName,
            motif is null,
            motif,
            driver.VerificationStatus,
            driver.OperationalStatus,
            position);
    }

    /// <summary>
    /// Traduit un refus en motif. LE DOSSIER PASSE AVANT L'ETAT : un livreur
    /// non verifie est hors ligne par construction, et dire « il n'est pas en
    /// ligne » enverrait l'exploitation lui telephoner au lieu d'aller
    /// regarder son dossier.
    /// </summary>
    private static string Refus(DriverAggregate driver)
        => driver.VerificationStatus != VerificationStatus.Verified ? NotVerified : NotAvailable;
}
