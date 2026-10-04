using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Journal;

/// <summary>
/// L'état d'un canal : quel adaptateur a été résolu, et s'il a réellement un
/// fournisseur.
/// </summary>
///
/// <remarks>
/// AUCUN SECRET, ET AUCUN CHAMP QUI Y MENE. On rend le nom de la classe et un
/// booléen. La clé d'API n'entre pas dans cette vue, donc il n'y a rien à
/// penser à retirer.
/// </remarks>
public sealed record EtatDuCanalVue(NotificationChannel Canal, string Adaptateur, bool Configure);

/// <summary>
/// Ce que <c>SenderStartupReport</c> consigne au démarrage, rendu à la demande.
///
/// LE JOURNAL DE DEMARRAGE NE SE RELIT PAS DEPUIS UNE CONSOLE. Il dit déjà
/// exactement cela, une fois, dans les logs du conteneur — c'est-à-dire là où
/// personne ne regarde six semaines plus tard, quand les codes de connexion
/// cessent d'arriver.
/// </summary>
public sealed record EtatDesCanauxQuery : IQuery<IReadOnlyList<EtatDuCanalVue>>;

public sealed class EtatDesCanauxHandler(
    IEnumerable<INotificationSender> senders,
    ICallerContext caller) : IQueryHandler<EtatDesCanauxQuery, IReadOnlyList<EtatDuCanalVue>>
{
    public Task<IReadOnlyList<EtatDuCanalVue>> HandleAsync(
        EtatDesCanauxQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // admin SEULEMENT. L'état des canaux dit ce que la plateforme sait
        // faire et ce qu'elle ne sait pas faire : c'est de la configuration,
        // pas de l'exploitation.
        if (!caller.IsInRole(HbaRoles.Admin))
        {
            throw new ForbiddenException("Réservé au rôle admin.");
        }

        // GroupBy ET NON ToDictionary, pour la raison écrite dans
        // SenderStartupReport : deux adaptateurs enregistrés pour le même canal
        // feraient lever, et un état ne doit jamais tomber en panne.
        var parCanal = senders
            .GroupBy(sender => sender.Channel)
            .ToDictionary(groupe => groupe.Key, groupe => groupe.First());

        IReadOnlyList<EtatDuCanalVue> etats =
        [
            .. Enum.GetValues<NotificationChannel>().Select(canal =>
                parCanal.TryGetValue(canal, out var adaptateur)
                    ? new EtatDuCanalVue(canal, adaptateur.GetType().Name, adaptateur.IsConfigured)
                    // AUCUN ADAPTATEUR ENREGISTRE N'EST UN TROISIEME CAS, et il
                    // se lit autrement qu'un adaptateur non configuré : le
                    // premier est un oubli de câblage, le second une décision.
                    : new EtatDuCanalVue(canal, "aucun adaptateur enregistré", false)),
        ];

        return Task.FromResult(etats);
    }
}
