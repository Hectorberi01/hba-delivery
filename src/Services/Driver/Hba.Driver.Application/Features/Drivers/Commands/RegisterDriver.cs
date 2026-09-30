using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.BuildingBlocks.Security;
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

/// <summary>
/// Cree le profil livreur du porteur du jeton, s'il n'existe pas deja.
/// </summary>
///
/// <remarks>
/// LE RATTRAPAGE DE L'EVENEMENT PERDU, ET IL N'EXISTAIT QUE POUR LE CLIENT.
///
/// Le chemin normal est un evenement : Identity publie AccountRegistered,
/// Driver le consomme et cree le profil. Cet evenement se perd — Kafka ne
/// rejoue pas pour un groupe de consommateurs qui n'existait pas encore au
/// moment du passage, et c'est arrive pour de bon le 27 septembre. Le client
/// avait recu sa porte de secours (POST /api/client/v1/me) ; le livreur, non.
///
/// CE QUE CELA DONNAIT : un compte Identity valide, un jeton portant driver_id,
/// et un GET /me qui repondait 404 A VIE. L'accueil affichait « DOSSIER EN
/// COURS » et renvoyait vers un ecran dont toutes les routes echouaient de la
/// meme facon. Le livreur ne pouvait rien faire, et rien ne lui disait pourquoi.
///
/// AUCUN PARAMETRE, ET CE N'EST PAS UN OUBLI. Le nom et le telephone viennent du
/// JETON que ce service a lui-meme valide. Les accepter dans la requete
/// laisserait quelqu'un creer un profil livreur sous l'identite d'un autre.
///
/// IDEMPOTENTE : appelee deux fois, elle rend deux fois la meme fiche.
/// </remarks>
public sealed record EnsureDriverCommand : ICommand<DriverView>;

public sealed class EnsureDriverHandler(
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<EnsureDriverCommand, DriverView>
{
    public async Task<DriverView> HandleAsync(EnsureDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Seul un livreur cree son propre profil.");
        }

        var identifiant = caller.DriverId ?? caller.SubjectId;

        if (!Guid.TryParse(identifiant, out var driverId))
        {
            throw new ForbiddenException("Le jeton ne porte pas d'identifiant de livreur exploitable.");
        }

        var existant = await drivers.GetByIdAsync(driverId, cancellationToken).ConfigureAwait(false);
        if (existant is not null)
        {
            return DriverView.From(existant);
        }

        // LE TELEPHONE EST OBLIGATOIRE ET IL VIENT DU JETON. Sans lui, le profil
        // naitrait muet : c'est par ce numero que l'exploitation joint le
        // livreur, et rien ne permettrait de le retrouver ensuite.
        if (string.IsNullOrWhiteSpace(caller.Phone))
        {
            throw new ForbiddenException("Le jeton ne porte pas de téléphone : reconnectez-vous.");
        }

        var driver = DriverAggregate.Register(
            driverId,
            string.IsNullOrWhiteSpace(caller.DisplayName) ? caller.Phone! : caller.DisplayName!,
            caller.Phone!,
            Vehicle.Unknown,
            Actor.Driver(identifiant),
            clock.UtcNow);

        drivers.Add(driver);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DriverView.From(driver);
    }
}

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
