using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Contracts.Notification.V1;
using Hba.Notification.Application.Journal;
using Microsoft.AspNetCore.Authorization;
using DomainChannel = Hba.Notification.Domain.Messages.NotificationChannel;
using DomainStatus = Hba.Notification.Domain.Messages.NotificationStatus;
using ProtoLigne = Hba.Contracts.Notification.V1.SentNotification;

namespace Hba.Notification.Api.Grpc;

/// <summary>
/// LA SEULE API SYNCHRONE DE NOTIFICATION, ET ELLE NE SAIT QUE LIRE.
///
/// Envoyer reste un dépôt Kafka : ouvrir un « SendNow » ici mettrait un
/// opérateur téléphonique au milieu de la transaction qui clôt une course.
/// </summary>
[Authorize]
public sealed class NotificationGrpcService(IDispatcher dispatcher) : NotificationService.NotificationServiceBase
{
    public override async Task<ListNotificationsResponse> ListNotifications(
        ListNotificationsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var page = await dispatcher.QueryAsync(
            new ListerLeJournalQuery(
                request.PageSize,
                request.Offset,
                CanalDemande(request.Channel),
                StatutDemande(request.Status),
                Vide(request.RecipientEndsWith),
                Vide(request.TemplateId)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListNotificationsResponse { Total = page.Total };
        // LAMBDA PLUTOT QUE GROUPE DE METHODES : « Vers » est surchargé trois
        // fois, et l'inférence de Select n'aurait rien pour choisir.
        reponse.Notifications.AddRange(page.Lignes.Select(ligne => Vers(ligne)));

        return reponse;
    }

    public override async Task<GetChannelStatusResponse> GetChannelStatus(
        GetChannelStatusRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var etats = await dispatcher.QueryAsync(
            new EtatDesCanauxQuery(),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new GetChannelStatusResponse();

        reponse.Channels.AddRange(etats.Select(etat => new ChannelStatus
        {
            Channel = Vers(etat.Canal),
            Adapter = etat.Adaptateur,
            Configured = etat.Configure,
        }));

        return reponse;
    }

    /// <summary>
    /// UNSPECIFIED N'EST PAS UNE VALEUR, C'EST UNE ABSENCE DE FILTRE. Le
    /// traduire en « canal inconnu » ne rendrait jamais rien, et l'écran
    /// afficherait une liste vide sans que personne comprenne pourquoi.
    /// </summary>
    ///
    /// <remarks>
    /// DES SWITCH, ET NON UNE CONVERSION D'ENTIER. Les deux énumérations
    /// portent aujourd'hui les mêmes numéros ; une réécriture du domaine qui
    /// les décalerait ferait passer « échoué » pour « envoyé » sans que rien ne
    /// casse. Ici, le compilateur signale le cas oublié.
    /// </remarks>
    private static DomainChannel? CanalDemande(NotificationChannel canal) => canal switch
    {
        NotificationChannel.Sms => DomainChannel.Sms,
        NotificationChannel.Whatsapp => DomainChannel.WhatsApp,
        NotificationChannel.Push => DomainChannel.Push,
        NotificationChannel.Email => DomainChannel.Email,
        _ => null,
    };

    private static DomainStatus? StatutDemande(NotificationStatus statut) => statut switch
    {
        NotificationStatus.Pending => DomainStatus.Pending,
        NotificationStatus.Sent => DomainStatus.Sent,
        NotificationStatus.Failed => DomainStatus.Failed,
        NotificationStatus.Skipped => DomainStatus.Skipped,
        _ => null,
    };

    private static NotificationChannel Vers(DomainChannel canal) => canal switch
    {
        DomainChannel.Sms => NotificationChannel.Sms,
        DomainChannel.WhatsApp => NotificationChannel.Whatsapp,
        DomainChannel.Push => NotificationChannel.Push,
        DomainChannel.Email => NotificationChannel.Email,
        _ => NotificationChannel.Unspecified,
    };

    private static NotificationStatus Vers(DomainStatus statut) => statut switch
    {
        DomainStatus.Pending => NotificationStatus.Pending,
        DomainStatus.Sent => NotificationStatus.Sent,
        DomainStatus.Failed => NotificationStatus.Failed,
        DomainStatus.Skipped => NotificationStatus.Skipped,
        _ => NotificationStatus.Unspecified,
    };

    private static string? Vide(string valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur;

    private static ProtoLigne Vers(LigneDuJournalVue vue)
    {
        var ligne = new ProtoLigne
        {
            Id = vue.Id.ToString(),
            Channel = Vers(vue.Canal),
            RecipientMasked = vue.DestinataireMasque,
            TemplateId = vue.ModeleId,
            Status = Vers(vue.Statut),
            Provider = vue.Operateur ?? string.Empty,
            ProviderMessageId = vue.ReferenceOperateur ?? string.Empty,
            Error = vue.Erreur ?? string.Empty,
            Attempts = vue.Tentatives,
            CorrelationId = vue.CorrelationId ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(vue.CreeLe),
        };

        if (vue.TermineLe is { } fin)
        {
            ligne.CompletedAt = Timestamp.FromDateTimeOffset(fin);
        }

        return ligne;
    }
}
