using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;

namespace Hba.Gateway.Plateforme;

/// <summary>Un secret ou un réglage que le service attend, et ce que son absence empêche.</summary>
///
/// <remarks>
/// LA VALEUR N'Y FIGURE PAS, ET CE TYPE N'A AUCUN CHAMP POUR L'ACCUEILLIR.
/// Cette page dit ce dont un service a besoin et ce qui casse sans lui ; elle
/// ne lit aucun secret et n'en écrit aucun. L'ADR 0018 le pose pour les jetons
/// de service, et la raison vaut pour tous : « un service est un morceau du
/// déploiement, au même titre que sa chaîne de connexion ».
/// </remarks>
public sealed record ReglageAttendu(string Cle, string Empeche);

/// <summary>Un service du déploiement, tel que la passerelle le connaît.</summary>
public sealed record ServiceConnu(string Nom, string Adresse, IReadOnlyList<ReglageAttendu> Reglages);

/// <summary>Ce qu'une sonde a trouvé, maintenant.</summary>
public sealed record SondeDuService(
    string Nom,
    string Adresse,
    string Sante,
    long Millisecondes,
    string? Detail,
    IReadOnlyList<ReglageAttendu> Reglages);

/// <summary>
/// Sonde la santé de chaque service, et porte l'inventaire de ce dont chacun a
/// besoin pour fonctionner.
/// </summary>
///
/// <remarks>
/// POURQUOI LA PASSERELLE ET PAS UN SERVICE DE PLUS. Elle est le seul composant
/// qui connaît l'adresse de tous les autres — c'est déjà sa fonction. Ajouter
/// un service « supervision » reviendrait à recopier cette liste à un second
/// endroit, où elle divergerait.
///
/// CE QUI EST MESURE ET CE QUI EST DECLARE, ET IL NE FAUT PAS LES CONFONDRE.
/// La santé est mesurée, ici, maintenant. L'inventaire des réglages est
/// DECLARE : la passerelle ne lit pas la configuration des autres conteneurs,
/// et prétendre le contraire afficherait « présent » pour un secret absent.
/// Seul Notification sait dire si ses canaux ont un fournisseur, et il le dit
/// lui-même (GetChannelStatus).
///
/// LES CANAUX SONT CREES UNE FOIS. Un GrpcChannel par appel ouvrirait une
/// connexion HTTP/2 par ouverture d'écran, pour une page qu'on rafraîchit.
/// </remarks>
public sealed class EtatDesServices
{
    private readonly ConcurrentDictionary<string, GrpcChannel> _canaux = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<ServiceConnu> _services;

    public EtatDesServices(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string Adresse(string nom, string defaut)
            => configuration[$"Services:{nom}"] ?? defaut;

        _services =
        [
            new ServiceConnu("identity", Adresse("Identity", "http://identity:8081"),
            [
                new ReglageAttendu("Jwt__Issuer", "aucun service ne valide les jetons : tout répond 401"),
                new ReglageAttendu("IdentityDb", "le service ne démarre pas"),
                new ReglageAttendu("ServiceClients__*", "les appels de service à service repartent sans jeton"),
            ]),
            new ServiceConnu("delivery", Adresse("Delivery", "http://delivery:8081"),
            [
                new ReglageAttendu("DeliveryDb", "le service ne démarre pas"),
                new ReglageAttendu("SERVICE_SECRET_DELIVERY", "le service refuse de démarrer (ADR 0018)"),
            ]),
            new ServiceConnu("dispatch", Adresse("Dispatch", "http://dispatch:8081"),
            [
                new ReglageAttendu("DispatchDb", "le service ne démarre pas"),
                new ReglageAttendu("SERVICE_SECRET_DISPATCH", "aucune vague ne s'ouvre, donc aucune offre ne part"),
            ]),
            new ServiceConnu("driver", Adresse("Driver", "http://driver:8081"),
            [
                new ReglageAttendu("DriverDb", "le service ne démarre pas"),
                new ReglageAttendu("Garage__*", "les pièces d'identité ne peuvent plus être déposées ni relues"),
            ]),
            new ServiceConnu("pricing", Adresse("Pricing", "http://pricing:8081"),
            [
                new ReglageAttendu("PricingDb", "le service ne démarre pas"),
                new ReglageAttendu("Routing__BaseUrl", "les devis repartent sur la mesure dégradée (point 36)"),
            ]),
            new ServiceConnu("payment", Adresse("Payment", "http://payment:8081"),
            [
                new ReglageAttendu("PaymentDb", "le service ne démarre pas"),
                new ReglageAttendu("FedaPay__SecretKey", "aucun paiement ne s'ouvre : toute course reste impayée"),
                new ReglageAttendu("FedaPay__WebhookSecret", "les retours de l'agrégateur sont refusés, les paiements restent en attente"),
            ]),
            new ServiceConnu("directory", Adresse("Directory", "http://directory:8081"),
            [
                new ReglageAttendu("DirectoryDb", "le service ne démarre pas"),
            ]),
            new ServiceConnu("location", Adresse("Location", "http://location:8081"),
            [
                new ReglageAttendu("Redis__Configuration", "aucune position de livreur n'est conservée : la carte reste vide"),
            ]),
            new ServiceConnu("ride", Adresse("Ride", "http://ride:8081"),
            [
                new ReglageAttendu("RideDb", "le service ne démarre pas"),
            ]),
            new ServiceConnu("billing", Adresse("Billing", "http://billing:8081"),
            [
                new ReglageAttendu("BillingDb", "le service ne démarre pas"),
            ]),
            new ServiceConnu("notification", Adresse("Notification", "http://notification:8081"),
            [
                new ReglageAttendu("SMS_PROVIDER + Ovh__*", "les codes de connexion et de remise sont consignés « ignorés » et ne partent pas"),
                new ReglageAttendu("WHATSAPP_PROVIDER + WhatsApp__*", "la chaîne bascule sur le SMS (ADR 0014)"),
                new ReglageAttendu("EMAIL_PROVIDER + Resend__*", "aucun reçu de course n'est envoyé"),
            ]),
        ];
    }

    public IReadOnlyList<ServiceConnu> Services => _services;

    /// <summary>
    /// Interroge la sonde gRPC de chaque service, en parallèle.
    /// </summary>
    ///
    /// <remarks>
    /// UNE ECHEANCE COURTE, ET ELLE EST LE SUJET. Un service injoignable doit
    /// se voir en deux secondes : c'est précisément ce qu'on vient vérifier.
    /// Sans échéance, la page attendrait le délai TCP par défaut et
    /// ressemblerait elle-même à une panne.
    ///
    /// AUCUNE EXCEPTION NE REMONTE. Un service qui ne répond pas est une
    /// réponse, pas une erreur de la page.
    /// </remarks>
    public async Task<IReadOnlyList<SondeDuService>> SonderAsync(CancellationToken cancellationToken)
    {
        var sondes = await Task.WhenAll(_services.Select(service => SonderUnAsync(service, cancellationToken)))
            .ConfigureAwait(false);

        return sondes;
    }

    private async Task<SondeDuService> SonderUnAsync(ServiceConnu service, CancellationToken cancellationToken)
    {
        var chrono = Stopwatch.StartNew();

        try
        {
            var canal = _canaux.GetOrAdd(service.Adresse, adresse => GrpcChannel.ForAddress(adresse));
            var sonde = new Health.HealthClient(canal);

            var reponse = await sonde.CheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(2),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            chrono.Stop();

            return new SondeDuService(
                service.Nom,
                service.Adresse,
                reponse.Status == HealthCheckResponse.Types.ServingStatus.Serving ? "sert" : "ne sert pas",
                chrono.ElapsedMilliseconds,
                reponse.Status.ToString(),
                service.Reglages);
        }
        catch (RpcException erreur)
        {
            chrono.Stop();

            // LE CODE GRPC EST PLUS UTILE QUE LE MESSAGE : « Unavailable » dit
            // que le conteneur ne répond pas, « DeadlineExceeded » qu'il répond
            // trop lentement, et les deux se traitent différemment.
            return new SondeDuService(
                service.Nom,
                service.Adresse,
                "injoignable",
                chrono.ElapsedMilliseconds,
                erreur.StatusCode.ToString(),
                service.Reglages);
        }
    }
}
