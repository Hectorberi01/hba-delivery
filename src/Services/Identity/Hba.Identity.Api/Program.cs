using System.Text.Json.Serialization;
using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Identity.Api.Bootstrap;
using Hba.Identity.Api.Endpoints;
using Hba.Identity.Api.Grpc;
using Hba.Identity.Api.Middleware;
using Hba.Identity.Api.Scheduling;
using Hba.Identity.Application;
using Hba.Identity.Application.Features.Accounts.Commands;
using Hba.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("identity");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

// SURFACE REST, EN PLUS DU GRPC. Les deux passent par le meme dispatcher et
// donc par les memes handlers : la regle metier n'existe qu'a un endroit.
//
// Le convertisseur d'enum n'est pas cosmetique : sans lui AccountStatus sort
// en entier, et un client qui lit « 2 » ne sait pas si le compte est suspendu
// ou supprime. Le gRPC, lui, a des enums nommes dans le contrat proto.
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// LE BFF LIMITE PAR ADRESSE, ET CETTE SURFACE DOIT LE FAIRE AUSSI. Ouvrir une
// seconde porte sans la meme limitation reviendrait a publier un contournement
// des plafonds des passerelles : c'est toujours la porte la plus faible qui
// est empruntee. La limitation par numero, elle, reste dans le handler OTP.
builder.Services.AddHbaAuthRateLimiter();

// UN CORPS MAL FORME NE PEUT PAS REPONDRE AUTREMENT QUE LE RESTE. Par defaut
// [ApiController] renvoie un ProblemDetails { type, title, status, errors },
// alors que toutes les autres erreurs d'HBA sont { code, message }. Deux
// formats sur la meme route obligent chaque client a savoir lequel il vient
// de recevoir.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .Select(entry => $"{entry.Key} : {entry.Value!.Errors[0].ErrorMessage}");

        return new BadRequestObjectResult(new
        {
            code = "VALIDATION_FAILED",
            message = string.Join(" ", details),
        })
        { ContentTypes = { "application/problem+json" } };
    };
});

// Identity valide les jetons qu'il émet lui-même : les appels d'administration
// passent par le même contrôle que partout ailleurs.
builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddIdentityApplication();
builder.Services.AddIdentityInfrastructure(builder.Configuration, builder.Environment);

// Le premier administrateur ne peut pas être créé par l'API : elle exige déjà
// le rôle admin. Il naît ici, une seule fois.
builder.Services.AddOptions<BootstrapOptions>()
    .Bind(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddHostedService<AdminBootstrap>();

// LA SUPPRESSION DE COMPTE, DEMANDEE PAR SON TITULAIRE (point 28).
//
// Le delai de grace et le rythme du balayage se reglent ici plutot que dans le
// code : la duree est un choix de produit, qui peut devoir suivre une exigence
// de magasin. Changer le reglage NE DEPLACE AUCUNE ECHEANCE DEJA ANNONCEE — la
// date est figee dans le compte au moment de la demande.
builder.Services.AddOptions<AccountDeletionOptions>()
    .Bind(builder.Configuration.GetSection(AccountDeletionOptions.Section));
builder.Services.AddHostedService<PurgeDesComptes>();

// SANS AddEndpointsApiExplorer, SwaggerGen NE PEUT PAS ETRE CONSTRUIT.
// SwaggerGenerator prend IApiDescriptionGroupCollectionProvider dans son
// constructeur, et c'est cette ligne qui l'enregistre. Sans elle, aucun
// constructeur n'est satisfaisable : le conteneur echoue a la validation,
// l'hote entier tombe, et « dotnet ef » ne peut plus construire le DbContext.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

// HORS DEVELOPPEMENT, RIEN N'EST EXPOSE. Identity publie sinon la totalite de
// sa surface d'API — routes d'authentification comprises — a la racine du
// domaine, sans authentification. Les quatre passerelles appliquent deja cette
// garde ; Identity etait la seule sans.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HBA Identity v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHbaRequestLogging();

// AVANT L'AUTHENTIFICATION, pour couvrir tout ce qui suit. C'est l'equivalent
// HTTP de l'ExceptionInterceptor gRPC : sans lui une DomainException sortirait
// en 500 nu sur les routes REST alors qu'elle sort cadree en gRPC.
app.UseHbaDomainExceptions();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<IdentityGrpcService>();
app.MapGrpcHealthChecksService();
app.MapIdentityDiscovery();

app.MapGet("/", () => Results.Text(
    "Hba.Identity.Api — jetons, JWKS, et API REST sous /api/v1. "
    + "Les applications passent normalement par les BFF.",
    "text/plain")).AllowAnonymous();

await app.RunAsync();

/// <summary>Rendu public pour les tests d'intégration.</summary>
public partial class Program;
