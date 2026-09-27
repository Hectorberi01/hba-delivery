using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Api.Endpoints;
using Hba.Payment.Application;
using Hba.Payment.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("payment");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddPaymentApplication();
builder.Services.AddPaymentInfrastructure(builder.Configuration, builder.Environment);

// UNE COURSE LIVREE CREDITE LE COMPTE DE SON LIVREUR. Premier consommateur
// de ce service, qui jusqu'ici ne faisait que publier.
builder.Services.AddHostedService<Hba.Payment.Api.Messaging.DeliveryEventsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Payment.Api.Grpc.PaymentGrpcService>();
app.MapGrpcHealthChecksService();

// SUR LE PORT HTTP, PAS SUR LE PORT GRPC. Le fournisseur appelle depuis
// l'exterieur, en HTTP/1.1 : il ne sait rien de notre port 8081 en HTTP/2.
app.MapFedaPayWebhook();

app.MapGet("/", () => Results.Text("Hba.Payment.Api", "text/plain"));

await app.RunAsync();
