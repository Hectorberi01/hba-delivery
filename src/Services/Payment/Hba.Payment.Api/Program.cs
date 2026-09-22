using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Extensions;
using Hba.Payment.Infrastructure.Extensions;

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
builder.Services.AddPaymentInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Payment.Api.Grpc.PaymentGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Payment.Api", "text/plain"));

await app.RunAsync();
