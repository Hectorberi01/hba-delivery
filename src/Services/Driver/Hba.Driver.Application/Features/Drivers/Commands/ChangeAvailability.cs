using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.ValueObjects;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Passage en ligne. La position initiale est posee dans Redis dans la foulee :
/// un livreur disponible sans position n'existe pour aucune vague.
/// </summary>
public sealed record GoOnlineCommand(Guid DriverId, double Latitude, double Longitude) : ICommand<DriverView>;

public sealed record GoOfflineCommand(Guid DriverId) : ICommand<DriverView>;

public sealed class GoOnlineHandler(
    IDriverRepository drivers,
    IDriverLocationStore locations,
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

        var now = clock.UtcNow;

        driver.GoOnline(caller.ToActor(), now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // APRES LE COMMIT, PAS AVANT. Poser la position d'abord rendrait
        // joignable un livreur dont le passage en ligne vient d'etre refuse.
        await locations
            .UpsertAsync(driver.Id, GeoPoint.Create(command.Latitude, command.Longitude), now, cancellationToken)
            .ConfigureAwait(false);

        return DriverView.From(driver);
    }
}

public sealed class GoOfflineHandler(
    IDriverRepository drivers,
    IDriverLocationStore locations,
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

        // La position part avec lui : une position sans disponibilite n'a
        // aucun usage, et la garder ferait apparaitre le livreur dans une
        // recherche avant que la base ne le filtre.
        await locations.RemoveAsync(driver.Id, cancellationToken).ConfigureAwait(false);

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
