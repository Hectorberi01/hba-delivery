using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Api.Endpoints;
using Hba.Driver.Api.Messaging;
using Hba.Driver.Application;
using Hba.Driver.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("driver");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

// Traducteur des refus metier pour la surface HTTP (ADR 0021).
builder.Services.AddSingleton<Hba.Driver.Api.Endpoints.TraduireLesRefus>();

builder.Services.AddDriverApplication();
builder.Services.AddDriverInfrastructure(builder.Configuration);

// LE PROFIL LIVREUR NAIT D'UN COMPTE, pas d'un appel : Identity cree le
// compte, Driver en tire un profil quand il porte le role livreur.
builder.Services.AddHostedService<IdentityEventsConsumer>();

// L'etat operationnel suit les offres du dispatch : reserve, libere, en mission.
builder.Services.AddHostedService<DispatchEventsConsumer>();

// ET IL EN REDESCEND QUAND LA COURSE S'ACHEVE. Dispatch sait qui a gagne une
// course, pas quand elle se termine : sans ce consommateur, un livreur monte
// jusqu'a ON_MISSION et n'en sort jamais.
builder.Services.AddHostedService<DeliveryEventsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Driver.Api.Grpc.DriverGrpcService>();
app.MapGrpcHealthChecksService();

// Depot des pieces : la seule route HTTP interne du depot (ADR 0021).
app.MapDriverDocumentEndpoints();

app.MapGet("/", () => Results.Text("Hba.Driver.Api", "text/plain"));

await app.RunAsync();
