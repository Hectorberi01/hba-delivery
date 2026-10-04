using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Rating.Api.Messaging;
using Hba.Rating.Application;
using Hba.Rating.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("rating");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddRatingApplication();
builder.Services.AddRatingInfrastructure(builder.Configuration);

// UNE NOTATION NAIT D'UNE MISSION TERMINEE, JAMAIS D'UN APPEL.
//
// Les deux consommateurs sont le SEUL chemin normal : c'est la que se trouvent
// le client et le livreur du trajet, et c'est ce qui empeche un client de noter
// quelqu'un qui ne l'a jamais servi.
//
// DEUX CONSOMMATEURS ET PAS UN, parce qu'il y a deux contrats. La lecon vient du
// meme jour : Driver n'ecoutait que Delivery, et une mission sur deux laissait
// son chauffeur bloque en ON_MISSION. Toute nature de mission qui peut etre
// notee doit avoir ici de quoi ouvrir sa notation.
builder.Services.AddHostedService<RideEventsConsumer>();
builder.Services.AddHostedService<DeliveryEventsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Rating.Api.Grpc.RatingGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Rating.Api", "text/plain"));

await app.RunAsync();
