using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hba.BuildingBlocks.Security;

public static class HbaRateLimitPolicies
{
    /// <summary>Demande de code SMS. Le plus strict : chaque envoi coûte de l'argent.</summary>
    public const string OtpRequest = "rate:otp-request";

    /// <summary>Connexion par mot de passe et vérification de code.</summary>
    public const string AuthAttempt = "rate:auth-attempt";

    /// <summary>Émission de jeton partenaire, appelée par des systèmes.</summary>
    public const string PartnerToken = "rate:partner-token";
}

public static class HbaRateLimiting
{
    /// <summary>
    /// Limitation par adresse IP des routes d'authentification.
    ///
    /// Elle est posée AU BFF et non dans Identity, à dessein : Identity ne voit
    /// que l'adresse du BFF, pas celle du client. La limitation par numéro, qui
    /// protège le titulaire du numéro, reste dans Identity ; celle-ci protège
    /// le système d'une enfilade de tentatives depuis une même origine.
    /// </summary>
    public static IServiceCollection AddHbaAuthRateLimiter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy<string>(HbaRateLimitPolicies.OtpRequest, ByClientAddress(permitLimit: 10, minutes: 10));
            options.AddPolicy<string>(HbaRateLimitPolicies.AuthAttempt, ByClientAddress(permitLimit: 30, minutes: 5));
            options.AddPolicy<string>(HbaRateLimitPolicies.PartnerToken, ByClientAddress(permitLimit: 60, minutes: 1));

            // Méthode nommée plutôt que lambda : OnRejected est un délégué
            // renvoyant ValueTask, et une lambda async écrite là demande au
            // compilateur d'en déduire le type. Une conversion de groupe de
            // méthodes ne lui laisse rien à deviner.
            options.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    /// <summary>
    /// Réponse au refus : 429, un en-tête Retry-After quand le limiteur sait
    /// dire combien de temps attendre, et un corps au même format que les
    /// autres erreurs des BFF.
    /// </summary>
    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "RATE_LIMITED", message = "Trop de tentatives. Réessayez dans un instant." },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Derrière Traefik, l'adresse vue par le BFF est celle du routeur. Sans
    /// cette configuration, toutes les requêtes partageraient une seule
    /// partition et la limitation punirait tout le monde d'un coup.
    ///
    /// ForwardedHeaders:TrustAllProxies ne doit être vrai que lorsque le BFF
    /// n'est joignable QUE par le routeur — c'est le cas dans le compose de
    /// production, où seul le réseau public traverse Traefik.
    /// </summary>
    public static IServiceCollection AddHbaForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            if (configuration.GetValue("ForwardedHeaders:TrustAllProxies", defaultValue: false))
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        return services;
    }

    private static Func<HttpContext, RateLimitPartition<string>> ByClientAddress(int permitLimit, int minutes)
        => context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(minutes),
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    /// <summary>
    /// Clé de partition : l'adresse du client. Une requête sans adresse — cas
    /// des tests en mémoire — tombe dans une partition commune plutôt que
    /// d'échapper à la limitation.
    /// </summary>
    private static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
