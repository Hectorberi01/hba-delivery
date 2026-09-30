using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Identity.V1;
using Hba.Directory.Application.Customers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hba.Directory.Api.Messaging;

/// <summary>
/// Un compte créé dans Identity donne un profil dans Directory. C'est le chemin
/// NORMAL de création d'un profil client, et aucune demande de livraison ne doit
/// en fabriquer un au passage.
///
/// CE COMMENTAIRE DISAIT « LE SEUL CHEMIN », ET CE N'EST PLUS VRAI. Il existe
/// depuis le 28 septembre 2026 une seconde voie, EnsureCustomerProfileCommand,
/// déclenchée par le client lui-même quand cet événement-ci s'est perdu — un
/// message publié pendant que Directory était arrêté n'est jamais rejoué par
/// Kafka pour un groupe de consommateurs qui n'existait pas encore. Ce que la
/// phrase d'origine visait tient toujours : aucune création par effet de bord.
/// Voir le point 25 de points-a-trancher.md.
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

        // DEUX EVENEMENTS, ET C'EST LE MEME CONSOMMATEUR. Un compte cree donne
        // une fiche, un compte efface l'emporte : les deux viennent du meme
        // topic et du meme agregat. Un second consommateur aurait sa propre
        // position de lecture, donc son propre risque de traiter la creation
        // et la suppression dans le desordre.
        if (message.PayloadCase == IdentityEvent.PayloadOneofCase.Erased)
        {
            await EffacerAsync(message.Erased, services, cancellationToken).ConfigureAwait(false);
            return;
        }

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

    /// <summary>
    /// Le compte a ete efface d'Identity : sa fiche et ses medias suivent.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUN FILTRE SUR LE ROLE ICI, contrairement a la creation. A la
    /// creation, on ne fabrique une fiche que pour un client ; a l'effacement,
    /// on supprime ce qui EXISTE. Un compte qui n'avait pas de fiche n'en a
    /// simplement pas a effacer, et la commande le dit sans lever.
    ///
    /// D'AILLEURS L'EVENEMENT NE PORTE PAS LES ROLES, et c'est voulu : il ne
    /// porte ni nom, ni telephone, ni rien qui ferait vivre l'identite du
    /// titulaire dans Kafka pendant toute la retention du topic — au moment
    /// precis ou il demande qu'elle disparaisse.
    /// </remarks>
    private async Task EffacerAsync(
        AccountErased erased,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(erased.AccountId, out var accountId))
        {
            logger.LogError("Identifiant de compte efface illisible : {AccountId}.", erased.AccountId);
            return;
        }

        var dispatcher = services.GetRequiredService<IDispatcher>();

        await dispatcher
            .SendAsync(new EraseCustomerCommand(accountId), cancellationToken)
            .ConfigureAwait(false);
    }
}
