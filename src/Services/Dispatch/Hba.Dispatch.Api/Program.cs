using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Dispatch.Api.Messaging;
using Hba.Dispatch.Api.Scheduling;
using Hba.Dispatch.Application;
using Hba.Dispatch.Infrastructure;

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

// « DeliveryConfirmed » ouvre la recherche ; le planificateur la fait avancer.
builder.Services.AddHostedService<DeliveryEventsConsumer>();
builder.Services.AddHostedService<DispatchScheduler>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Dispatch.Api.Grpc.DispatchGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Dispatch.Api", "text/plain"));

await app.RunAsync();
