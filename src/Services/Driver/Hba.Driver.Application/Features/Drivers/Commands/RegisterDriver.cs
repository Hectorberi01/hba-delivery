using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Features.Drivers.Commands;

/// <summary>
/// Creation du profil livreur.
///
/// ELLE NE VIENT PAS D'UNE APPLICATION, mais de l'evenement AccountRegistered
/// d'Identity, quand le compte porte le role driver. Le contrat gRPC n'expose
/// d'ailleurs aucune methode d'inscription : le compte fait foi, et le profil
/// reprend son identifiant — un seul identifiant traverse tous les services.
///
/// REJOUABLE : Kafka livre au moins une fois. Un profil deja cree est rendu
/// tel quel, sans erreur et sans second evenement DriverRegistered.
/// </summary>
public sealed record RegisterDriverCommand(
    Guid AccountId,
    string DisplayName,
    string Phone) : ICommand<DriverView>;

public sealed class RegisterDriverHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RegisterDriverCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(RegisterDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await drivers.GetByIdAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return DriverView.From(existing);
        }

        var driver = DriverAggregate.Register(
            command.AccountId,
            command.DisplayName,
            command.Phone,
            // LE VEHICULE N'EST PAS CONNU A L'INSCRIPTION. Le compte ne le
            // porte pas, et le referentiel ne dit pas comment il est declare.
            // La moto par defaut evite un profil inutilisable ; la declaration
            // du vehicule reste a construire.
            Vehicle.Unknown,
            Actor.Driver(command.AccountId.ToString()),
            clock.UtcNow);

        drivers.Add(driver);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DriverView.From(driver);
    }
}
