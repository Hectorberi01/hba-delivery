using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Identity.V1;
using Hba.Driver.Application.Features.Drivers.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Driver.Api.Messaging;

/// <summary>
/// Naissance du profil livreur.
///
/// LE COMPTE FAIT FOI. Le contrat gRPC n'expose aucune methode d'inscription :
/// c'est Identity qui cree le compte, et Driver en tire un profil quand ce
/// compte porte le role livreur. Le profil reprend l'identifiant du compte, si
/// bien qu'un seul identifiant traverse tous les services — aucune table de
/// correspondance a tenir, et aucun risque de la voir diverger.
/// </summary>
public sealed class IdentityEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<IdentityEventsConsumer> logger)
    : KafkaConsumerBase<IdentityEvent>(scopeFactory, options, logger)
{
    protected override string Topic => KafkaTopics.IdentityEvents;

    protected override Guid GetEventId(IdentityEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Guid.TryParse(message.Envelope?.EventId, out var id) ? id : Guid.CreateVersion7();
    }

    protected override string GetEventType(IdentityEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Envelope?.EventType ?? "unknown";
    }

    protected override async Task HandleAsync(
        IdentityEvent message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        if (message.PayloadCase != IdentityEvent.PayloadOneofCase.Registered)
        {
            // AccountRolesChanged fera naitre un profil le jour ou un client
            // deviendra livreur. Ce n'est pas traite : le referentiel ne dit
            // pas si ce passage est permis, et l'inventer creerait des profils
            // livreurs a partir de comptes qui n'ont jamais depose de dossier.
            return;
        }

        var registered = message.Registered;

        if (!registered.Roles.Contains(HbaRoles.Driver, StringComparer.Ordinal))
        {
            return;
        }

        if (!Guid.TryParse(registered.AccountId, out var accountId))
        {
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(
            new RegisterDriverCommand(
                accountId,
                string.IsNullOrWhiteSpace(registered.DisplayName) ? registered.Phone : registered.DisplayName,
                registered.Phone),
            cancellationToken).ConfigureAwait(false);
    }
}
