using Grpc.Core;
using Hba.Contracts.Routing.V1;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using ProtoGeoPoint = Hba.Contracts.Common.V1.GeoPoint;

namespace Hba.Pricing.Infrastructure.Services.Routing;

/// <summary>
/// Mesure un trajet en interrogeant le service Routing.
/// </summary>
///
/// <remarks>
/// C'EST LA FIN D'UNE PROVISOIRE QUI A DURÉ. Jusqu'au 1er octobre 2026, tous les
/// prix de la plateforme étaient calculés sur une distance À VOL D'OISEAU par
/// <c>HaversineRouteEngine</c>, dont le commentaire annonçait lui-même son
/// remplacement. Deux choses l'ont rendu intenable : le prix est désormais FERME
/// à la commande — une mesure fausse devient une facture fausse — et le dispatch
/// doit comparer des temps de trajet réels.
///
/// LES PRIX VONT CHANGER, ET C'EST LE BUT. Une distance routière est plus longue
/// qu'une ligne droite : d'environ un tiers en ville, davantage quand le trajet
/// contourne la lagune. Les grilles tarifaires n'ont pas été recalculées pour
/// autant — c'est une décision commerciale à prendre en regardant les premiers
/// devis réels.
///
/// PRICING NE CONNAÎT PAS OSRM, ET NE DOIT JAMAIS LE CONNAÎTRE. Il parle à
/// Routing, qui choisit son fournisseur. C'est ce qui permettra de passer à
/// Google ou Mapbox sans qu'une règle de prix ne bouge.
/// </remarks>
internal sealed class RoutingServiceRouteEngine(
    RoutingService.RoutingServiceClient routing,
    IRouteEngine repli,
    ILogger<RoutingServiceRouteEngine> logger) : IRouteEngine
{
    public async Task<RouteMeasurement> MeasureAsync(
        GeoPoint pickup,
        GeoPoint dropoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);

        try
        {
            var trajet = await routing.CalculateRouteAsync(
                new CalculateRouteRequest
                {
                    Origin = new ProtoGeoPoint { Latitude = pickup.Latitude, Longitude = pickup.Longitude },
                    Destination = new ProtoGeoPoint { Latitude = dropoff.Latitude, Longitude = dropoff.Longitude },

                    // LE PROFIL N'EST PAS ENCORE TRANSMIS, et il faut le dire
                    // plutot que de le laisser deviner. Le devis connait le type
                    // de vehicule, mais RouteMeasurement et IRouteEngine ne le
                    // portent pas : l'ajouter touche la signature du port, donc
                    // le domaine. Tant que c'est ainsi, Routing applique son
                    // repli — la voiture, le reseau le plus contraint, donc la
                    // mesure la plus prudente. Une moto est surestimee, jamais
                    // sous-facturee.
                    IncludePolyline = false,
                },
                cancellationToken: cancellationToken);

            if (trajet.FromFallback)
            {
                // ON JOURNALISE, ON NE REFUSE PAS. Refuser arreterait tous les
                // devis de la plateforme des qu'OSRM tousse ; accepter en
                // silence figerait des prix sous-estimes sans que personne ne le
                // sache.
                //
                // DEPUIS LE 2 OCTOBRE 2026, LE JOURNAL N'EST PLUS LE SEUL ENDROIT
                // OU CETTE DEGRADATION EST VISIBLE : le drapeau descend dans la
                // mesure, qui est persistee avec le devis (point 36, option B).
                // Le journal reste utile pour voir la panne en cours ; la colonne
                // sert a retrouver les devis apres coup.
                logger.LogWarning(
                    "Devis calcule sur une mesure a vol d'oiseau ({Fournisseur}) : "
                    + "la distance est sous-estimee, le prix aussi.",
                    trajet.Provider);
            }

            return RouteMeasurement.Create(
                trajet.DistanceMeters,
                trajet.DurationSeconds,
                isDegraded: trajet.FromFallback);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception erreur)
        {
            // ROUTING N'A PAS RENDU DE MESURE. On mesure alors localement plutot
            // que d'arreter la vente : c'est exactement ce que Pricing faisait
            // jusqu'au 1er octobre 2026, et ce jour-la personne ne s'en
            // plaignait.
            //
            // CE CAS EST LE PLUS DEGRADE DES DEUX, ET C'EST CELUI QU'ON POUVAIT
            // OUBLIER : il ne traverse aucune reponse gRPC, donc aucun drapeau
            // from_fallback n'existe ici. C'est HaversineRouteEngine qui marque
            // sa propre mesure — voir le commentaire de son Measure.
            //
            // « INJOIGNABLE » ETAIT ECRIT ICI POUR LES DEUX CAS, ET C'ETAIT FAUX
            // UNE FOIS SUR DEUX. Le 2 octobre 2026, Routing repondait en
            // cinquante millisecondes — un statut d'erreur, mais il repondait :
            // le journal envoyait pourtant chercher une panne de reseau, alors
            // que la cause etait une chaine de connexion Redis sans mot de passe
            // a l'interieur de Routing. Un message qui designe le mauvais
            // coupable coute plus cher qu'un message absent.
            logger.LogWarning(erreur, "{Diagnostic} Mesure locale a vol d'oiseau.", Diagnostic(erreur));

            return await repli.MeasureAsync(pickup, dropoff, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dit lequel des deux problemes on vient de rencontrer.
    /// </summary>
    ///
    /// <remarks>
    /// DEUX PANNES QUI NE SE CORRIGENT PAS AU MEME ENDROIT. « Injoignable »
    /// envoie regarder le reseau, les adresses et l'etat du conteneur ;
    /// « a echoue » envoie lire les journaux de Routing, ou l'exception reelle
    /// est deja ecrite sous « Echec non metier ». Les confondre fait perdre la
    /// soiree a chercher du cote ou il n'y a rien.
    /// </remarks>
    private static string Diagnostic(Exception erreur) => erreur switch
    {
        RpcException { StatusCode: StatusCode.Unavailable or StatusCode.Unknown } =>
            "Service Routing injoignable.",

        RpcException { StatusCode: StatusCode.DeadlineExceeded } =>
            "Service Routing trop lent : delai depasse.",

        // IL A REPONDU, ET IL A REPONDU UNE ERREUR. La reference est dans son
        // detail : elle retrouve la ligne « Echec non metier » de son journal,
        // qui porte la pile complete.
        RpcException autre =>
            $"Service Routing en echec ({autre.StatusCode}) : {autre.Status.Detail}",

        _ => "Appel a Routing en echec.",
    };
}
