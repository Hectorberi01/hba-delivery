using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Http;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Media.Api.Endpoints;
using Hba.Media.Application;
using Hba.Media.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("media");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddSingleton<TraduireLesRefus>();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddMediaApplication();
builder.Services.AddMediaInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Media.Api.Grpc.MediaGrpcService>();
app.MapGrpcHealthChecksService();

// LA ROUTE QUI JUSTIFIE LE SERVICE. Les octets entrent ici, en HTTP, et
// nulle part ailleurs : c'est le metier de Media, pas une exception arrachee a
// l'ADR 0015.
app.MapMediaUploadEndpoints();

app.MapGet("/", () => Results.Text("Hba.Media.Api", "text/plain"));

await app.RunAsync();
