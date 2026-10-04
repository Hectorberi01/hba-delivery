using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Passage en ligne : la PRESENCE DECLAREE.
/// </summary>
///
/// <remarks>
/// IL NE POSE PLUS DE POSITION, ET CE N'EST PAS UNE REGRESSION. Jusqu'au 1er octobre
/// 2026 ce handler ecrivait la position initiale dans Redis dans la foulee, parce qu'un
/// livreur disponible sans position n'existait pour aucune vague. Depuis que les
/// positions ont quitte ce service, c'est la PASSERELLE qui enchaine les deux appels —
/// Driver pour l'etat, Location pour le premier point.
///
/// DEUX PRESENCES, ET IL NE FAUT PAS LES CONFONDRE. « Je me declare disponible » est un
/// etat METIER : il se decide, il s'audite, il survit a un telephone eteint, et il vit
/// donc dans l'agregat. « Mon telephone emet » est un fait TECHNIQUE, qui s'evapore tout
/// seul au bout de la fenetre de fraicheur, et qui vit dans Location. « Disponible »,
/// pour une vague, reste la conjonction des deux — c'est ce que croise l'appelant.
///
/// CE QUI ARRIVE SI LE SECOND APPEL ECHOUE : le livreur est en ligne en base et absent
/// de la carte, donc aucune vague ne le trouve. C'est exactement l'etat d'un livreur
/// dont le telephone n'a pas encore envoye son premier point — un etat normal, qui se
/// resout au battement suivant, en quelques secondes.
/// </remarks>
public sealed record GoOnlineCommand(Guid DriverId) : ICommand<DriverView>;

public sealed record GoOfflineCommand(Guid DriverId) : ICommand<DriverView>;

public sealed class GoOnlineHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<GoOnlineCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(GoOnlineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        driver.GoOnline(caller.ToActor(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DriverView.From(driver);
    }
}

public sealed class GoOfflineHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<GoOfflineCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(GoOfflineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DriverAuthorization.EnsureSelfOrBackOffice(caller, command.DriverId);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        driver.GoOffline(caller.ToActor(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // LA POSITION NE PART PLUS AVEC LUI, ET CE N'EST PLUS GRAVE.
        //
        // Avant le 1er octobre 2026, ce handler effacait le point dans la foulee, et
        // l'oublier aurait laisse un livreur rentre chez lui apparaitre dans une
        // recherche. Aujourd'hui la vague croise toujours la base, qui le voit hors
        // ligne : un point qui traine ne produit plus aucune offre par lui-meme.
        //
        // LA PASSERELLE L'EFFACE QUAND MEME, pour la carte de l'exploitation : sans
        // cela, ops verrait encore pendant deux minutes quelqu'un qui vient de se
        // deconnecter. C'est de l'hygiene d'affichage, plus une decision — et c'est
        // precisement pour cela qu'elle peut vivre a la passerelle sans risque.

        return DriverView.From(driver);
    }
}

/// <summary>
/// Autorisation cote service (ADR 0007) : un livreur n'agit que sur son propre
/// profil. Le back-office peut agir pour lui — un livreur dont le telephone
/// est eteint doit pouvoir etre repasse hors ligne par ops.
/// </summary>
internal static class DriverAuthorization
{
    public static void EnsureSelfOrBackOffice(ICallerContext caller, Guid driverId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.IsInRole(HbaRoles.Admin) || caller.IsInRole(HbaRoles.Ops))
        {
            return;
        }

        var self = caller.DriverId ?? caller.SubjectId;

        if (!string.Equals(self, driverId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Un livreur n'agit que sur son propre profil.");
        }
    }
}
