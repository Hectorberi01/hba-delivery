using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Billing.Application;
using Hba.Billing.Infrastructure;

// L'ENTREE EST GRPC ET SYNCHRONE, ET C'EST TOUT L'INTERET DE CE SERVICE.
// Delivery debite AVANT de creer la course : un donneur d'ordre au plafond doit
// l'apprendre tout de suite, pas apres un « 201 Created » suivi d'un echec
// silencieux.
//
// AUCUNE ENTREE POUR LES APPLICATIONS. Le client et le livreur n'ont rien a
// faire ici ; l'appelant est Delivery.
//
// MAIS PAS AVEC UN JETON DE SERVICE, ET CETTE PHRASE DISAIT LE CONTRAIRE.
// Delivery appelle a travers TokenForwardingInterceptor, qui reporte LE JETON DE
// L'UTILISATEUR FINAL : c'est un porteur de jeton commercant ou partenaire qui
// arrive a cette porte. C'est pour cela que BillingAccess existe, et c'est cette
// phrase-ci qui fera rouvrir la porte le jour ou quelqu'un le trouvera trop
// strict.
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("billing");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddBillingApplication();
builder.Services.AddBillingInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Billing.Api.Grpc.BillingGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text(
    "Hba.Billing.Api — comptes de facturation des donneurs d'ordre. "
    + "Les applications passent par les BFF ; l'appel vient de Delivery.",
    "text/plain"));

await app.RunAsync();

/// <summary>Rendu public pour les tests d'integration (WebApplicationFactory).</summary>
public partial class Program;
