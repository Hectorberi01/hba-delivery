using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Notification.Api.Messaging;
using Hba.Notification.Application.Extensions;
using Hba.Notification.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("notification");

// Aucun service gRPC : Notification n'a que des consommateurs. L'hôte web
// existe pour la sonde de santé et pour la pile d'observabilité.
builder.Services.AddGrpc();
builder.Services.AddGrpcHealthChecks();
builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddNotificationApplication();
builder.Services.AddNotificationInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddHostedService<NotificationCommandsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text(
    "Hba.Notification.Api — consommateur de hba.notification.commands.v1.",
    "text/plain"));

await app.RunAsync();
