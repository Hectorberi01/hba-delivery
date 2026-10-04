using System.Security.Claims;
using Grpc.Core;
using Hba.Contracts.Notification.V1;
using Hba.Gateway.Plateforme;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// L'état de la plateforme, service par service.
/// </summary>
///
/// <remarks>
/// POURQUOI CETTE PORTE N'A PAS DE ROLE. Le référentiel acteurs connaît dix
/// rôles et « super admin » n'en fait pas partie — Identity REFUSE d'ailleurs
/// un rôle inconnu, et un test le vérifie. Les propriétaires de la plateforme
/// sont donc nommés dans la CONFIGURATION de l'hôte, exactement comme les
/// clients de service de l'ADR 0018 : « un service est un morceau du
/// déploiement, au même titre que sa chaîne de connexion ». Ajouter quelqu'un
/// ici demande un déploiement, et c'est la propriété qu'on veut.
///
/// LA VERIFICATION EST ICI PARCE QUE LA RESSOURCE EST ICI. L'ADR 0007 exige
/// que l'autorisation soit vérifiée par le service qui détient la donnée ; la
/// donnée, ce sont les sondes que la passerelle prend elle-même. Le seul appel
/// descendant — l'état des canaux de Notification — est de toute façon
/// revérifié par Notification, qui le réserve à admin.
///
/// AUCUN SECRET NE TRAVERSE CETTE ROUTE, dans aucun sens. Elle rend des noms
/// de clés et ce que leur absence empêche ; elle n'en lit ni n'en écrit
/// aucune valeur.
/// </remarks>
public static class AdminPlateformeEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlateformeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var groupe = app.MapGroup("/api/admin/v1/plateforme").RequireAuthorization();

        // L'ECRAN DEMANDE S'IL DOIT EXISTER. Cacher n'est pas protéger — le
        // refus ci-dessous est ce qui protège — mais proposer une entrée qui
        // se referme au clic est un mauvais écran.
        groupe.MapGet("/acces", (HttpContext http, IConfiguration configuration) =>
            Results.Ok(new { proprietaire = EstProprietaire(http, configuration) }));

        groupe.MapGet("", async (
            HttpContext http,
            IConfiguration configuration,
            EtatDesServices etat,
            NotificationService.NotificationServiceClient notification,
            CancellationToken cancellationToken) =>
        {
            if (!EstProprietaire(http, configuration))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var sondes = await etat.SonderAsync(cancellationToken);

            // L'ETAT DES CANAUX EST LA SEULE MESURE QUI NE VIENT PAS D'ICI, et
            // il peut échouer sans emporter la page : Notification injoignable
            // est déjà dit par sa sonde.
            IReadOnlyList<object> canaux = [];

            try
            {
                var reponse = await notification.GetChannelStatusAsync(
                    new GetChannelStatusRequest(),
                    cancellationToken: cancellationToken);

                canaux =
                [
                    .. reponse.Channels.Select(canal => new
                    {
                        canal = canal.Channel.ToString(),
                        adaptateur = canal.Adapter,
                        configure = canal.Configured,
                    }),
                ];
            }
            catch (RpcException)
            {
                // Volontairement muet : la liste vide se lit « non mesuré »,
                // et la sonde de notification dit déjà pourquoi.
            }

            return Results.Ok(new
            {
                services = sondes.Select(sonde => new
                {
                    nom = sonde.Nom,
                    adresse = sonde.Adresse,
                    sante = sonde.Sante,
                    millisecondes = sonde.Millisecondes,
                    detail = sonde.Detail,
                    reglages = sonde.Reglages.Select(reglage => new
                    {
                        cle = reglage.Cle,
                        empeche = reglage.Empeche,
                    }),
                }),
                canaux,
            });
        });

        return app;
    }

    /// <summary>
    /// Le sujet du jeton figure-t-il dans la liste des propriétaires ?
    /// </summary>
    ///
    /// <remarks>
    /// « Plateforme:Proprietaires » EST UNE LISTE D'IDENTIFIANTS DE COMPTE,
    /// séparés par des virgules. Vide, la porte est fermée pour tout le monde :
    /// une page de supervision ouverte par défaut serait l'inverse de ce qu'on
    /// cherche.
    ///
    /// LE SUJET, ET NON L'E-MAIL : un e-mail se change, un identifiant de
    /// compte non.
    /// </remarks>
    private static bool EstProprietaire(HttpContext http, IConfiguration configuration)
    {
        var sujet = http.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sujet))
        {
            return false;
        }

        var liste = configuration["Plateforme:Proprietaires"] ?? string.Empty;

        return liste
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(sujet, StringComparer.OrdinalIgnoreCase);
    }
}
