using Hba.BuildingBlocks.Security;
using Hba.Contracts.Notification.V1;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// Le journal des envois, pour le back-office.
///
/// POURQUOI CETTE ROUTE EXISTE. Jusqu'ici, « le client dit qu'il n'a pas reçu
/// son code » se vérifiait en lisant la table du service à la main. Le journal
/// répond à trois questions et à trois seulement : est-ce parti, par quel
/// canal, et qu'en a dit l'opérateur.
///
/// LE DESTINATAIRE ARRIVE DEJA MASQUE. Notification ne rend que les quatre
/// derniers caractères (ListerLeJournalHandler) : la passerelle n'a rien à
/// masquer, et surtout rien à démasquer.
/// </summary>
public static class AdminNotificationEndpoints
{
    public static IEndpointRouteBuilder MapAdminNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // admin, ops et support — PAS finance. Notification revérifie (ADR
        // 0007) : la politique posée ici évite seulement qu'un appel
        // manifestement hors périmètre traverse la passerelle.
        var journal = app.MapGroup("/api/admin/v1/notifications")
            .RequireAuthorization(HbaPolicies.JournalNotifications);

        journal.MapGet("", async (
            NotificationService.NotificationServiceClient notifications,
            CancellationToken cancellationToken,
            int pageSize = 50,
            int offset = 0,
            int channel = 0,
            int status = 0,
            string? recipient = null,
            string? templateId = null) =>
        {
            // LES FILTRES ARRIVENT EN ENTIERS, comme les énumérations repartent.
            // Zéro vaut UNSPECIFIED, c'est-à-dire « ne filtre pas » : une valeur
            // hors bornes est ramenée là plutôt que refusée, parce qu'un filtre
            // d'écran mal formé ne doit pas valoir une erreur à l'utilisateur.
            var requete = new ListNotificationsRequest
            {
                PageSize = pageSize,
                Offset = offset,
                Channel = Enum.IsDefined(typeof(NotificationChannel), channel)
                    ? (NotificationChannel)channel
                    : NotificationChannel.Unspecified,
                Status = Enum.IsDefined(typeof(NotificationStatus), status)
                    ? (NotificationStatus)status
                    : NotificationStatus.Unspecified,
                RecipientEndsWith = recipient ?? string.Empty,
                TemplateId = templateId ?? string.Empty,
            };

            var page = await notifications.ListNotificationsAsync(requete, cancellationToken: cancellationToken);

            return Results.Ok(page);
        });

        return app;
    }
}
