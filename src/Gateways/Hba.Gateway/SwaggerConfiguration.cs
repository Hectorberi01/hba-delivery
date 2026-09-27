using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Hba.Gateway;

/// <summary>
/// Documentation des quatre surfaces.
///
/// UNE PAGE QUI NE PERMET PAS D'APPELER NE SERT QU'A MOITIE. Sans schema de
/// securite declare, Swagger UI n'affiche pas le bouton « Authorize » : on peut
/// lire les routes, mais n'essayer que les anonymes — c'est-a-dire quatre
/// routes sur une cinquantaine. Le reste renvoie 401 sans que la page dise
/// pourquoi.
/// </summary>
public static class SwaggerConfiguration
{
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddHbaGatewayDocumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            foreach (var surface in ApiSurfaces.All)
            {
                options.SwaggerDoc(surface.Name, new()
                {
                    Title = surface.Title,
                    Version = "v1",
                    Description = surface.Description,
                });
            }

            // Le prefixe d'URL EST la frontiere entre les surfaces : c'est lui
            // qui dit a quelle application une route s'adresse. L'utiliser ici
            // evite d'etiqueter quinze groupes de routes a la main, et evite
            // surtout qu'une etiquette oubliee fasse disparaitre une route de
            // la documentation sans que rien ne le signale.
            options.DocInclusionPredicate((document, description) =>
                ApiSurfaces.Resolve(description.RelativePath) == document);

            // Regroupe par ressource — auth, deliveries, accounts, partners —
            // plutot que par nom de controleur, qui n'existe pas ici.
            options.TagActionsBy(description => [ApiSurfaces.Tag(description.RelativePath)]);

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "Jeton d'acces obtenu par /api/<surface>/v1/auth. "
                    + "Coller la valeur brute du champ accessToken, sans le prefixe « Bearer ».",
            });

            // PAS D'EXIGENCE GLOBALE, A DESSEIN. AddSecurityRequirement pose le
            // cadenas sur TOUTES les routes, y compris /auth/otp/request, qui
            // est anonyme : la page laisserait croire qu'il faut deja un jeton
            // pour en demander un. Le filtre ci-dessous ne le pose que la ou
            // l'autorisation est reellement exigee.
            options.OperationFilter<AuthorizedOperationFilter>();
        });

        return services;
    }

    /// <summary>
    /// Pose l'exigence de jeton sur les seules operations protegees, en lisant
    /// les metadonnees de l'endpoint plutot qu'en devinant d'apres l'URL.
    /// </summary>
    private sealed class AuthorizedOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(context);

            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

            // AllowAnonymous l'emporte : c'est aussi la regle d'ASP.NET Core.
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return;
            }

            if (!metadata.OfType<IAuthorizeData>().Any())
            {
                return;
            }

            // Les codes 401 et 403 ne sont pas declares ici : dans OpenAPI.NET v2
            // le dictionnaire des reponses porte l'interface IOpenApiResponse, et
            // fabriquer l'implementation demande un type dont le nom a change
            // avec la version. Le cadenas suffit a rendre la page utilisable.
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerScheme)] = new List<string>(),
                },
            ];
        }
    }
}
