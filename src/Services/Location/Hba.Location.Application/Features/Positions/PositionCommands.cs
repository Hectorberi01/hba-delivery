using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Location.Application.Authorization;
using Hba.Location.Application.Common;
using Hba.Location.Domain.Positions;

namespace Hba.Location.Application.Features.Positions;

/// <summary>
/// Un battement de position.
/// </summary>
///
/// <remarks>
/// C'EST L'APPEL LE PLUS FREQUENT DU SYSTEME — un point toutes les quelques secondes,
/// par chauffeur en ligne — ET IL NE LIT RIEN. Pas d'agregat, pas de base, aucune
/// verification que ce chauffeur a le droit d'etre en ligne. Y ajouter une lecture
/// multiplierait les requetes par le nombre de chauffeurs actifs, pour un controle
/// que la recherche refait de toute facon : c'est Driver qui dit qui est eligible, et
/// une position qui traine sans eligibilite ne produit aucune offre.
///
/// L'HORODATAGE DU TELEPHONE NE DECIDE RIEN. Une horloge mal reglee, un point mis en
/// file pendant une coupure reseau, et la position paraitrait neuve alors qu'elle a
/// dix minutes — ou l'inverse. On le conserve pour l'analyse ; la fraicheur se fie a
/// l'heure de RECEPTION.
///
/// UN HORODATAGE ABSENT VAUT MAINTENANT, et non un refus : un telephone qui n'arrive
/// pas a lire son horloge envoie quand meme un point utile.
/// </remarks>
public sealed record UpdatePositionCommand(
    Guid DriverId,
    double Latitude,
    double Longitude,
    DateTimeOffset? CapturedAt,
    double? HeadingDegrees = null,
    double? SpeedMps = null,
    double? AccuracyMeters = null) : ICommand<bool>;

public sealed class UpdatePositionHandler(
    IPositionStore positions,
    ICallerContext caller,
    IClock clock) : ICommandHandler<UpdatePositionCommand, bool>
{
    public async Task<bool> HandleAsync(UpdatePositionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        LocationAccess.EnsureCanWrite(caller, command.DriverId);

        var recu = clock.UtcNow;

        var position = new DriverPosition(
            command.DriverId,
            GeoPoint.Create(command.Latitude, command.Longitude),
            Motion.Create(command.HeadingDegrees, command.SpeedMps, command.AccuracyMeters),
            command.CapturedAt ?? recu,
            recu);

        await positions.UpsertAsync(position, cancellationToken).ConfigureAwait(false);

        return true;
    }
}

/// <summary>
/// Oublie la position : le chauffeur passe hors ligne.
/// </summary>
///
/// <remarks>
/// CE GESTE NE PORTE PLUS DE DECISION, ET C'EST UN PROGRES. Quand Driver tenait les
/// deux moities de la question, retirer le point etait ce qui mettait quelqu'un hors
/// du dispatch : l'oublier laissait un chauffeur rentre chez lui recevoir des offres.
/// Depuis que l'eligibilite se lit dans Driver, un point qui traine ne produit aucune
/// offre par lui-meme — la vague croise toujours la base, qui le voit hors ligne.
///
/// IL RESTE UTILE POUR LA CARTE. Un point perime disparait tout seul au bout de la
/// fenetre de fraicheur ; l'oubli explicite evite les deux minutes pendant lesquelles
/// l'exploitation verrait encore quelqu'un qui vient de se deconnecter.
/// </remarks>
public sealed record ForgetPositionCommand(Guid DriverId) : ICommand<bool>;

public sealed class ForgetPositionHandler(
    IPositionStore positions,
    ICallerContext caller) : ICommandHandler<ForgetPositionCommand, bool>
{
    public async Task<bool> HandleAsync(ForgetPositionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        LocationAccess.EnsureCanWrite(caller, command.DriverId);

        await positions.ForgetAsync(command.DriverId, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
