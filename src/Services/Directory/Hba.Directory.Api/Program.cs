using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Directory.Api.Grpc;
using Hba.Directory.Api.Messaging;
using Hba.Directory.Application.Extensions;
using Hba.Directory.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("directory");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddDirectoryApplication();
builder.Services.AddDirectoryInfrastructure(builder.Configuration);

// Un compte créé dans Identity donne un profil ici.
builder.Services.AddHostedService<IdentityEventsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<DirectoryGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text(
    "Hba.Directory.Api — profils clients et commerçants. Les applications passent par les BFF.",
    "text/plain"));

await app.RunAsync();

/// <summary>Rendu public pour les tests d'intégration.</summary>
public partial class Program;
