using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Ride.V1;
using Hba.Realtime.Api.Diffusion;
using Hba.Realtime.Api.Hubs;
using Hba.Realtime.Api.Messaging;
using Hba.Realtime.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("realtime");

builder.Services.AddHbaSecurity(builder.Configuration);

// ---- LE JETON DANS LA CHAINE DE REQUETE, ET SEULEMENT POUR LES HUBS --------
//
// UN WEBSOCKET NE PORTE PAS D'EN-TETE AUTHORIZATION : le navigateur ne permet pas d'en
// poser sur l'ouverture, et la convention de SignalR est donc « access_token » dans la
// chaine de requete. Sans ce bloc, toute connexion a un hub est refusee en 401, et le
// message d'erreur ne dit rien d'utile.
//
// RESTREINT AUX CHEMINS DES HUBS, ET CE N'EST PAS DE LA PRUDENCE DECORATIVE. Un jeton
// dans une URL se retrouve dans les journaux d'acces, dans l'historique d'un
// navigateur, et dans l'en-tete Referer envoye au site suivant. L'accepter partout
// ouvrirait cette fuite a toute l'API ; ici il n'y a que trois chemins, qu'aucun
// navigateur n'affiche et qui ne mènent vers aucun autre site.
builder.Services.PostConfigure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        options.Events ??= new JwtBearerEvents();

        options.Events.OnMessageReceived = contexte =>
        {
            var chemin = contexte.HttpContext.Request.Path;

            if (chemin.StartsWithSegments("/hubs"))
            {
                var jeton = contexte.Request.Query["access_token"].ToString();

                if (!string.IsNullOrWhiteSpace(jeton))
                {
                    contexte.Token = jeton;
                }
            }

            return Task.CompletedTask;
        };
    });

// ---- REDIS : LE BACKPLANE ET L'ANNUAIRE, SUR LA MEME CONNEXION -------------
//
// IL EST OBLIGATOIRE, ET POUR DEUX RAISONS QUI SE CUMULENT.
//
// 1. LE BACKPLANE. Un evenement Kafka n'arrive que sur une replique ; sans backplane,
//    les clients connectes aux autres ne recoivent jamais rien. Avec deux repliques,
//    la moitie des ecrans reste figee, sans aucune erreur nulle part.
// 2. L'ANNUAIRE DES MISSIONS. C'est lui qui autorise le passage d'une position vers le
//    client d'une mission. En memoire, il serait propre a chaque replique, et une
//    replique qui recoit un battement sans avoir vu l'affectation ne saurait pas ou
//    l'envoyer.
//
// Sans Redis, ce service demarrerait, accepterait les connexions, et ne diffuserait
// presque rien. C'est la panne invisible qu'on refuse : il s'arrete ici.
var redis = builder.Configuration.GetConnectionString("Redis");

if (string.IsNullOrWhiteSpace(redis))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Redis est absent. Il porte le backplane SignalR ET l'annuaire des "
        + "missions : sans lui ce service accepterait des connexions sans rien leur envoyer.");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
})
.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = RedisChannel.Literal("hba-rt"));

builder.Services.AddOptions<RealtimeOptions>()
    .Bind(builder.Configuration.GetSection(RealtimeOptions.SectionName))
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.PositionChannel),
        "Realtime:PositionChannel est obligatoire, et doit valoir exactement Location:PositionChannel. "
        + "S'ils divergent, aucune position n'arrive et rien ne le signale.")
    .Validate(
        o => o.MissionDriverTtlHours is >= 1 and <= 168,
        "Realtime:MissionDriverTtlHours doit tenir entre 1 et 168 heures. C'est un garde-fou contre "
        + "une association mission-chauffeur dont la cloture aurait ete perdue.")
    .ValidateOnStart();

// LES OPTIONS KAFKA SANS AddHbaMessaging, ET C'EST VOLONTAIRE. Cette extension
// enregistre aussi un producteur et l'expediteur de l'outbox, qui exigent une base de
// donnees. Ce service ne PRODUIT rien et n'a pas de base : il ne lui faut que de quoi
// consommer.
builder.Services.AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName))
    .Configure(o => o.ConsumerGroupId = "realtime")
    .ValidateOnStart();

builder.Services.AddSingleton<Presences>();
builder.Services.AddSingleton<AnnuaireDesMissions>();

// ---- LES SERVICES PROPRIETAIRES, POUR VERIFIER UN ABONNEMENT ---------------
//
// LE HUB NE DECIDE PAS SI UNE MISSION EST LA SIENNE : il appelle Ride ou Delivery avec
// le jeton du client et regarde s'ils repondent. Voir CustomerHub.
//
// L'ECHEANCE EST COURTE : cet appel est sur le chemin de l'ouverture d'un ecran de
// suivi. Un abonnement qui mettrait trente secondes a etre accorde serait pire qu'un
// refus — l'utilisateur regarde une carte vide sans savoir si elle va se remplir.
builder.Services.AddHbaGrpcClient<RideService.RideServiceClient>(
    new Uri(builder.Configuration["Services:Ride"] ?? "http://ride:8081"),
    TimeSpan.FromSeconds(3));

builder.Services.AddHbaGrpcClient<DeliveryService.DeliveryServiceClient>(
    new Uri(builder.Configuration["Services:Delivery"] ?? "http://delivery:8081"),
    TimeSpan.FromSeconds(3));

builder.Services.AddHostedService<RideEventsDiffuseur>();
builder.Services.AddHostedService<DeliveryEventsDiffuseur>();
builder.Services.AddHostedService<DispatchEventsDiffuseur>();
builder.Services.AddHostedService<DiffuseurDePositions>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

// ---- LES TROIS HUBS -------------------------------------------------------
//
// TROIS PLUTOT QU'UN SEUL AVEC DES GROUPES, et la raison est l'autorisation : chaque
// hub porte sa politique dans son attribut, verifiee par le cadre avant qu'une seule
// ligne de ce service ne tourne. Un hub unique aurait demande de verifier le role a la
// main dans chaque methode, et le jour ou l'on en ajoute une, l'oubli ne se voit pas.
app.MapHub<CustomerHub>("/hubs/client");
app.MapHub<DriverHub>("/hubs/driver");
app.MapHub<OpsHub>("/hubs/ops");

app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Text("Hba.Realtime.Api", "text/plain"));

await app.RunAsync();
