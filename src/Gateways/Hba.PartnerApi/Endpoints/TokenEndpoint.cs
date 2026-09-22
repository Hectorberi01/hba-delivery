using System.Text.Json.Serialization;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Identity.V1;

namespace Hba.PartnerApi.Endpoints;

/// <summary>
/// OAuth2 client credentials. C'est par là qu'un partenaire obtient son jeton ;
/// sans cette route, le contrat OAuth2 n'existait que dans Identity, en gRPC,
/// donc hors de portée d'un système extérieur.
///
/// Pas de jeton de rafraîchissement : un système n'a pas de session, il
/// redemande un jeton quand le sien expire.
/// </summary>
public static class TokenEndpoint
{
    public static IEndpointRouteBuilder MapPartnerTokenEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/oauth/token", async (
            TokenRequestDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(body.GrantType, "client_credentials", StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    error = "unsupported_grant_type",
                    error_description = "Seul client_credentials est accepté.",
                });
            }

            var pair = await identity.IssuePartnerTokenAsync(
                BuildRequest(body),
                cancellationToken: cancellationToken);

            // Forme de réponse OAuth2 : les bibliothèques clientes des
            // partenaires la reconnaissent sans code spécifique.
            return Results.Ok(new
            {
                access_token = pair.AccessToken,
                token_type = "Bearer",
                expires_in = pair.ExpiresInSeconds,
                scope = string.Join(' ', body.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []),
            });
        })
        .AllowAnonymous()
        .RequireRateLimiting(HbaRateLimitPolicies.PartnerToken);

        return app;
    }

    private static IssuePartnerTokenRequest BuildRequest(TokenRequestDto body)
    {
        var request = new IssuePartnerTokenRequest
        {
            ClientId = body.ClientId,
            ClientSecret = body.ClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(body.Scope))
        {
            request.Scopes.AddRange(body.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return request;
    }
}

/// <summary>
/// Les noms JSON sont en snake_case parce que c'est ce que dit la spécification
/// OAuth2 : une bibliothèque cliente enverra grant_type, pas grantType. Sans
/// ces attributs, la liaison échouerait silencieusement et le partenaire
/// recevrait un refus incompréhensible.
/// </summary>
public sealed record TokenRequestDto(
    [property: JsonPropertyName("grant_type")] string GrantType,
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret,
    [property: JsonPropertyName("scope")] string? Scope);
