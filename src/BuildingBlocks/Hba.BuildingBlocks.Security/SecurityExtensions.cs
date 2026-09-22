using Hba.BuildingBlocks.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Hba.BuildingBlocks.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Émetteur : le service Identity.</summary>
    public string Authority { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = "hba-delivery";

    /// <summary>En développement uniquement : autorise un Identity en HTTP.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

public static class SecurityExtensions
{
    /// <summary>
    /// Validation du JWT via les JWKS d'Identity, et politiques d'autorisation
    /// communes. Chaque service appelle ceci : l'autorisation n'est jamais
    /// déléguée au BFF.
    /// </summary>
    public static IServiceCollection AddHbaSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddHttpContextAccessor();
        services.AddScoped<ICallerContext, CallerContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = options.Authority;
                o.Audience = options.Audience;
                o.RequireHttpsMetadata = options.RequireHttpsMetadata;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = HbaClaims.Roles,
                    NameClaimType = "sub",
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(HbaPolicies.Customer, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.Customer))
            .AddPolicy(HbaPolicies.Driver, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.Driver))
            .AddPolicy(HbaPolicies.Merchant, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.MerchantOwner, HbaRoles.MerchantStaff))
            .AddPolicy(HbaPolicies.Partner, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.Partner))
            .AddPolicy(HbaPolicies.BackOffice, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.Admin, HbaRoles.Ops, HbaRoles.Support, HbaRoles.Finance))
            .AddPolicy(HbaPolicies.Admin, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.Admin))
            .AddPolicy(HbaPolicies.Finance, p => p.RequireClaim(HbaClaims.Roles, HbaRoles.Admin, HbaRoles.Finance));

        return services;
    }
}
