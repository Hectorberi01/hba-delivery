using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Hba.Bff.Driver.Endpoints;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Identity.V1;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("bff-driver");

builder.Services.AddHbaSecurity(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHbaAuthRateLimiter();
builder.Services.AddHbaForwardedHeaders(builder.Configuration);

builder.Services.AddHbaGrpcClient<DeliveryService.DeliveryServiceClient>(
    new Uri(builder.Configuration["Services:Delivery"] ?? "http://delivery:8080"));
builder.Services.AddHbaGrpcClient<DispatchService.DispatchServiceClient>(
    new Uri(builder.Configuration["Services:Dispatch"] ?? "http://dispatch:8080"));
builder.Services.AddHbaGrpcClient<DriverService.DriverServiceClient>(
    new Uri(builder.Configuration["Services:Driver"] ?? "http://driver:8080"));
builder.Services.AddHbaGrpcClient<IdentityService.IdentityServiceClient>(
    new Uri(builder.Configuration["Services:Identity"] ?? "http://identity:8080"));

var app = builder.Build();

// Avant tout le reste : sans l'adresse réelle du client, la limitation de
// débit partitionnerait sur l'adresse de Traefik.
app.UseForwardedHeaders();
app.UseHbaRequestLogging();
app.UseRateLimiter();
app.UseRpcExceptionTranslation();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapDriverAuthEndpoints();
app.MapDriverEndpoints();

await app.RunAsync();
