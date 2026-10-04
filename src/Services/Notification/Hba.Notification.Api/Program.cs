using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Notification.Api.Grpc;
using Hba.Notification.Api.Messaging;
using Hba.Notification.Application.Extensions;
using Hba.Notification.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("notification");

// UN SERVICE GRPC DEPUIS LE 2 OCTOBRE 2026, ET IL NE SAIT QUE LIRE. Envoyer
// reste un dépôt Kafka ; ce qui s'ouvre ici, c'est le journal des envois, pour
// que le support puisse répondre à « je n'ai pas reçu mon code ».
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();
builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddNotificationApplication();
builder.Services.AddNotificationInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddHostedService<NotificationCommandsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<NotificationGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text(
    "Hba.Notification.Api — consommateur de hba.notification.commands.v1.",
    "text/plain"));

await app.RunAsync();
