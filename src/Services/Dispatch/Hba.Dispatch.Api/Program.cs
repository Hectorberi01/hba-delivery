using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Application.Extensions;
using Hba.Dispatch.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("dispatch");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddDispatchApplication();
builder.Services.AddDispatchInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Dispatch.Api.Grpc.DispatchGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Dispatch.Api", "text/plain"));

await app.RunAsync();
