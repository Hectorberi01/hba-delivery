using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.BuildingBlocks.Storage;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Application.Features.Drivers.Commands;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// Le dossier d'un livreur, avec une URL signee par piece.
///
/// RESERVE AU PROPRIETAIRE ET A OPS. C'est le tableau de visibilite du
/// referentiel : « Documents KYC livreur — Les siens » pour le livreur,
/// « Oui (URL signee) » pour l'admin, « Non » pour tous les autres.
/// </summary>
public sealed record GetDriverApplicationQuery(Guid DriverId) : IQuery<DriverApplicationView>;

public sealed class GetDriverApplicationHandler(
    IDriverRepository drivers,
    IObjectStore objets,
    IPersonalDataReadLog lectures,
    ICallerContext caller,
    IClock clock) : IQueryHandler<GetDriverApplicationQuery, DriverApplicationView>
{
    public async Task<DriverApplicationView> HandleAsync(
        GetDriverApplicationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, query.DriverId);

        var driver = await drivers.GetByIdAsync(query.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", query.DriverId.ToString());

        // CONSIGNE QUAND C'EST LE BACK-OFFICE QUI REGARDE, pas quand le
        // livreur ouvre son propre dossier. L'application le recharge a
        // chaque passage sur l'ecran : consigner ces ouvertures-la remplirait
        // le journal de lignes ou personne ne regarde la donnee de personne,
        // et noierait celles qui comptent.
        //
        // AVANT LES URL SIGNEES, ET VOLONTAIREMENT. La ligne du journal doit
        // exister meme si la fabrication des URL echoue ensuite : les cles
        // ont ete lues, et c'est cela qu'on trace.
        if (caller.Roles.Overlaps(HbaRoles.BackOffice))
        {
            await lectures
                .RecordAsync(PersonalDataReadKind.DriverApplication, driver.Id.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }

        // L'ECHEANCE EST CALCULEE ICI, PAS DEMANDEE AU STOCKAGE : le port ne
        // rend qu'une URL, et l'ecran a besoin de savoir quand elle expire
        // pour recharger plutot que d'afficher une image cassee.
        var emisA = clock.UtcNow;

        var pieces = new List<DocumentView>(driver.Documents.Count);

        foreach (var piece in driver.Documents.OrderBy(d => d.Type))
        {
            var url = await objets.GetReadUrlAsync(piece.ObjectKey, cancellationToken).ConfigureAwait(false);

            pieces.Add(new DocumentView(
                piece.Type,
                piece.UploadedAt,
                piece.SizeBytes,
                piece.ContentType,
                url,
                emisA.Add(objets.ReadUrlLifetime)));
        }

        Uri? photo = null;

        if (!string.IsNullOrWhiteSpace(driver.ProfilePhotoKey))
        {
            photo = await objets.GetReadUrlAsync(driver.ProfilePhotoKey, cancellationToken).ConfigureAwait(false);
        }

        var manquantes = driver.PiecesManquantes;

        return new DriverApplicationView(
            driver.Id,
            driver.VerificationStatus,
            driver.StatusReason ?? string.Empty,
            driver.SubmittedAt,
            pieces,
            manquantes,
            driver.Vehicle.Type,
            driver.Vehicle.Plate,
            driver.Vehicle.CapacityGrams,
            photo,

            // MEME REGLE QUE L'AGREGAT, exprimee une fois de plus — et c'est
            // le seul endroit ou c'est acceptable : l'ecran doit savoir s'il
            // peut proposer le bouton AVANT de l'appuyer. Une divergence se
            // verrait aussitot, puisque SubmitForReview refuserait.
            manquantes.Count == 0
                && !string.IsNullOrWhiteSpace(driver.Vehicle.Plate)
                && driver.DossierModifiable);
    }
}
