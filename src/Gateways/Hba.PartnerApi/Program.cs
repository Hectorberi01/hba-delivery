using Hba.BuildingBlocks.Grpc.Extensions;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Identity.V1;
using Hba.Contracts.Pricing.V1;
using Hba.PartnerApi.Endpoints;
using Hba.PartnerApi.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("partner-api");

// Les partenaires s'authentifient en OAuth2 client credentials auprès d'Identity ;
// ici, on valide le jeton résultant comme n'importe quel autre.
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

builder.Services.AddHttpClient<IWebhookSender, WebhookSender>();

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
app.MapPartnerTokenEndpoint();
app.MapPartnerEndpoints();

await app.RunAsync();
