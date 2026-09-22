using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Hba.Bff.Client.Endpoints;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Directory.V1;
using Hba.Contracts.Identity.V1;
using Hba.Contracts.Pricing.V1;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("bff-client");

// Le BFF valide aussi le jeton, mais il ne décide rien : les services refont la
// vérification pour leur compte.
builder.Services.AddHbaSecurity(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHbaAuthRateLimiter();
builder.Services.AddHbaForwardedHeaders(builder.Configuration);

builder.Services.AddHbaGrpcClient<DeliveryService.DeliveryServiceClient>(
    new Uri(builder.Configuration["Services:Delivery"] ?? "http://delivery:8080"));
builder.Services.AddHbaGrpcClient<PricingService.PricingServiceClient>(
    new Uri(builder.Configuration["Services:Pricing"] ?? "http://pricing:8080"));
builder.Services.AddHbaGrpcClient<IdentityService.IdentityServiceClient>(
    new Uri(builder.Configuration["Services:Identity"] ?? "http://identity:8080"));
builder.Services.AddHbaGrpcClient<DirectoryService.DirectoryServiceClient>(
    new Uri(builder.Configuration["Services:Directory"] ?? "http://directory:8080"));

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
app.MapClientAuthEndpoints();
app.MapClientProfileEndpoints();
app.MapClientDeliveryEndpoints();

await app.RunAsync();
