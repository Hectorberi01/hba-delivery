using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Identity.V1;
using Hba.Directory.Application.Customers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Directory.Api.Messaging;

/// <summary>
/// Un compte créé dans Identity donne un profil dans Directory. C'est le seul
/// chemin de création d'un profil client : aucune demande de livraison ne doit
/// en fabriquer un au passage.
///
/// Les comptes de commerçants et du back-office sont ignorés : le premier est
/// rattaché à un commerçant déjà référencé, le second n'a pas de profil ici.
/// </summary>
public sealed class IdentityEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<IdentityEventsConsumer> logger)
    : KafkaConsumerBase<IdentityEvent>(scopeFactory, options, logger)
{
    private const string CustomerRole = "customer";

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
            return;
        }

        var registered = message.Registered;

        if (!registered.Roles.Contains(CustomerRole, StringComparer.Ordinal))
        {
            return;
        }

        if (!Guid.TryParse(registered.AccountId, out var accountId))
        {
            logger.LogError("Identifiant de compte illisible : {AccountId}.", registered.AccountId);
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(
            new CreateCustomerProfileCommand(
                accountId,
                registered.DisplayName,
                registered.Phone,
                string.IsNullOrWhiteSpace(registered.Email) ? null : registered.Email,
                registered.RegisteredAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }
}
