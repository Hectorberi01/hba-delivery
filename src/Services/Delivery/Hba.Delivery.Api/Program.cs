using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Api.Grpc;
using Hba.Delivery.Api.Messaging;
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

builder.Services.AddDeliveryApplication();
builder.Services.AddDeliveryInfrastructure(builder.Configuration);

// Entrées asynchrones. Elles vivent dans le même hôte que l'entrée gRPC :
// un seul déployable par service.
builder.Services.AddHostedService<PaymentEventsConsumer>();
builder.Services.AddHostedService<DispatchEventsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<DeliveryGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text(
    "Hba.Delivery.Api — service gRPC. Les applications passent par les BFF.",
    "text/plain"));

await app.RunAsync();

/// <summary>Rendu public pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
