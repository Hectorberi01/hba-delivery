using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Api.Bootstrap;
using Hba.Identity.Api.Endpoints;
using Hba.Identity.Api.Grpc;
using Hba.Identity.Application.Extensions;
using Hba.Identity.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("identity");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

// Identity valide les jetons qu'il émet lui-même : les appels d'administration
// passent par le même contrôle que partout ailleurs.
builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddIdentityApplication();
builder.Services.AddIdentityInfrastructure(builder.Configuration);

// Le premier administrateur ne peut pas être créé par l'API : elle exige déjà
// le rôle admin. Il naît ici, une seule fois.
builder.Services.AddOptions<BootstrapOptions>()
    .Bind(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddHostedService<AdminBootstrap>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<IdentityGrpcService>();
app.MapGrpcHealthChecksService();
app.MapIdentityDiscovery();

app.MapGet("/", () => Results.Text(
    "Hba.Identity.Api — jetons et JWKS. Les applications passent par les BFF.",
    "text/plain"));

await app.RunAsync();

/// <summary>Rendu public pour les tests d'intégration.</summary>
public partial class Program;
