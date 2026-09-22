using Microsoft.AspNetCore.Authorization;

namespace Hba.Driver.Api.Grpc;

/// <summary>
/// Entree synchrone du service Driver. Aucune methode n'est encore implementee :
/// chaque appel renvoie UNIMPLEMENTED, ce qui est explicite pour les appelants
/// et ne fait pas croire a un comportement inexistant.
/// </summary>
[Authorize]
public sealed class DriverGrpcService : Hba.Contracts.Driver.V1.DriverService.DriverServiceBase;
