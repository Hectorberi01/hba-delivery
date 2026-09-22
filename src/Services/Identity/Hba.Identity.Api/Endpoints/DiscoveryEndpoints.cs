using Hba.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Hba.Identity.Api.Endpoints;

/// <summary>
/// Points d'entrée HTTP consommés par le middleware JwtBearer de chaque service.
/// Ils sont volontairement hors gRPC : c'est un protocole standard, et les
/// bibliothèques clientes savent déjà le lire.
/// </summary>
public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapIdentityDiscovery(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/.well-known/openid-configuration", (
            HttpContext http,
            IOptions<TokenOptions> tokens) =>
        {
            var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";

            return Results.Json(new
            {
                issuer = tokens.Value.Issuer,
                jwks_uri = $"{baseUrl}/.well-known/jwks.json",
                // HBA Delivery n'implémente pas OpenID Connect : ce document
                // n'existe que pour que les services trouvent les clés. Les
                // jetons s'obtiennent par gRPC ou par les BFF.
                response_types_supported = new[] { "token" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                grant_types_supported = new[] { "client_credentials", "refresh_token" },
            });
        }).AllowAnonymous();

        app.MapGet("/.well-known/jwks.json", (ISigningKeyProvider keys) =>
        {
            // Sérialisation explicite : seuls les paramètres publics sortent,
            // et on ne dépend pas du format interne de JsonWebKey.
            var payload = new
            {
                keys = keys.PublicKeys.Select(key => new
                {
                    kty = key.Kty,
                    use = key.Use,
                    alg = key.Alg,
                    kid = key.Kid,
                    n = key.N,
                    e = key.E,
                }).ToArray(),
            };

            return Results.Json(payload);
        }).AllowAnonymous();

        return app;
    }
}
