using Hba.BuildingBlocks.Application.Extensions;
using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Billing.V1;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Directory.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Identity.V1;
using Hba.Contracts.Payment.V1;
using Hba.Contracts.Pricing.V1;
using Hba.Gateway;
using Hba.Gateway.Endpoints.Client;
using Hba.Gateway.Endpoints.Driver;
using Hba.Gateway.Endpoints.Partner;
using Hba.Gateway.Endpoints.Public;
using Hba.Gateway.Endpoints.Relais;
using Hba.Gateway.Endpoints.Web;
using Hba.Gateway.Webhooks;
using Microsoft.AspNetCore.Builder;

// ENTREE UNIQUE DES QUATRE SURFACES PUBLIQUES.
//
// Ce projet remplace Hba.Bff.Client, Hba.Bff.Driver, Hba.Bff.Web et
// Hba.PartnerApi, qui etaient quatre deployables au code quasi identique :
// memes intergiciels, memes clients gRPC, memes reglages, seuls les groupes de
// routes differaient. Les voici reunis, sans qu'une seule ligne de traduction
// REST vers gRPC ait ete reecrite : les fichiers d'endpoints sont ceux d'avant,
// deplaces.
//
// CE QUE LA FUSION FAIT PERDRE, ET QU'IL FAUT SAVOIR : les routes
// d'administration et les routes client vivent desormais dans le meme
// processus. Un defaut dans l'une peut affecter l'autre, et une mise a jour les
// redemarre ensemble. La separation reste portee par les politiques
// d'autorisation de chaque groupe de routes, et surtout par les services
// eux-memes, qui refont la verification pour leur compte (ADR 0007). Le gateway
// n'a jamais ete l'endroit ou l'autorisation se decide.

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("gateway");

// Le gateway valide le jeton, mais ne decide rien : c'est un filtre precoce qui
// evite un appel gRPC pour une requete dont le jeton est deja invalide.
builder.Services.AddHbaSecurity(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHbaAuthRateLimiter();
builder.Services.AddHbaForwardedHeaders(builder.Configuration);

builder.Services.AddHbaGatewayDocumentation();

// L'ECHEANCE PAR DEFAUT EST DE 5 SECONDES, ET ELLE NE SUFFIT PAS ICI. La
// creation d'une livraison declenche, en chaine, la consommation du devis puis
// l'ouverture d'un paiement chez un agregateur externe. Une echeance plus
// courte que celle de Delivery vers Payment ferait abandonner la gateway
// pendant que la course se cree correctement derriere : le client verrait une
// erreur pour une livraison qui existe.
builder.Services.AddHbaGrpcClient<DeliveryService.DeliveryServiceClient>(
    new Uri(builder.Configuration["Services:Delivery"] ?? "http://delivery:8081"),
    TimeSpan.FromSeconds(30));
builder.Services.AddHbaGrpcClient<DispatchService.DispatchServiceClient>(
    new Uri(builder.Configuration["Services:Dispatch"] ?? "http://dispatch:8081"));
builder.Services.AddHbaGrpcClient<DirectoryService.DirectoryServiceClient>(
    new Uri(builder.Configuration["Services:Directory"] ?? "http://directory:8081"));
builder.Services.AddHbaGrpcClient<DriverService.DriverServiceClient>(
    new Uri(builder.Configuration["Services:Driver"] ?? "http://driver:8081"));
builder.Services.AddHbaGrpcClient<IdentityService.IdentityServiceClient>(
    new Uri(builder.Configuration["Services:Identity"] ?? "http://identity:8081"));
builder.Services.AddHbaGrpcClient<PricingService.PricingServiceClient>(
    new Uri(builder.Configuration["Services:Pricing"] ?? "http://pricing:8081"));
builder.Services.AddHbaGrpcClient<PaymentService.PaymentServiceClient>(
    new Uri(builder.Configuration["Services:Payment"] ?? "http://payment:8081"));

// BILLING N'AVAIT AUCUN CLIENT ICI, ET C'EST CE QUI BOUCHAIT TOUT LE B2B.
//
// « OpenAccount » et « Credit » n'existaient que sur le gRPC interne, que le
// proxy ne route pas : aucun chemin ne permettait d'ouvrir un compte de
// facturation ni d'y porter une recharge. La premiere course de tout partenaire
// echouait donc en « compte introuvable » — apres avoir consomme son devis.
builder.Services.AddHbaGrpcClient<BillingService.BillingServiceClient>(
    new Uri(builder.Configuration["Services:Billing"] ?? "http://billing:8081"));

// LA PASSERELLE RESOUT ELLE-MEME LA FENETRE des indicateurs, puis l'impose
// aux quatre services (ADR 0019 et 0020). Chacun sait calculer la sienne par
// defaut, et les quatre tomberaient d'accord tant que leur configuration est
// identique — « tant que » etant precisement le probleme.
builder.Services.AddHbaTime(builder.Configuration);

builder.Services.AddHttpClient<IWebhookSender, WebhookSender>();

// RELAIS DES PIECES DU DOSSIER LIVREUR (ADR 0021). Le seul appel interne en
// HTTP du depot, et une exception assumee a l'ADR 0002 : gRPC porte mal les
// binaires, dont les intercepteurs de trace n'ont pas a voir le contenu.
//
// LE PORT 8080, PAS 8081 : c'est la surface HTTP du service, pas sa surface
// gRPC. Se tromper donne un « HTTP_1_1_REQUIRED » qui ne designe pas sa cause.
//
// Le delai est LONG par rapport aux appels gRPC : cinq megaoctets depuis un
// telephone en 3G a Cotonou prennent bien plus que les cinq secondes par
// defaut, et un envoi coupe fait recommencer le livreur.
builder.Services.AddHttpClient<IDriverUploadRelay, DriverUploadRelay>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:DriverHttp"] ?? "http://driver:8080");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// RELAIS DE LA PHOTO DE PROFIL DU CLIENT VERS MEDIA (point 27). Meme forme
// que celui des pieces du dossier livreur juste au-dessus, et pour la meme
// raison : gRPC porte mal les binaires. La difference est que Media est un
// service dont c'est le METIER — ce n'est plus une exception a l'ADR 0015.
//
// LE PORT 8080, PAS 8081 : c'est la surface HTTP du service, pas sa surface
// gRPC. Se tromper donne un « HTTP_1_1_REQUIRED » qui ne designe pas sa cause.
builder.Services.AddHttpClient<IMediaUploadRelay, MediaUploadRelay>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:MediaHttp"] ?? "http://media:8080");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// LE TROISIEME RELAIS D'OCTETS, ET LE SEUL QUI NE VISE PAS UN SERVICE DE
// STOCKAGE. La photo d'une etape part vers DELIVERY : lui seul sait si ce
// livreur est affecte a cette course, et c'est lui qui deposera ensuite chez
// Media avec un jeton de service. La passerelle n'en detient aucun, et c'est
// delibere — lui en donner un ferait d'elle un appelant de confiance pour tous
// les medias de la plateforme.
//
// LE PORT 8080, PAS 8081 : la surface HTTP de Delivery, pas sa surface gRPC. Ce
// service n'ouvre qu'une seule route HTTP, et c'est celle-ci.
builder.Services.AddHttpClient<IPreuveRelay, PreuveRelay>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:DeliveryHttp"] ?? "http://delivery:8080");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// YARP NE PORTE QUE LE RELAIS PUR, ET C'EST TOUT CE QU'IL SAIT FAIRE. Un
// reverse proxy relaie du HTTP vers du HTTP : il ne sait ni fabriquer un
// message protobuf a partir d'un corps JSON, ni appeler deux services et
// fusionner leurs reponses. Les routes qui en ont besoin restent des endpoints
// ecrits a la main, juste en dessous. Ici, seules passent les routes qu'un
// service expose deja en HTTP et telles quelles : la decouverte JWKS
// d'Identity, dont les applications et les navigateurs ont besoin sans avoir a
// connaitre un second nom d'hote.
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Avant tout le reste : sans l'adresse reelle du client, la limitation de debit
// partitionnerait sur l'adresse de Traefik et punirait tout le monde d'un coup.
app.UseForwardedHeaders();
app.UseHbaRequestLogging();
app.UseRateLimiter();
app.UseRpcExceptionTranslation();
app.UseAuthentication();
app.UseAuthorization();

// HORS DEVELOPPEMENT, RIEN N'EST EXPOSE PAR DEFAUT. Cette page decrit d'un coup
// les routes client, livreur, back-office ET administration : publiee sans y
// penser, elle donne a un inconnu la carte complete de la surface d'attaque,
// noms de champs compris. Swagger:Enabled permet de l'ouvrir sur un
// environnement de recette sans recompiler — c'est une decision explicite, pas
// un defaut.
if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(ui =>
    {
        foreach (var surface in ApiSurfaces.All)
        {
            ui.SwaggerEndpoint($"/swagger/{surface.Name}/swagger.json", surface.Title);
        }
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Retour du payeur apres la page du fournisseur. Ne conclut rien : voir le
// commentaire de la classe.
app.MapPaymentReturnEndpoint();

// ---------------------------------------------------------------- Client ---
app.MapClientAuthEndpoints();
app.MapClientProfileEndpoints();
app.MapClientDeliveryEndpoints();
app.MapClientNearbyEndpoints();

// --------------------------------------------------------------- Livreur ---
app.MapDriverAuthEndpoints();
app.MapDriverEndpoints();
app.MapDriverApplicationEndpoints();

// ----------------------------------------------- Portail et back-office ---
app.MapWebAuthEndpoints();
app.MapMerchantEndpoints();
app.MapMerchantDirectoryEndpoints();
app.MapBackOfficeEndpoints();
app.MapKpiEndpoints();
app.MapAdminDirectoryEndpoints();
app.MapAdminPayoutEndpoints();
app.MapAdminBillingEndpoints();

// ----------------------------------------------------------- Partenaires ---
app.MapPartnerTokenEndpoint();
app.MapPartnerEndpoints();

// En dernier : les routes ecrites ci-dessus l'emportent toujours sur le relais.
app.MapReverseProxy();

await app.RunAsync();

/// <summary>Rendu public pour les tests d'integration.</summary>
public partial class Program;
