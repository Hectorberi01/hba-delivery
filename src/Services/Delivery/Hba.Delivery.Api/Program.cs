using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Http;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Api.Endpoints;
using Hba.Delivery.Api.Grpc;
using Hba.Delivery.Api.Messaging;
using Hba.Delivery.Api.Scheduling;
using Hba.Delivery.Application.Commands.Internal;
using Hba.Delivery.Application.Extensions;
using Hba.Delivery.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("delivery");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

// L'autorisation est vérifiée ICI, à partir du JWT, et pas seulement au BFF.
builder.Services.AddHbaSecurity(builder.Configuration);

// LE FILTRE DE LA ROUTE HTTP. Sans lui, un refus metier sort en 500 avec un
// corps vide : l'intercepteur gRPC ne couvre pas les routes minimales, et les
// codes des deux chemins doivent rester les memes.
builder.Services.AddSingleton<TraduireLesRefus>();

builder.Services.AddDeliveryApplication();
builder.Services.AddDeliveryInfrastructure(builder.Configuration);

// Entrées asynchrones. Elles vivent dans le même hôte que l'entrée gRPC :
// un seul déployable par service.
builder.Services.AddHostedService<PaymentEventsConsumer>();
builder.Services.AddHostedService<DispatchEventsConsumer>();

// Le seul composant qui referme une course que personne n'a payee.
builder.Services.AddOptions<UnpaidDeliveryOptions>()
    .Bind(builder.Configuration.GetSection(UnpaidDeliveryOptions.Section));
builder.Services.AddHostedService<AbandonDesImpayees>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<DeliveryGrpcService>();
app.MapGrpcHealthChecksService();

// LA SEULE ROUTE HTTP DE CE SERVICE, et elle porte des octets : une photo de
// collecte ou de remise. Voir PreuveEndpoints — gRPC porte mal les binaires
// (ADR 0021), et le point 7 a tranche que c'est Delivery qui les porte.
app.MapPreuveEndpoints();

app.MapGet("/", () => Results.Text(
    "Hba.Delivery.Api — service gRPC. Les applications passent par les BFF.",
    "text/plain"));

await app.RunAsync();

/// <summary>Rendu public pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
