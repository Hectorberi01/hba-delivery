using Microsoft.AspNetCore.Authorization;

namespace Hba.Pricing.Api.Grpc;

/// <summary>
/// Entree synchrone du service Pricing. Aucune methode n'est encore implementee :
/// chaque appel renvoie UNIMPLEMENTED, ce qui est explicite pour les appelants
/// et ne fait pas croire a un comportement inexistant.
/// </summary>
[Authorize]
public sealed class PricingGrpcService : Hba.Contracts.Pricing.V1.PricingService.PricingServiceBase;
