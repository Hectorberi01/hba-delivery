using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Decision sur le dossier d'un livreur. RESERVEE A L'ADMINISTRATION : c'est
/// elle qui autorise quelqu'un a transporter les colis des clients.
/// </summary>
public sealed record ReviewDriverKycCommand(Guid DriverId, bool Approved, string? Reason) : ICommand<DriverView>;

public sealed record SuspendDriverCommand(Guid DriverId, string Reason) : ICommand<DriverView>;

public sealed class ReviewDriverKycHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<ReviewDriverKycCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(ReviewDriverKycCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        EnsureAdmin(caller);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        driver.ReviewKyc(command.Approved, command.Reason, caller.SubjectId, caller.ToActor(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // UN REFUS N'EFFACE PLUS LA POSITION, ET IL N'A PLUS A LE FAIRE.
        //
        // Ce handler appelait locations.RemoveAsync jusqu'au 1er octobre 2026, parce
        // qu'une position laissee en place faisait apparaitre le livreur dans les vagues.
        // Depuis que l'eligibilite se lit dans la base et la position dans Location, une
        // vague croise toujours les deux : un dossier refuse est ecarte par le croisement,
        // quel que soit l'etat de l'index geographique.
        //
        // LE POINT S'EFFACE TOUT SEUL au bout de la fenetre de fraicheur. Pendant ces
        // deux minutes, la carte de l'exploitation le montre encore — et c'est plutot
        // utile le jour ou l'on refuse le dossier de quelqu'un qui est en train de rouler.

        return DriverView.From(driver);
    }

    internal static void EnsureAdmin(ICallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Seule l'administration decide d'un dossier livreur.");
        }
    }
}

/// <summary>
/// Ops leve une suspension. Le livreur reste hors ligne : il revient lui-meme.
/// </summary>
public sealed record ReinstateDriverCommand(Guid DriverId, string Reason) : ICommand<DriverView>;

public sealed class ReinstateDriverHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<ReinstateDriverCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(ReinstateDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ReviewDriverKycHandler.EnsureAdmin(caller);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        driver.Reinstate(command.Reason, caller.ToActor(), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // RIEN A FAIRE SUR LA POSITION, ET PLUS AUCUN DES TROIS HANDLERS DE CE FICHIER
        // N'Y TOUCHE. Avant le 1er octobre 2026, la suspension effacait le point et la
        // reinstallation le laissait revenir avec le battement du livreur. Aujourd'hui
        // aucun des deux ne touche a l'index : c'est l'etat en base qui decide, a chaque
        // vague, et la position suit le telephone.
        return DriverView.From(driver);
    }
}

public sealed class SuspendDriverHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<SuspendDriverCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(SuspendDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ReviewDriverKycHandler.EnsureAdmin(caller);

        var driver = await drivers.GetByIdAsync(command.DriverId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livreur", command.DriverId.ToString());

        driver.Suspend(command.Reason, caller.ToActor(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // MEME RAISON QUE POUR LE REFUS DE DOSSIER : la suspension n'efface plus la
        // position. L'etat suspendu en base suffit a ecarter le livreur de toute vague,
        // puisque le croisement passe par la base a chaque fois.
        //
        // CE SERVICE N'A PLUS AUCUN MOYEN D'EFFACER UN POINT, d'ailleurs — il n'a plus de
        // client vers Location, et c'est voulu : lui en donner un pour ce geste-la aurait
        // rouvert une dependance de Driver vers les positions pour un nettoyage que la
        // fenetre de fraicheur fait seule en deux minutes.

        return DriverView.From(driver);
    }
}
