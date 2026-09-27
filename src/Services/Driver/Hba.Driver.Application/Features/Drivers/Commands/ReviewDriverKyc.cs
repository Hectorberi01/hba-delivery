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
    IDriverLocationStore locations,
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

        if (!command.Approved)
        {
            await locations.RemoveAsync(driver.Id, cancellationToken).ConfigureAwait(false);
        }

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

public sealed class SuspendDriverHandler(
    IDriverRepository drivers,
    IDriverLocationStore locations,
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

        // Une suspension qui laisse la position en place laisserait le livreur
        // apparaitre dans les vagues jusqu'a la prochaine purge.
        await locations.RemoveAsync(driver.Id, cancellationToken).ConfigureAwait(false);

        return DriverView.From(driver);
    }
}
