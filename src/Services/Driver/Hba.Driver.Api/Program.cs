using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Driver.Application.Extensions;
using Hba.Driver.Infrastructure.Extensions;

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

builder.Services.AddDriverApplication();
builder.Services.AddDriverInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Driver.Api.Grpc.DriverGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Driver.Api", "text/plain"));

await app.RunAsync();
