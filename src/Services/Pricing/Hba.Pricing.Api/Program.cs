using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Pricing.Application.Extensions;
using Hba.Pricing.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("pricing");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddPricingApplication();
builder.Services.AddPricingInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Pricing.Api.Grpc.PricingGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Pricing.Api", "text/plain"));

await app.RunAsync();
